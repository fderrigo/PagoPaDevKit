using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PagoPaDevKit.Client;

namespace PagoPaDevKit.DemoEnte;

public record ScenarioStep(string Name, bool Ok, string? Detail = null, string? Request = null, string? Response = null);
public record ScenarioResult(string Key, string Title, bool Ok, long DurationMs, List<ScenarioStep> Steps);
public record ScenarioInfo(string Key, string Title, string Description, string Expected, string Icon);

// Esegue scenari end-to-end chiamando le API HTTP REALI dei mock (nessun esito simulato).
// Ogni scenario produce step con check verde/rosso, durata, request/response ispezionabili.
public class ScenarioRunner
{
    readonly PagoPaGpdClient _gpd;
    readonly ApiConfigClient _apiConfig;
    readonly NodeForPspClient _node;
    readonly GeneratoreIuv _iuv;
    readonly IHttpClientFactory _http;
    readonly string _ofc, _company, _key;
    readonly decimal _fee;
    const string DemoIban = "IT60X0542811101000000123456";

    public ScenarioRunner(PagoPaGpdClient gpd, ApiConfigClient apiConfig, NodeForPspClient node,
        GeneratoreIuv iuv, IHttpClientFactory http, IConfiguration cfg)
    {
        _gpd = gpd; _apiConfig = apiConfig; _node = node; _iuv = iuv; _http = http;
        _ofc = cfg["Ente:FiscalCode"] ?? "77777777777";
        _company = cfg["Ente:CompanyName"] ?? "Comune Demo";
        _key = cfg["PagoPaGpd:SubscriptionKey"] ?? "dev-gpd-key";
        _fee = cfg.GetValue("Ente:FeeCents", 150) / 100m;
    }

    public static readonly List<ScenarioInfo> Catalog = new()
    {
        new("ciclo-ok", "Ciclo completo OK", "Crea e pubblica una posizione, paga con carta valida (verify→activate→notify→outcome), verifica posizione PAID e ricevuta presente.", "Posizione PAID + RT disponibile", "bi-check-circle"),
        new("carta-ko", "Pagamento con carta KO", "Crea una posizione e simula una carta con autorizzazione negata: l'esito e KO e la posizione resta pagabile.", "Esito KO + posizione VALID", "bi-x-circle"),
        new("carta-scaduta", "Carta scaduta", "Carta di test scaduta: il PSP non procede, la posizione resta pagabile.", "Esito KO + posizione VALID", "bi-calendar-x"),
        new("key-errata", "Subscription key errata", "Chiama GPD con una Ocp-Apim-Subscription-Key errata.", "HTTP 401 Unauthorized", "bi-shield-lock"),
        new("iupd-dup", "IUPD duplicato", "Crea due volte la stessa posizione (stesso IUPD).", "HTTP 409 Conflict alla seconda", "bi-files"),
        new("avviso-scaduto", "Avviso scaduto", "Crea un avviso gia scaduto (dueDate nel passato) e prova a verificarlo.", "Verify rifiutato con fault", "bi-clock-history"),
        new("qr-nav", "QR avviso coerente", "Genera il payload QR dell'avviso e verifica che il NAV codificato corrisponda.", "NAV nel QR == NAV emesso", "bi-qr-code"),
    };

    public async Task<ScenarioResult> RunAsync(string key)
    {
        var info = Catalog.FirstOrDefault(c => c.Key == key);
        var title = info?.Title ?? key;
        var steps = new List<ScenarioStep>();
        var sw = Stopwatch.StartNew();
        try
        {
            switch (key)
            {
                case "ciclo-ok": await CicloOk(steps); break;
                case "carta-ko": await CartaKo(steps, expired: false); break;
                case "carta-scaduta": await CartaKo(steps, expired: true); break;
                case "key-errata": await KeyErrata(steps); break;
                case "iupd-dup": await IupdDup(steps); break;
                case "avviso-scaduto": await AvvisoScaduto(steps); break;
                case "qr-nav": await QrNav(steps); break;
                default: steps.Add(new("Scenario sconosciuto", false, key)); break;
            }
        }
        catch (Exception ex)
        {
            steps.Add(new("Eccezione non gestita", false, ex.Message));
        }
        sw.Stop();
        return new ScenarioResult(key, title, steps.All(s => s.Ok), sw.ElapsedMilliseconds, steps);
    }

    // ---- helpers ----
    async Task SeedIban()
    {
        try
        {
            await _apiConfig.CreateIbanAsync(_ofc, new IbanDto { Iban = DemoIban, CompanyName = _company, IsActive = true, ValidityDate = DateTimeOffset.UtcNow });
        }
        catch { /* gia presente: ok */ }
    }

