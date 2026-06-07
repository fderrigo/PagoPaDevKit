using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace PagoPaDevKit.Client;

// Opzioni via options pattern. Cambiando solo questi valori il client punta a Mock o a UAT.
public class PagoPaGpdOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5001";          // GPD
    public string CheckoutBaseUrl { get; set; } = "http://localhost:5002";  // Checkout (ricevute)
    public string SubscriptionKey { get; set; } = "dev-gpd-key";            // header Ocp-Apim-Subscription-Key
}

// Typed HttpClient verso GPD. Stessa firma di chiamate per mock e UAT.
public class PagoPaGpdClient
{
    readonly HttpClient _http;
    readonly PagoPaGpdOptions _opt;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public PagoPaGpdClient(HttpClient http, IOptions<PagoPaGpdOptions> opt)
    {
        _opt = opt.Value;
        _http = http;
        _http.BaseAddress ??= new Uri(_opt.BaseUrl);
        if (!_http.DefaultRequestHeaders.Contains(ApiHeaders.SubKey))
            _http.DefaultRequestHeaders.Add(ApiHeaders.SubKey, _opt.SubscriptionKey);
    }

    // POST /organizations/{ofc}/debtpositions  (toPublish=true crea+pubblica in un colpo)
    public async Task<PaymentPositionResponse> CreateDebtPositionAsync(
        string organizationFiscalCode, PaymentPositionRequest req, bool toPublish = false, CancellationToken ct = default)
    {
        var url = $"/organizations/{organizationFiscalCode}/debtpositions?toPublish={toPublish.ToString().ToLowerInvariant()}";
        var resp = await _http.PostAsJsonAsync(url, req, Json, ct);
        return await Read<PaymentPositionResponse>(resp, ct);
    }

    // POST /organizations/{ofc}/debtpositions/{iupd}/publish
    public async Task<PaymentPositionResponse> PublishAsync(
        string organizationFiscalCode, string iupd, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"/organizations/{organizationFiscalCode}/debtpositions/{iupd}/publish", null, ct);
        return await Read<PaymentPositionResponse>(resp, ct);
    }

    // GET /organizations/{ofc}/debtpositions/{iupd}
    public async Task<PaymentPositionResponse?> GetDebtPositionAsync(
        string organizationFiscalCode, string iupd, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/organizations/{organizationFiscalCode}/debtpositions/{iupd}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        return await Read<PaymentPositionResponse>(resp, ct);
    }

    // DELETE /organizations/{ofc}/debtpositions/{iupd}
    public async Task DeleteAsync(string organizationFiscalCode, string iupd, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/organizations/{organizationFiscalCode}/debtpositions/{iupd}", ct);
        await EnsureOk(resp, ct);
    }

    // GET /payments/{ofc}/receipts/{iuv} (Receipts REST gpd-payments). Ritorna la RT in XML, parsata.
    public async Task<ReceiptDto?> GetReceiptByIuvAsync(string organizationFiscalCode, string iuv, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/payments/{organizationFiscalCode}/receipts/{iuv}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureOk(resp, ct);
        var xml = await resp.Content.ReadAsStringAsync(ct);
        var doc = System.Xml.Linq.XDocument.Parse(xml);
        string V(string name) => doc.Root!.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value ?? "";
        decimal D(string name) => decimal.TryParse(V(name), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0m;
        return new ReceiptDto
        {
            ReceiptId = V("receiptId"), Iuv = V("creditorReferenceId"), Nav = V("noticeNumber"),
            PaymentAmount = D("paymentAmount"), Fee = D("fee"), Outcome = V("outcome"),
            PspCompanyName = V("PSPCompanyName"),
            PaymentDateTime = DateTimeOffset.TryParse(V("paymentDateTime"), out var dt) ? dt : default
        };
    }

    // GET /payments/{ofc}/receipts : ricevute dell'ente (per la riconciliazione contabile).
    public async Task<List<ReceiptSummary>> GetReceiptsAsync(string organizationFiscalCode, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"/payments/{organizationFiscalCode}/receipts?pageSize=1000", ct);
        if (!resp.IsSuccessStatusCode) return new();
        var res = await resp.Content.ReadFromJsonAsync<PaymentsResultDto>(Json, ct);
        return res?.Results ?? new();
    }

    static async Task<T> Read<T>(HttpResponseMessage resp, CancellationToken ct)
    {
        await EnsureOk(resp, ct);
        var data = await resp.Content.ReadFromJsonAsync<T>(Json, ct);
        return data ?? throw new PagoPaApiException((int)resp.StatusCode, null, "Risposta vuota da GPD.");
    }

    static async Task EnsureOk(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = await resp.Content.ReadAsStringAsync(ct);
        throw new PagoPaApiException((int)resp.StatusCode, body,
            $"GPD ha risposto {(int)resp.StatusCode} {resp.ReasonPhrase}.");
    }
}

static class ApiHeaders
{
    public const string SubKey = "Ocp-Apim-Subscription-Key";
}

public static class PagoPaClientServiceCollectionExtensions
{
    // Registra il typed client. Le opzioni si bindano dalla sezione "PagoPaGpd" di appsettings.
    public static IServiceCollection AddPagoPaGpdClient(this IServiceCollection services, Action<PagoPaGpdOptions> configure)
    {
        services.Configure(configure);
        services.AddHttpClient<PagoPaGpdClient>();
        return services;
    }
}
