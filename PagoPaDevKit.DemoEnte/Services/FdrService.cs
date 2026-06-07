using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using PagoPaDevKit.Client;

namespace PagoPaDevKit.DemoEnte;

// Flussi di Rendicontazione (FdR) lato gestionale Ente. NON modifica il mock: usa le API FdR
// già esposte da GPD (pagopa-fdr) — lato Organization per il RECUPERO dei flussi pubblicati,
// e lato PSP per SIMULARE la pubblicazione di un flusso (il gestionale fa da PSP per popolare i dati).
public record FdrSummary(string Fdr, string PspId, int Revision, DateTimeOffset FlowDate);
public record FdrPaymentRow(long Index, string Iuv, string Iur, decimal Pay, string PayStatus, DateTimeOffset PayDate);
public class FdrDetail
{
    public string Fdr { get; set; } = "";
    public string Status { get; set; } = "";
    public string PspId { get; set; } = "";
    public string? PspName { get; set; }
    public string? OrganizationName { get; set; }
    public DateTimeOffset FdrDate { get; set; }
    public string? Regulation { get; set; }
    public DateTimeOffset? RegulationDate { get; set; }
    public string? Bic { get; set; }
    public int Revision { get; set; }
    public long TotPayments { get; set; }
    public decimal SumPayments { get; set; }
}

public class FdrService
{
    readonly IHttpClientFactory _http;
    readonly string _ofc, _key, _pspId, _pspName;
    static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public FdrService(IHttpClientFactory http, IConfiguration cfg)
    {
        _http = http;
        _ofc = cfg["Ente:FiscalCode"] ?? "77777777777";
        _key = cfg["PagoPaGpd:SubscriptionKey"] ?? "dev-gpd-key";
        _pspId = cfg["Ente:IdPsp"] ?? "BANCADEMO";
        _pspName = cfg["Ente:PspName"] ?? "Banca Demo S.p.A.";
    }

    HttpRequestMessage Req(HttpMethod m, string url, object? body = null)
    {
        var r = new HttpRequestMessage(m, url);
        r.Headers.Add("Ocp-Apim-Subscription-Key", _key);
        if (body is not null) r.Content = JsonContent.Create(body, options: Web);
        return r;
    }
    HttpClient Client() => _http.CreateClient("gpd-raw");

    // ---------- Organization: recupero flussi pubblicati ----------
    public async Task<List<FdrSummary>> ListAsync()
    {
        var resp = await Client().SendAsync(Req(HttpMethod.Get, $"/organizations/{_ofc}/fdrs?size=1000"));
        if (!resp.IsSuccessStatusCode) return new();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var list = new List<FdrSummary>();
        if (doc.RootElement.TryGetProperty("data", out var data))
            foreach (var f in data.EnumerateArray())
                list.Add(new FdrSummary(
                    Str(f, "fdr"), Str(f, "pspId"),
                    f.TryGetProperty("revision", out var rv) ? rv.GetInt32() : 1,
                    Date(f, "flowDate")));
        return list.OrderByDescending(x => x.FlowDate).ToList();
    }

    public async Task<FdrDetail?> GetAsync(string fdr, int rev, string pspId)
    {
        var resp = await Client().SendAsync(Req(HttpMethod.Get, $"/organizations/{_ofc}/fdrs/{Esc(fdr)}/revisions/{rev}/psps/{Esc(pspId)}"));
        if (!resp.IsSuccessStatusCode) return null;
        var r = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(r);
        var e = doc.RootElement;
        var sender = e.TryGetProperty("sender", out var s) ? s : default;
        var receiver = e.TryGetProperty("receiver", out var rc) ? rc : default;
        return new FdrDetail
        {
            Fdr = Str(e, "fdr"), Status = Str(e, "status"), Revision = e.TryGetProperty("revision", out var rv) ? rv.GetInt32() : 1,
            FdrDate = Date(e, "fdrDate"), Regulation = StrN(e, "regulation"),
            RegulationDate = e.TryGetProperty("regulationDate", out var rd) && rd.ValueKind != JsonValueKind.Null && rd.TryGetDateTimeOffset(out var rdv) ? rdv : null,
            Bic = StrN(e, "bicCodePouringBank"),
            PspId = sender.ValueKind == JsonValueKind.Object ? Str(sender, "pspId") : "",
            PspName = sender.ValueKind == JsonValueKind.Object ? StrN(sender, "pspName") : null,
            OrganizationName = receiver.ValueKind == JsonValueKind.Object ? StrN(receiver, "organizationName") : null,
            TotPayments = e.TryGetProperty("computedTotPayments", out var tp) ? tp.GetInt64() : 0,
            SumPayments = e.TryGetProperty("computedSumPayments", out var sp) ? sp.GetDecimal() : 0
        };
    }

