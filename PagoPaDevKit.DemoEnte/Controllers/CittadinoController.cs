using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PagoPaDevKit.Client;

namespace PagoPaDevKit.DemoEnte.Controllers;

// Area "Test Cittadino": il console si comporta da PSP e percorre il flusso di pagamento
// verso il NODO (verify -> activate -> notify -> outcome), piu opzioni di pagamento,
// pagamento spontaneo (on-demand) e carte di test.
[Route("cittadino")]
public class CittadinoController : Controller
{
    readonly EnteDbContext _db;
    readonly NodeForPspClient _node;
    readonly ApiConfigClient _apiConfig;
    readonly IHttpClientFactory _http;
    readonly string _ofc, _key;
    readonly decimal _feeEuro;

    public CittadinoController(EnteDbContext db, NodeForPspClient node, ApiConfigClient apiConfig, IHttpClientFactory http, IConfiguration cfg)
    {
        _db = db; _node = node; _apiConfig = apiConfig; _http = http;
        _ofc = cfg["Ente:FiscalCode"] ?? "77777777777";
        _key = cfg["PagoPaGpd:SubscriptionKey"] ?? "dev-gpd-key";
        _feeEuro = cfg.GetValue("Ente:FeeCents", 150) / 100m;
    }

    [HttpGet("")]
    public IActionResult Index() => Redirect("/cittadino/paga");

    // ===== Paga un avviso (stepper) =====
    [HttpGet("paga")]
    public async Task<IActionResult> Paga([FromQuery] string? nav)
    {
        var ultimo = (await _db.Dovuti.ToListAsync()).OrderByDescending(x => x.CreatedAt).FirstOrDefault()?.Nav;
        return View(new PagaCittadinoVm
        {
            Step = 1, Ofc = _ofc, FiscalCode = _ofc, Nav = nav ?? "", UltimoNav = ultimo,
            Carte = TestCards.All
        });
    }

    [HttpPost("paga")]
    public async Task<IActionResult> Paga([FromForm] string fase, [FromForm] string nav, [FromForm] string fiscalCode, [FromForm] string? pan)
    {
        var vm = new PagaCittadinoVm { Ofc = _ofc, Nav = nav, FiscalCode = fiscalCode, Carte = TestCards.All };

        // Step 1 -> 2: verifica avviso.
        var v = await _node.VerifyAsync(fiscalCode, nav);
        if (!v.Ok)
        {
            vm.Step = 1; vm.Ok = false;
            vm.Message = $"Avviso non pagabile: {v.Fault?.Display}";
            vm.FaultCode = v.Fault?.Display;
            vm.UltimoNav = (await _db.Dovuti.ToListAsync()).OrderByDescending(x => x.CreatedAt).FirstOrDefault()?.Nav;
            return View(vm);
        }
        vm.CompanyName = v.CompanyName; vm.Description = v.Description; vm.AmountEuro = v.AmountEuro; vm.FeeEuro = _feeEuro;

        if (fase == "verifica")
        {
            vm.Step = 2;   // mostra riepilogo + form carta
            return View(vm);
        }

        // fase == "paga": classifica carta e processa.
        var outcome = TestCards.Classify(pan);
        if (outcome == CardOutcome.Unknown)
        {
            vm.Step = 2; vm.Ok = false;
            vm.Message = "Carta non riconosciuta: usa una delle carte di test.";
            return View(vm);
        }

        vm.Step = 4;
        if (outcome != CardOutcome.Ok)
        {
            vm.Ok = false;
            vm.Message = outcome == CardOutcome.Expired ? "Carta scaduta: pagamento non riuscito." : "Autorizzazione negata dalla banca.";
            return View(vm);
        }

        // Carta OK: activate -> notify -> outcome.
        var a = await _node.ActivateAsync(fiscalCode, nav, v.AmountEuro);
        if (!a.Ok) { vm.Ok = false; vm.Message = $"Attivazione fallita: {a.Fault?.Display}"; vm.FaultCode = a.Fault?.Display; return View(vm); }

        var (notified, nfault) = await _node.PspNotifyAsync(fiscalCode, a.TotalAmount, _feeEuro,
            new[] { new NodeForPspClient.NotifyItem(a.PaymentToken, a.TotalAmount, a.CompanyName) });
        if (!notified) { vm.Ok = false; vm.Message = $"Notifica fallita: {nfault?.Display}"; return View(vm); }

        var (ok, fault) = await _node.SendOutcomeAsync(new[] { a.PaymentToken }, ok: true, _feeEuro, fiscalCode, null);
        if (!ok) { vm.Ok = false; vm.Message = $"Invio esito fallito: {fault?.Display}"; return View(vm); }

        vm.Ok = true; vm.Message = "Pagamento riuscito. La ricevuta (RT) e disponibile su GPD.";
        vm.AmountEuro = a.TotalAmount;
        var d = await _db.Dovuti.FirstOrDefaultAsync(x => x.Nav == nav);
        vm.ReceiptId = d?.ReceiptId;
        return View(vm);
    }

