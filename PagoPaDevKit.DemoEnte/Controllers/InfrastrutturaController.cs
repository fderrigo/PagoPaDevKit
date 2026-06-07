using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PagoPaDevKit.Client;

namespace PagoPaDevKit.DemoEnte.Controllers;

// Area "Infrastruttura": stato servizi live, scenari end-to-end, strumenti.
[Route("infrastruttura")]
public class InfrastrutturaController : Controller
{
    readonly ServiceHealthService _health;
    readonly ScenarioRunner _scenari;
    readonly EnteDbContext _db;
    readonly PagoPaGpdClient _gpd;
    readonly ApiConfigClient _apiConfig;
    readonly GeneratoreIuv _iuvGen;
    readonly IHttpClientFactory _http;
    readonly string _ofc, _companyName, _key;
    readonly int _seg;

    public InfrastrutturaController(ServiceHealthService health, ScenarioRunner scenari, EnteDbContext db,
        PagoPaGpdClient gpd, ApiConfigClient apiConfig, GeneratoreIuv iuvGen, IHttpClientFactory http, IConfiguration cfg)
    {
        _health = health; _scenari = scenari; _db = db; _gpd = gpd; _apiConfig = apiConfig; _iuvGen = iuvGen; _http = http;
        _ofc = cfg["Ente:FiscalCode"] ?? "77777777777";
        _companyName = cfg["Ente:CompanyName"] ?? "Comune Demo";
        _key = cfg["PagoPaGpd:SubscriptionKey"] ?? "dev-gpd-key";
        _seg = cfg.GetValue("Ente:SegregationCode", 47);
    }

    // ===== Stato servizi =====
    [HttpGet("")]
    public async Task<IActionResult> Index() =>
        View(new StatoServiziVm { Servizi = await _health.CheckAllAsync() });

    [HttpGet("health")]
    public async Task<IActionResult> Health()
    {
        var s = await _health.CheckAllAsync();
        return Json(new { services = s.Select(x => new { name = x.Name, url = x.Url, healthy = x.Healthy, latencyMs = x.LatencyMs, detail = x.Detail }) });
    }

    // ===== Scenari di test =====
    [HttpGet("scenari")]
    public IActionResult Scenari() => View(new ScenariVm { Scenari = ScenarioRunner.Catalog });

    [HttpPost("scenari/run")]
    public async Task<IActionResult> RunScenario([FromQuery] string key)
    {
        var res = await _scenari.RunAsync(key);
        return Json(res);
    }

    // ===== Strumenti =====
    [HttpGet("strumenti")]
    public async Task<IActionResult> Strumenti()
    {
        var ibans = await Safe(() => _apiConfig.GetIbansAsync(_ofc), new List<IbanDto>());
        return View(new StrumentiVm { Ibans = ibans, Ofc = _ofc, CompanyName = _companyName, SegregationCode = _seg });
    }

    // Generatore IUV/NAV interattivo (consuma il progressivo: e una demo).
    [HttpGet("strumenti/iuv")]
    public IActionResult GeneraIuv()
    {
        string iuv = _iuvGen.NextIuv();
        string nav = GeneratoreIuv.NavFromIuv(iuv);
        return Json(new
        {
            iuv, nav,
            segregation = iuv[..2],
            progressivo = iuv.Substring(2, 13),
            check = iuv[15..],
            auxDigit = "3"
        });
    }

    [HttpPost("strumenti/reset")]
    public async Task<IActionResult> Reset()
    {
        _db.WebhookLogs.RemoveRange(_db.WebhookLogs);
        _db.Dovuti.RemoveRange(_db.Dovuti);
        await _db.SaveChangesAsync();
        TempData["Toast"] = "Dati locali del gestionale azzerati (posizioni + log webhook).";
        TempData["ToastKind"] = "warning";
        return Redirect("/infrastruttura/strumenti");
    }

    // Reset COMPLETO: azzera anche i dati dell'ente su GPD (posizioni, ricevute, marche, FdR) via
    // l'endpoint dev di GPD, oltre al DB locale. NON tocca IBAN e tassonomie (vivono su ApiConfig).
    [HttpPost("strumenti/reset-completo")]
    public async Task<IActionResult> ResetCompleto()
    {
        string gpdMsg = "";
        try
        {
            var c = _http.CreateClient("gpd-raw");
            var req = new HttpRequestMessage(HttpMethod.Post, $"/dev/reset/{_ofc}");
            req.Headers.Add("Ocp-Apim-Subscription-Key", _key);
            var resp = await c.SendAsync(req);
            if (resp.IsSuccessStatusCode) gpdMsg = await resp.Content.ReadAsStringAsync();
        }
        catch { gpdMsg = "(GPD non raggiungibile)"; }

        _db.WebhookLogs.RemoveRange(_db.WebhookLogs);
        _db.Dovuti.RemoveRange(_db.Dovuti);
        await _db.SaveChangesAsync();

        TempData["ToastKind"] = "warning";
        TempData["Toast"] = "Reset completo: posizioni, ricevute, marche da bollo e FdR su GPD + dati locali azzerati. IBAN e tassonomie conservati.";
        return Redirect("/infrastruttura/strumenti");
    }

