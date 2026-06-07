using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PagoPaDevKit.Client;
using QRCoder;

namespace PagoPaDevKit.DemoEnte.Controllers;

// Area "Test Ente": ciclo di vita del dovuto lato Ente Creditore.
[Route("ente")]
public class EnteController : Controller
{
    readonly EnteDbContext _db;
    readonly PagoPaGpdClient _gpd;
    readonly ApiConfigClient _apiConfig;
    readonly GeneratoreIuv _iuvGen;
    readonly FdrService _fdr;
    readonly IConfiguration _cfg;
    readonly string _ofc, _companyName, _codiceSia, _pspId;

    public EnteController(EnteDbContext db, PagoPaGpdClient gpd, ApiConfigClient apiConfig, GeneratoreIuv iuvGen, FdrService fdr, IConfiguration cfg)
    {
        _db = db; _gpd = gpd; _apiConfig = apiConfig; _iuvGen = iuvGen; _fdr = fdr; _cfg = cfg;
        _ofc = cfg["Ente:FiscalCode"] ?? "77777777777";
        _companyName = cfg["Ente:CompanyName"] ?? "Comune Demo";
        _codiceSia = cfg["Ente:CodiceSia"] ?? "33333";
        _pspId = cfg["Ente:IdPsp"] ?? "BANCADEMO";
    }

    // Enti beneficiari (organizationFiscalCode) per le select dello split: piattaforma (l'EC) + altri da config.
    List<EnteBeneficiarioVm> EntiBeneficiari()
    {
        var enti = new List<EnteBeneficiarioVm> { new(_ofc, _companyName, true) };
        foreach (var c in _cfg.GetSection("Ente:EntiBeneficiari").GetChildren())
        {
            var cf = c["cf"]; var nome = c["nome"];
            if (!string.IsNullOrWhiteSpace(cf)) enti.Add(new(cf, nome ?? cf, false));
        }
        return enti;
    }

    // Typeahead tassonomia ufficiale (proxy su ApiConfig). Restituisce i primi risultati per q.
    [HttpGet("tassonomie")]
    public async Task<IActionResult> Tassonomie([FromQuery] string? q)
    {
        var list = await Safe(() => _apiConfig.SearchTaxonomiesAsync(q, 50), new List<TaxonomyDto>());
        return Json(list.Select(t => new { code = t.CodiceTassonomico, label = t.DenominazioneServizio, tipoEnte = t.TipoEnte }));
    }

    [HttpGet("")]
    public IActionResult Index() => Redirect("/ente/posizioni");

    // ===== Crea dovuto =====
    [HttpGet("crea")]
    public async Task<IActionResult> Crea()
    {
        var enti = EntiBeneficiari();
        // Registro IBAN per OGNI ente (piattaforma + secondari): ogni ente ha i suoi IBAN su ApiConfig.
        var ibansByEnte = new Dictionary<string, List<IbanDto>>();
        foreach (var e in enti)
            ibansByEnte[e.Cf] = (await Safe(() => _apiConfig.GetIbansAsync(e.Cf), new List<IbanDto>())).Where(i => i.IsActive).ToList();

        var vm = new CreaDovutoVm
        {
            CompanyName = _companyName, Ofc = _ofc,
            Enti = enti,
            IbansByEnte = ibansByEnte,
            Ibans = ibansByEnte.TryGetValue(_ofc, out var pl) ? pl : new List<IbanDto>(),
            CreatedNav = TempData["CreatedNav"] as string,
            CreatedIuv = TempData["CreatedIuv"] as string,
            ErrorMessage = TempData["CreaError"] as string
        };
        return View(vm);
    }