    public async Task<List<FdrPaymentRow>> PaymentsAsync(string fdr, int rev, string pspId)
    {
        var resp = await Client().SendAsync(Req(HttpMethod.Get, $"/organizations/{_ofc}/fdrs/{Esc(fdr)}/revisions/{rev}/psps/{Esc(pspId)}/payments?size=1000"));
        if (!resp.IsSuccessStatusCode) return new();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var list = new List<FdrPaymentRow>();
        if (doc.RootElement.TryGetProperty("data", out var data))
            foreach (var p in data.EnumerateArray())
                list.Add(new FdrPaymentRow(
                    p.TryGetProperty("index", out var ix) ? ix.GetInt64() : 0,
                    Str(p, "iuv"), Str(p, "iur"),
                    p.TryGetProperty("pay", out var pay) ? pay.GetDecimal() : 0,
                    Str(p, "payStatus"), Date(p, "payDate")));
        return list.OrderBy(x => x.Index).ToList();
    }

    // FlussoRiversamento 1.0.4 firmato (XML-DSig), recupero lato Organization.
    public async Task<string?> FlussoRiversamentoXmlAsync(string fdr, int rev, string pspId)
    {
        var resp = await Client().SendAsync(Req(HttpMethod.Get, $"/organizations/{_ofc}/fdrs/{Esc(fdr)}/revisions/{rev}/psps/{Esc(pspId)}/flussoriversamento"));
        return resp.IsSuccessStatusCode ? await resp.Content.ReadAsStringAsync() : null;
    }

    // ---------- PSP: simula la pubblicazione dei flussi dalle ricevute dell'ente ----------
    // Un FdR per giornata di pagamento (come fa il PSP nel reale). Idempotente PER PAGAMENTO:
    // rendiconta solo le ricevute il cui IUV non è già presente in un FdR pubblicato.
    public async Task<int> SimulaPubblicazioneAsync(List<ReceiptSummary> receipts)
    {
        if (receipts.Count == 0) return 0;
        var existing = await ListAsync();
        var existingNames = existing.Select(x => x.Fdr).ToHashSet();

        // IUV già rendicontati (presenti in qualunque FdR pubblicato): non li ripubblichiamo.
        var rendicontati = new HashSet<string>();
        foreach (var f in existing)
            foreach (var p in await PaymentsAsync(f.Fdr, f.Revision, f.PspId))
                rendicontati.Add(p.Iuv);

        int published = 0;
        foreach (var g in receipts.GroupBy(r => r.PaymentDateTime.Date))
        {
            var pays = g.Where(r => !rendicontati.Contains(r.Iuv)).OrderBy(r => r.Iuv).ToList();
            if (pays.Count == 0) continue;

            // Nome flusso univoco per giornata (suffisso progressivo se già esiste).
            string fdrName = $"{g.Key:yyyyMMdd}-{_pspId}";
            int seq = 1;
            while (existingNames.Contains(fdrName)) fdrName = $"{g.Key:yyyyMMdd}-{_pspId}-{++seq}";
            existingNames.Add(fdrName);
            decimal sum = pays.Sum(r => r.PaymentAmount) / 100m;

            // 1) createEmptyFlow
            var create = await Client().SendAsync(Req(HttpMethod.Post, $"/psps/{_pspId}/fdrs/{fdrName}", new
            {
                fdr = fdrName,
                fdrDate = DateTimeOffset.UtcNow,
                sender = new { type = "LEGAL_PERSON", id = _pspId, pspId = _pspId, pspName = _pspName, pspBrokerId = _pspId, channelId = $"{_pspId}_01" },
                receiver = new { id = _ofc, organizationId = _ofc, organizationName = "Comune Demo" },
                regulation = $"REG-{g.Key:yyyyMMdd}", regulationDate = g.Key.AddDays(1), bicCodePouringBank = "BCITITMM",
                totPayments = pays.Count, sumPayments = sum
            }));
            if (!create.IsSuccessStatusCode) continue;

            // 2) addPayment
            long idx = 1;
            var payload = new
            {
                payments = pays.Select(r => new
                {
                    index = idx++, iuv = r.Iuv, iur = $"IUR{g.Key:yyyyMMdd}{r.Iuv[^6..]}",
                    idTransfer = 1, pay = r.PaymentAmount / 100m, payStatus = "EXECUTED", payDate = r.PaymentDateTime
                }).ToList()
            };
            await Client().SendAsync(Req(HttpMethod.Put, $"/psps/{_pspId}/fdrs/{fdrName}/payments/add", payload));

            // 3) publishFlow
            var pub = await Client().SendAsync(Req(HttpMethod.Post, $"/psps/{_pspId}/fdrs/{fdrName}/publish"));
            if (pub.IsSuccessStatusCode) published++;
        }
        return published;
    }

    static string Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    static string? StrN(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    static DateTimeOffset Date(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.TryGetDateTimeOffset(out var d) ? d : default;
    static string Esc(string s) => Uri.EscapeDataString(s);
}