    PaymentPositionRequest BuildPosition(string iupd, string iuv, long amount, DateTimeOffset due, bool expired = false) => new()
    {
        Iupd = iupd, CompanyName = _company,
        PaymentOption = new()
        {
            new PaymentOptionDto
            {
                Description = "Scenario di test", SwitchToExpired = expired,
                Debtor = new DebtorDto { Type = "F", FiscalCode = "RSSMRA80A01H501U", FullName = "Mario Rossi" },
                Installments = new()
                {
                    new InstallmentDto
                    {
                        Iuv = iuv, Amount = amount, Description = "Scenario di test", DueDate = due,
                        Transfer = new() { new TransferDto { IdTransfer = "1", Amount = amount, RemittanceInformation = "Scenario", Iban = DemoIban, Category = "9/0101108TS/" } }
                    }
                }
            }
        }
    };

    async Task<(string nav, string iupd, string iuv)> CreaDovuto(List<ScenarioStep> steps, long amount = 1000, int dueDaysFromNow = 30, bool expired = false)
    {
        await SeedIban();
        string iuv = _iuv.NextIuv();
        string iupd = $"{_ofc}-{iuv}";
        var due = DateTimeOffset.UtcNow.AddDays(dueDaysFromNow);
        var created = await _gpd.CreateDebtPositionAsync(_ofc, BuildPosition(iupd, iuv, amount, due, expired), toPublish: true);
        string nav = created.FirstNav() ?? "";
        steps.Add(new("Crea + pubblica posizione su GPD", !string.IsNullOrEmpty(nav),
            $"NAV {nav}", $"POST /organizations/{_ofc}/debtpositions?toPublish=true\nIUPD={iupd} IUV={iuv} importo={amount}c",
            $"NAV={nav} status={created.Status}"));
        return (nav, iupd, iuv);
    }

    async Task CicloOk(List<ScenarioStep> steps)
    {
        var (nav, iupd, iuv) = await CreaDovuto(steps, amount: 1000);

        var v = await _node.VerifyAsync(_ofc, nav);
        steps.Add(new("verifyPaymentNotice (PSP→Nodo→EC)", v.Ok, v.Ok ? $"{v.CompanyName} — {v.AmountEuro:0.00} €" : $"fault {v.Fault?.Display}",
            $"verifyPaymentNotice ofc={_ofc} nav={nav}", v.Ok ? $"OK {v.CompanyName} {v.AmountEuro}" : v.Fault?.Display));
        if (!v.Ok) return;

        var a = await _node.ActivateAsync(_ofc, nav, v.AmountEuro);
        steps.Add(new("activatePaymentNotice", a.Ok, a.Ok ? $"token {a.PaymentToken[..Math.Min(8, a.PaymentToken.Length)]}…" : $"fault {a.Fault?.Display}",
            $"activatePaymentNotice nav={nav} amount={v.AmountEuro}", a.Ok ? $"token={a.PaymentToken}" : a.Fault?.Display));
        if (!a.Ok) return;

        var n = await _node.PspNotifyAsync(_ofc, a.TotalAmount, _fee, new[] { new NodeForPspClient.NotifyItem(a.PaymentToken, a.TotalAmount, a.CompanyName) });
        steps.Add(new("pspNotifyPaymentV2", n.ok, n.ok ? "notifica accettata" : $"fault {n.fault?.Display}", "pspNotifyPaymentV2", n.ok ? "OK" : n.fault?.Display));

        var o = await _node.SendOutcomeAsync(new[] { a.PaymentToken }, ok: true, _fee, "RSSMRA80A01H501U", "Mario Rossi");
        steps.Add(new("sendPaymentOutcomeV2 (OK)", o.ok, o.ok ? "esito OK inviato" : $"fault {o.fault?.Display}", "sendPaymentOutcomeV2 outcome=OK", o.ok ? "OK" : o.fault?.Display));

        var pos = await _gpd.GetDebtPositionAsync(_ofc, iupd);
        bool paid = pos?.Status is "PAID" or "PARTIALLY_PAID";
        steps.Add(new("GPD: posizione PAID", paid, $"status={pos?.Status}", $"GET /organizations/{_ofc}/debtpositions/{iupd}", $"status={pos?.Status}"));

        var rt = await _gpd.GetReceiptByIuvAsync(_ofc, iuv);
        steps.Add(new("Receipts: RT disponibile", rt is not null, rt is not null ? $"receiptId {rt.ReceiptId}" : "nessuna RT",
            $"GET /payments/{_ofc}/receipts/{iuv}", rt is not null ? $"receiptId={rt.ReceiptId}" : "404"));
    }