    // POST /dovuti : crea+pubblica su GPD v3 (client "stupido": nessuna validazione, errori dal backend).
    [HttpPost("/dovuti")]
    public async Task<IActionResult> Salva([FromForm] string debtorFiscalCode, [FromForm] string debtorName,
        [FromForm] string description, [FromForm] string amountEuro, [FromForm] string dueDate,
        [FromForm] string category, [FromForm] string iban)
    {
        long amount = decimal.TryParse(amountEuro, NumberStyles.Any, CultureInfo.InvariantCulture, out var eur)
            ? (long)Math.Round(eur * 100) : 0;
        var due = DateTimeOffset.TryParse(dueDate, out var d) ? d : DateTimeOffset.UtcNow;
        string iuv = _iuvGen.NextIuv();
        string iupd = $"{_ofc}-{iuv}";

        var request = new PaymentPositionRequest
        {
            Iupd = iupd, CompanyName = _companyName,
            PaymentOption = new()
            {
                new PaymentOptionDto
                {
                    Description = description,
                    Debtor = new DebtorDto { Type = "F", FiscalCode = debtorFiscalCode, FullName = debtorName },
                    Installments = new()
                    {
                        new InstallmentDto
                        {
                            Iuv = iuv, Amount = amount, Description = description, DueDate = due,
                            Transfer = new()
                            {
                                new TransferDto
                                {
                                    IdTransfer = "1", Amount = amount, RemittanceInformation = description, Iban = iban, Category = category,
                                    TransferMetadata = new()
                                    {
                                        new MetadataKv { Key = "codiceCapitolo", Value = "1.01.01" },
                                        new MetadataKv { Key = "codiceUfficio", Value = "TRIB" }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        try
        {
            var created = await _gpd.CreateDebtPositionAsync(_ofc, request, toPublish: true);
            _db.Dovuti.Add(new Dovuto
            {
                Iupd = iupd, Iuv = iuv, Nav = created.FirstNav() ?? "",
                DebtorFiscalCode = debtorFiscalCode, DebtorName = debtorName,
                Description = description, Amount = amount, DueDate = due,
                Status = "DA PAGARE", CreatedAt = DateTimeOffset.UtcNow
            });
            await _db.SaveChangesAsync();
            TempData["CreatedNav"] = created.FirstNav();
            TempData["CreatedIuv"] = iuv;
            TempData["Toast"] = "Posizione creata e pubblicata.";
            return Redirect("/ente/crea");
        }
        catch (PagoPaApiException ex)
        {
            TempData["CreaError"] = $"GPD ha rifiutato la creazione (HTTP {ex.StatusCode}): {ex.ResponseBody}";
            TempData["ToastKind"] = "danger";
            TempData["Toast"] = "Creazione rifiutata da GPD.";
            return Redirect("/ente/crea");
        }
    }

    // POST /ente/posizioni : creazione COMPLETA dal builder (N opzioni alternative, N rate,
    // split multi-transfer, marca da bollo). Riceve il modello v3 in JSON; assegna IUV per rata
    // e idTransfer, poi inoltra a GPD. Client "stupido": nessuna validazione qui.
    [HttpPost("/ente/posizioni")]
    public async Task<IActionResult> CreaAvanzata([FromBody] PaymentPositionRequest req)
    {
        if (req?.PaymentOption is null || req.PaymentOption.Count == 0)
            return BadRequest(new { ok = false, error = "Almeno un'opzione di pagamento e richiesta." });

        // Assegna IUV a ogni rata e idTransfer progressivo a ogni transfer.
        foreach (var po in req.PaymentOption)
        {
            po.Debtor ??= new DebtorDto();
            if (string.IsNullOrEmpty(po.Debtor.Type)) po.Debtor.Type = "F";
            foreach (var inst in po.Installments)
            {
                inst.Iuv = _iuvGen.NextIuv();
                inst.Nav = null;   // GPD lo deriva dall'IUV
                int ti = 1;
                foreach (var t in inst.Transfer)
                {
                    t.IdTransfer = (ti++).ToString();
                    if (string.IsNullOrEmpty(t.RemittanceInformation)) t.RemittanceInformation = inst.Description;
                    if (t.Stamp is not null && string.IsNullOrEmpty(t.Stamp.HashDocument)) t.Stamp = null; // bollo vuoto -> niente
                }
            }
        }

        string firstIuv = req.PaymentOption[0].Installments.FirstOrDefault()?.Iuv ?? _iuvGen.NextIuv();
        string iupd = $"{_ofc}-{firstIuv}";
        req.Iupd = iupd;
        req.CompanyName = _companyName;

        try
        {
            var created = await _gpd.CreateDebtPositionAsync(_ofc, req, toPublish: true);
            var firstOpt = req.PaymentOption[0];
            long total = firstOpt.Installments.Sum(i => i.Amount);
            _db.Dovuti.Add(new Dovuto
            {
                Iupd = iupd, Iuv = firstIuv, Nav = created.FirstNav() ?? GeneratoreIuv.NavFromIuv(firstIuv),
                DebtorFiscalCode = firstOpt.Debtor.FiscalCode, DebtorName = firstOpt.Debtor.FullName,
                Description = firstOpt.Description,
                Amount = total, DueDate = firstOpt.Installments.FirstOrDefault()?.DueDate ?? DateTimeOffset.UtcNow,
                Status = "DA PAGARE", CreatedAt = DateTimeOffset.UtcNow
            });
            await _db.SaveChangesAsync();
            TempData["Toast"] = "Posizione creata e pubblicata.";
            return Json(new { ok = true, iupd, redirect = $"/ente/posizioni/{Uri.EscapeDataString(iupd)}" });
        }
        catch (PagoPaApiException ex)
        {
            return StatusCode(ex.StatusCode == 0 ? 500 : ex.StatusCode, new { ok = false, error = ex.ResponseBody ?? ex.Message });
        }
    }

    // ===== Posizioni debitorie =====
    [HttpGet("posizioni")]
    public async Task<IActionResult> Posizioni([FromQuery] string? stato, [FromQuery] string? q)
    {
        var list = (await _db.Dovuti.ToListAsync()).OrderByDescending(x => x.CreatedAt).ToList();
        foreach (var dv in list)
        {
            try
            {
                var pos = await _gpd.GetDebtPositionAsync(_ofc, dv.Iupd);
                var inst = pos?.PaymentOption.FirstOrDefault()?.Installments.FirstOrDefault();
                if (inst is not null) dv.Status = MapStato.From(pos!.Status, inst.Status);
                if (pos is not null) PosShape.Apply(dv, pos);
            }
            catch { }
        }
        await _db.SaveChangesAsync();

        IEnumerable<Dovuto> filtered = list;
        if (!string.IsNullOrWhiteSpace(stato)) filtered = filtered.Where(x => x.Status == stato);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var qq = q.Trim();
            filtered = filtered.Where(x => x.Nav.Contains(qq) || x.DebtorFiscalCode.Contains(qq, StringComparison.OrdinalIgnoreCase));
        }
        return View(new PosizioniVm { Dovuti = filtered.ToList(), Ofc = _ofc, Stato = stato, Q = q });
    }

    // ===== Dettaglio posizione =====
    [HttpGet("posizioni/{iupd}")]
    public async Task<IActionResult> Dettaglio(string iupd)
    {
        var d = await _db.Dovuti.FirstOrDefaultAsync(x => x.Iupd == iupd);
        if (d is null) return NotFound();
        PaymentPositionResponse? pos = null;
        string raw = "(GPD non raggiungibile)";
        try
        {
            pos = await _gpd.GetDebtPositionAsync(_ofc, iupd);
            if (pos is not null)
            {
                raw = JsonSerializer.Serialize(pos, new JsonSerializerOptions { WriteIndented = true });
                var inst = pos.PaymentOption.FirstOrDefault()?.Installments.FirstOrDefault();
                d.Status = MapStato.From(pos.Status, inst?.Status);
                await _db.SaveChangesAsync();
            }
        }
        catch (Exception ex) { raw = $"Errore lettura GPD: {ex.Message}"; }
        return View(new DettaglioVm { Dovuto = d, Posizione = pos, RawJson = raw, Ofc = _ofc });
    }

    [HttpPost("posizioni/{iupd}/pubblica")]
    public async Task<IActionResult> Pubblica(string iupd)
    {
        try { await _gpd.PublishAsync(_ofc, iupd); TempData["Toast"] = "Posizione pubblicata."; }
        catch (PagoPaApiException ex) { TempData["ToastKind"] = "danger"; TempData["Toast"] = $"Pubblicazione fallita: HTTP {ex.StatusCode}."; }
        return Redirect($"/ente/posizioni/{iupd}");
    }

    [HttpPost("posizioni/{iupd}/elimina")]
    public async Task<IActionResult> Elimina(string iupd)
    {
        try
        {
            await _gpd.DeleteAsync(_ofc, iupd);
            var d = await _db.Dovuti.FirstOrDefaultAsync(x => x.Iupd == iupd);
            if (d is not null) { _db.Dovuti.Remove(d); await _db.SaveChangesAsync(); }
            TempData["Toast"] = "Posizione eliminata.";
        }
        catch (PagoPaApiException ex) { TempData["ToastKind"] = "danger"; TempData["Toast"] = $"Eliminazione fallita: HTTP {ex.StatusCode}."; }
        return Redirect("/ente/posizioni");
    }

    // ===== Ricevute =====
    [HttpGet("ricevute")]
    public async Task<IActionResult> Ricevute()
    {
        var list = await Safe(() => _gpd.GetReceiptsAsync(_ofc), new List<ReceiptSummary>());
        return View(new RicevuteVm { Ricevute = list.OrderByDescending(r => r.PaymentDateTime).ToList(), Ofc = _ofc });
    }

    [HttpGet("dovuti/{iuv}/ricevuta")]
    [HttpGet("/dovuti/{iuv}/ricevuta")]
    public async Task<IActionResult> Ricevuta(string iuv, [FromQuery] string? iupd)
    {
        var d = await ResolveRata(iuv, iupd);
        if (d is null) return NotFound();
        var receipt = await Safe(() => _gpd.GetReceiptByIuvAsync(_ofc, iuv), (ReceiptDto?)null);
        return View(new RicevutaVm { Dovuto = d, Receipt = receipt });
    }

    // Risolve una rata per IUV: prima dal DB locale, altrimenti dalla posizione GPD (per rate
    // non in lista: piani rateali, opzioni alternative). Cosi avviso/ricevuta valgono per ogni NAV.
    async Task<Dovuto?> ResolveRata(string iuv, string? iupd)
    {
        var d = await _db.Dovuti.FirstOrDefaultAsync(x => x.Iuv == iuv);
        if (d is not null) return d;
        if (string.IsNullOrEmpty(iupd)) return null;
        var pos = await Safe(() => _gpd.GetDebtPositionAsync(_ofc, iupd), (PaymentPositionResponse?)null);
        if (pos is null) return null;
        foreach (var po in pos.PaymentOption)
            foreach (var i in po.Installments)
                if (i.Iuv == iuv)
                    return new Dovuto
                    {
                        Iupd = pos.Iupd, Iuv = i.Iuv, Nav = i.Nav ?? GeneratoreIuv.NavFromIuv(i.Iuv),
                        Amount = i.Amount, Description = i.Description, DueDate = i.DueDate,
                        DebtorFiscalCode = po.Debtor.FiscalCode, DebtorName = po.Debtor.FullName,
                        Status = MapStato.From(pos.Status, i.Status)
                    };
        return null;
    }

    // ===== Log webhook =====
    [HttpGet("webhook")]
    public async Task<IActionResult> Webhook()
    {
        var logs = (await _db.WebhookLogs.ToListAsync()).OrderByDescending(x => x.ReceivedAt).ToList();
        return View(new WebhookVm { Logs = logs });
    }

    // ===== Stampa avvisi =====
    [HttpGet("avvisi")]
    public async Task<IActionResult> Avvisi()
    {
        var list = (await _db.Dovuti.ToListAsync()).OrderByDescending(x => x.CreatedAt).ToList();
        return View(new AvvisiListVm { Dovuti = list, Ofc = _ofc });
    }

    [HttpGet("dovuti/{iuv}/avviso")]
    [HttpGet("/dovuti/{iuv}/avviso")]
    public async Task<IActionResult> Avviso(string iuv, [FromQuery] string? iupd)
    {
        var d = await ResolveRata(iuv, iupd);
        if (d is null) return NotFound();
        string payload = $"PAGOPA|002|{d.Nav}|{_ofc}|{d.Amount}";
        var gen = new QRCodeGenerator();
        var data = gen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        string qr = Convert.ToBase64String(new PngByteQRCode(data).GetGraphic(6));
        return View(new AvvisoVm
        {
            CompanyName = _companyName, Ofc = _ofc, CodiceSia = _codiceSia,
            Dovuto = d, QrPngBase64 = qr, QrPayload = payload
        });
    }

    // ===== Riconciliazione contabile + Rendicontazione (FdR reali) =====
    [HttpGet("/riconciliazione")]
    public async Task<IActionResult> Riconciliazione()
    {
        var receipts = await Safe(() => _gpd.GetReceiptsAsync(_ofc), new List<ReceiptSummary>());
        var fdrSummaries = await Safe(() => _fdr.ListAsync(), new List<FdrSummary>());

        // Arricchisco ogni FdR con totali + mappa IUV -> flusso (per la quadratura).
        var fdrRows = new List<FdrRow>();
        var iuvToFdr = new Dictionary<string, string>();
        var paysByDay = new Dictionary<DateTime, (int count, decimal sum)>();
        foreach (var f in fdrSummaries)
        {
            var detail = await Safe(() => _fdr.GetAsync(f.Fdr, f.Revision, f.PspId), (FdrDetail?)null);
            var pays = await Safe(() => _fdr.PaymentsAsync(f.Fdr, f.Revision, f.PspId), new List<FdrPaymentRow>());
            fdrRows.Add(new FdrRow
            {
                Fdr = f.Fdr, PspId = f.PspId, Revision = f.Revision, FlowDate = f.FlowDate,
                NumPagamenti = detail?.TotPayments ?? pays.Count,
                Importo = detail?.SumPayments ?? pays.Sum(p => p.Pay)
            });
            foreach (var p in pays)
            {
                iuvToFdr.TryAdd(p.Iuv, f.Fdr);
                var day = p.PayDate.Date;
                var cur = paysByDay.TryGetValue(day, out var v) ? v : (0, 0m);
                paysByDay[day] = (cur.Item1 + 1, cur.Item2 + p.Pay);
            }
        }

        // Quadratura per giornata: RT (incassato) ↔ FdR (rendicontato) ↔ riversamento (banca, MOCK).
        var giorni = receipts
            .GroupBy(r => r.PaymentDateTime.Date)
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var fd = paysByDay.TryGetValue(g.Key, out var v) ? v : (count: 0, sum: 0m);
                return new QuadraturaGiorno
                {
                    Data = g.Key,
                    RtCount = g.Count(), RtImportoCent = g.Sum(r => r.PaymentAmount),
                    FdrCount = fd.count, FdrImporto = fd.sum, Riversato = fd.sum,
                    Pagamenti = g.OrderBy(r => r.Iuv).Select(r => new QuadraturaRiga
                    {
                        Iuv = r.Iuv, Debitore = r.Debtor, ImportoCent = r.PaymentAmount,
                        Rendicontato = iuvToFdr.ContainsKey(r.Iuv),
                        Fdr = iuvToFdr.TryGetValue(r.Iuv, out var fn) ? fn : null
                    }).ToList()
                };
            }).ToList();

        decimal fdrTot = fdrRows.Sum(x => x.Importo);
        int daRend = receipts.Count(r => !iuvToFdr.ContainsKey(r.Iuv));

        return View(new RiconciliazioneVm
        {
            CompanyName = _companyName, Ofc = _ofc, PspId = _pspId,
            RtCount = receipts.Count, RtTotaleCent = receipts.Sum(r => r.PaymentAmount),
            FdrCount = fdrRows.Count, FdrTotale = fdrTot, RiversatoTotale = fdrTot,
            RtDaRendicontare = daRend, TuttoQuadrato = receipts.Count > 0 && daRend == 0,
            Fdr = fdrRows, Giorni = giorni
        });
    }

    // Simula la pubblicazione dei FdR da parte del PSP (dalle ricevute dell'ente). Usa le API FdR
    // reali di GPD lato PSP (create+add+publish); poi l'ente li recupera lato Organization.
    [HttpPost("/riconciliazione/simula")]
    public async Task<IActionResult> SimulaFdr()
    {
        var receipts = await Safe(() => _gpd.GetReceiptsAsync(_ofc), new List<ReceiptSummary>());
        int n = await Safe(() => _fdr.SimulaPubblicazioneAsync(receipts), 0);
        if (n > 0) TempData["Toast"] = $"Pubblicati {n} flussi di rendicontazione (PSP {_pspId}). Le rate citate passano a REPORTED.";
        else { TempData["ToastKind"] = "warning"; TempData["Toast"] = "Nessun nuovo flusso da pubblicare (servono ricevute non ancora rendicontate)."; }
        return Redirect("/riconciliazione");
    }

    [HttpGet("/riconciliazione/fdr/{pspId}/{fdr}/{rev:int}")]
    public async Task<IActionResult> FdrDettaglio(string pspId, string fdr, int rev)
    {
        var detail = await Safe(() => _fdr.GetAsync(fdr, rev, pspId), (FdrDetail?)null);
        if (detail is null) return NotFound();
        var payments = await Safe(() => _fdr.PaymentsAsync(fdr, rev, pspId), new List<FdrPaymentRow>());
        return View(new FdrDettaglioVm { Detail = detail, Payments = payments, Ofc = _ofc, Revision = rev, PspId = pspId });
    }

    // FlussoRiversamento 1.0.4 firmato (XML-DSig) — download.
    [HttpGet("/riconciliazione/fdr/{pspId}/{fdr}/{rev:int}/xml")]
    public async Task<IActionResult> FdrXml(string pspId, string fdr, int rev)
    {
        var xml = await Safe(() => _fdr.FlussoRiversamentoXmlAsync(fdr, rev, pspId), (string?)null);
        if (xml is null) return NotFound();
        return File(System.Text.Encoding.UTF8.GetBytes(xml), "application/xml", $"FlussoRiversamento_{fdr}.xml");
    }

    // ===== Esito callback dal Checkout (returnUrl) =====
    [HttpGet("/esito")]
    public IActionResult Esito([FromQuery] string? outcome, [FromQuery] string[] nav) =>
        View(new EsitoEnteVm { Outcome = outcome ?? "", Navs = (nav ?? Array.Empty<string>()).ToList() });

    static async Task<T> Safe<T>(Func<Task<T>> f, T fallback)
    {
        try { return await f(); } catch { return fallback; }
    }
}