    // ===== Opzioni di pagamento =====
    [HttpGet("opzioni")]
    public async Task<IActionResult> Opzioni([FromQuery] string? nav)
    {
        var vm = new OpzioniCittadinoVm { Ofc = _ofc, Nav = nav ?? "" };
        if (string.IsNullOrWhiteSpace(nav)) return View(vm);

        var client = _http.CreateClient("gpd-raw");
        var req = new HttpRequestMessage(HttpMethod.Get, $"/payment-options/organizations/{_ofc}/notices/{nav}");
        req.Headers.Add("Ocp-Apim-Subscription-Key", _key);
        HttpResponseMessage resp;
        try { resp = await client.SendAsync(req); }
        catch (Exception ex) { vm.Error = $"GPD non raggiungibile: {ex.Message}"; return View(vm); }

        if (!resp.IsSuccessStatusCode)
        {
            vm.Error = $"payment-options ha risposto HTTP {(int)resp.StatusCode}.";
            return View(vm);
        }
        string body = await resp.Content.ReadAsStringAsync();
        vm.RawJson = body;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            vm.Status = root.TryGetProperty("status", out var st) ? st.GetString() : null;
            foreach (var po in root.GetProperty("paymentOption").EnumerateArray())
                foreach (var i in po.GetProperty("installments").EnumerateArray())
                    vm.Rate.Add(new OpzioneRata
                    {
                        Nav = i.TryGetProperty("nav", out var n) ? n.GetString() ?? "" : "",
                        Description = i.TryGetProperty("description", out var de) ? de.GetString() ?? "" : "",
                        Amount = i.TryGetProperty("amount", out var am) ? am.GetInt64() : 0,
                        DueDate = i.TryGetProperty("dueDate", out var dd) && dd.TryGetDateTimeOffset(out var d) ? d : null,
                        Status = i.TryGetProperty("status", out var ss) ? ss.GetString() ?? "" : ""
                    });
        }
        catch (Exception ex) { vm.Error = $"Parsing risposta fallito: {ex.Message}"; }
        return View(vm);
    }

    // ===== Pagamento spontaneo (on-demand) =====
    [HttpGet("spontaneo")]
    public async Task<IActionResult> Spontaneo()
    {
        return View(new SpontaneoVm
        {
            Ofc = _ofc,
            Servizi = await Safe(() => _apiConfig.GetTaxonomiesAsync(), new List<TaxonomyDto>())
        });
    }

    [HttpPost("spontaneo")]
    public async Task<IActionResult> Spontaneo([FromForm] string idServizio, [FromForm] string debtorFiscalCode,
        [FromForm] string debtorName, [FromForm] string causale, [FromForm] string amountEuro)
    {
        var vm = new SpontaneoVm { Ofc = _ofc, Servizi = await Safe(() => _apiConfig.GetTaxonomiesAsync(), new List<TaxonomyDto>()) };
        decimal amount = decimal.TryParse(amountEuro, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var a) ? a : 0;
        var res = await _node.DemandAsync(_ofc, idServizio, debtorFiscalCode, amount, causale, debtorName);
        if (res.Ok)
        {
            vm.ResultNav = res.Nav;
            // L'avviso spontaneo e una posizione GPD (Origin=SPONTANEO, IUPD=SPONT-{iuv}).
            // Registro la riga locale cosi compare in "Posizioni debitorie" come ogni dovuto.
            string nav = res.Nav;
            string iuv = nav.Length == 18 && nav.StartsWith('3') ? nav[1..] : nav;
            if (!await _db.Dovuti.AnyAsync(x => x.Nav == nav))
            {
                _db.Dovuti.Add(new Dovuto
                {
                    Iupd = $"SPONT-{iuv}", Iuv = iuv, Nav = nav,
                    DebtorFiscalCode = debtorFiscalCode, DebtorName = debtorName,
                    Description = $"[spontaneo] {causale}", Amount = (long)Math.Round(amount * 100),
                    DueDate = DateTimeOffset.UtcNow.AddDays(30), Status = "DA PAGARE", CreatedAt = DateTimeOffset.UtcNow
                });
                await _db.SaveChangesAsync();
            }
        }
        else { vm.Error = $"Generazione avviso fallita: {res.Fault?.Display}"; }
        return View(vm);
    }

    // ===== Carte di test =====
    [HttpGet("carte")]
    public async Task<IActionResult> Carte()
    {
        var ultimo = (await _db.Dovuti.ToListAsync()).OrderByDescending(x => x.CreatedAt).FirstOrDefault()?.Nav;
        return View(new CarteVm { Carte = TestCards.All, UltimoNav = ultimo, Ofc = _ofc });
    }

    static async Task<T> Safe<T>(Func<Task<T>> f, T fallback)
    {
        try { return await f(); } catch { return fallback; }
    }
}