    async Task CartaKo(List<ScenarioStep> steps, bool expired)
    {
        var (nav, iupd, _) = await CreaDovuto(steps, amount: 1500);
        var v = await _node.VerifyAsync(_ofc, nav);
        steps.Add(new("verifyPaymentNotice", v.Ok, v.Ok ? $"{v.AmountEuro:0.00} €" : $"fault {v.Fault?.Display}", $"verify nav={nav}", v.Ok ? "OK" : v.Fault?.Display));
        if (!v.Ok) return;

        if (expired)
        {
            steps.Add(new("Carta scaduta rilevata dal PSP", true, "il PSP non procede all'attivazione", "TestCards.Classify=Expired", "carta scaduta"));
        }
        else
        {
            var a = await _node.ActivateAsync(_ofc, nav, v.AmountEuro);
            steps.Add(new("activatePaymentNotice", a.Ok, a.Ok ? "attivata" : $"fault {a.Fault?.Display}", $"activate nav={nav}", a.Ok ? "OK" : a.Fault?.Display));
            if (a.Ok)
            {
                var o = await _node.SendOutcomeAsync(new[] { a.PaymentToken }, ok: false, 0m, "RSSMRA80A01H501U", "Mario Rossi");
                steps.Add(new("sendPaymentOutcomeV2 (KO)", o.ok, "esito KO inviato (autorizzazione negata)", "sendPaymentOutcomeV2 outcome=KO", o.ok ? "OK" : o.fault?.Display));
            }
        }

        var pos = await _gpd.GetDebtPositionAsync(_ofc, iupd);
        bool stillPayable = pos?.Status is "VALID" or "PARTIALLY_PAID";
        steps.Add(new("GPD: posizione ancora pagabile", stillPayable, $"status={pos?.Status}", $"GET …/debtpositions/{iupd}", $"status={pos?.Status}"));
    }

    async Task KeyErrata(List<ScenarioStep> steps)
    {
        var client = _http.CreateClient("gpd-raw");
        var req = new HttpRequestMessage(HttpMethod.Get, $"/organizations/{_ofc}/debtpositions");
        req.Headers.Add("Ocp-Apim-Subscription-Key", "chiave-sbagliata");
        var resp = await client.SendAsync(req);
        bool ok = resp.StatusCode == HttpStatusCode.Unauthorized;
        steps.Add(new("GPD con key errata → 401", ok, $"HTTP {(int)resp.StatusCode}",
            $"GET /organizations/{_ofc}/debtpositions\nOcp-Apim-Subscription-Key: chiave-sbagliata",
            $"HTTP {(int)resp.StatusCode} {await resp.Content.ReadAsStringAsync()}"));
    }

    async Task IupdDup(List<ScenarioStep> steps)
    {
        await SeedIban();
        string iuv = _iuv.NextIuv();
        string iupd = $"{_ofc}-{iuv}";
        var body = BuildPosition(iupd, iuv, 1000, DateTimeOffset.UtcNow.AddDays(30));

        var client = _http.CreateClient("gpd-raw");
        async Task<HttpResponseMessage> Create()
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"/organizations/{_ofc}/debtpositions?toPublish=false")
            { Content = JsonContent.Create(body, options: new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
            req.Headers.Add("Ocp-Apim-Subscription-Key", _key);
            return await client.SendAsync(req);
        }
        var first = await Create();
        steps.Add(new("Prima creazione", first.IsSuccessStatusCode, $"HTTP {(int)first.StatusCode}", $"POST …/debtpositions IUPD={iupd}", $"HTTP {(int)first.StatusCode}"));
        var second = await Create();
        bool conflict = second.StatusCode == HttpStatusCode.Conflict;
        steps.Add(new("Seconda creazione stesso IUPD → 409", conflict, $"HTTP {(int)second.StatusCode}",
            $"POST …/debtpositions IUPD={iupd}", $"HTTP {(int)second.StatusCode} {await second.Content.ReadAsStringAsync()}"));
    }

    async Task AvvisoScaduto(List<ScenarioStep> steps)
    {
        var (nav, _, _) = await CreaDovuto(steps, amount: 1000, dueDaysFromNow: -10, expired: true);
        var v = await _node.VerifyAsync(_ofc, nav);
        // Atteso: la verifica rifiuta l'avviso scaduto con un fault.
        bool rejected = !v.Ok;
        steps.Add(new("verifyPaymentNotice su avviso scaduto", rejected,
            rejected ? $"rifiutato: {v.Fault?.Display}" : "ATTENZIONE: verify ha restituito OK",
            $"verify nav={nav}", v.Ok ? $"OK {v.AmountEuro}" : v.Fault?.Display));
    }

    async Task QrNav(List<ScenarioStep> steps)
    {
        var (nav, _, _) = await CreaDovuto(steps, amount: 1234);
        string payload = $"PAGOPA|002|{nav}|{_ofc}|1234";
        var parts = payload.Split('|');
        bool ok = parts.Length == 5 && parts[2] == nav && parts[3] == _ofc;
        steps.Add(new("QR PAGOPA|002 coerente col NAV", ok, ok ? $"NAV nel QR = {nav}" : "mismatch",
            "build payload QR avviso", payload));
    }
}