    static readonly (string Desc, long Amount, string Cf, string Name, string Cat)[] SeedData =
    {
        ("TARI 2026 - rata unica", 12750, "RSSMRA80A01H501U", "Mario Rossi", "9/0101108TS/"),
        ("IMU 2026 - acconto", 24500, "VRDLGI75M12F205X", "Luigi Verdi", "9/0101108TS/"),
        ("Mensa scolastica - aprile", 4500, "BNCNNA90T45H501Y", "Anna Bianchi", "9/0301102ME/"),
        ("Multa CDS art.142", 17300, "FRRPLA82B11L219Z", "Paolo Ferrari", "9/0201101SA/"),
        ("Retta asilo nido - maggio", 32000, "GLLSRA88D62H501T", "Sara Galli", "9/0301102ME/"),
        ("Canone occupazione suolo", 8900, "CNTMRC79E15F839W", "Marco Conti", "9/0101108TS/"),
        ("Diritti di segreteria", 1600, "RMNLCU84H03G273Q", "Luca Romano", "9/0101108TS/"),
        ("TARI 2026 - II rata", 6375, "RSSMRA80A01H501U", "Mario Rossi", "9/0101108TS/"),
        ("Servizio idrico - bimestre", 5420, "MRTGRG77P20A662R", "Giorgio Marotta", "9/0101108TS/"),
        ("Bollo concessione edilizia", 7800, "DLLFNC86S55D612K", "Francesca Dell'Olio", "9/0101108TS/"),
    };
    const string SeedIban = "IT60X0542811101000000123456";

    [HttpPost("strumenti/seed")]
    public async Task<IActionResult> Seed()
    {
        try { await _apiConfig.CreateIbanAsync(_ofc, new IbanDto { Iban = SeedIban, CompanyName = _companyName, IsActive = true, ValidityDate = DateTimeOffset.UtcNow }); }
        catch { }

        int created = 0, rng = 0;
        foreach (var s in SeedData)
        {
            string iuv = _iuvGen.NextIuv();
            string iupd = $"{_ofc}-{iuv}";
            var due = DateTimeOffset.UtcNow.AddDays(15 + (rng++ * 7) % 60);
            var req = new PaymentPositionRequest
            {
                Iupd = iupd, CompanyName = _companyName,
                PaymentOption = new()
                {
                    new PaymentOptionDto
                    {
                        Description = s.Desc,
                        Debtor = new DebtorDto { Type = "F", FiscalCode = s.Cf, FullName = s.Name },
                        Installments = new()
                        {
                            new InstallmentDto
                            {
                                Iuv = iuv, Amount = s.Amount, Description = s.Desc, DueDate = due,
                                Transfer = new() { new TransferDto { IdTransfer = "1", Amount = s.Amount, RemittanceInformation = s.Desc, Iban = SeedIban, Category = s.Cat,
                                    TransferMetadata = new() { new MetadataKv { Key = "codiceCapitolo", Value = "1.01.01" }, new MetadataKv { Key = "codiceUfficio", Value = "TRIB" } } } }
                            }
                        }
                    }
                }
            };
            try
            {
                var resp = await _gpd.CreateDebtPositionAsync(_ofc, req, toPublish: true);
                _db.Dovuti.Add(new Dovuto
                {
                    Iupd = iupd, Iuv = iuv, Nav = resp.FirstNav() ?? "",
                    DebtorFiscalCode = s.Cf, DebtorName = s.Name, Description = s.Desc,
                    Amount = s.Amount, DueDate = due, Status = "DA PAGARE", CreatedAt = DateTimeOffset.UtcNow
                });
                created++;
            }
            catch { }
        }
        await _db.SaveChangesAsync();
        TempData["Toast"] = $"Create {created} posizioni demo.";
        return Redirect("/ente/posizioni");
    }

    static async Task<T> Safe<T>(Func<Task<T>> f, T fallback)
    {
        try { return await f(); } catch { return fallback; }
    }
}
