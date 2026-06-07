using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace PagoPaDevKit.Mock.Gpd;

// Consegna della RT alla "stazione broadcast" dell'EC (paSendRT push), opzione consigliata del
// modello asincrono. In caso di fallimento la RT resta NOTICE_PENDING e viene ritentata.
// NOTA: pagoPA reale ritenta fino a 48 volte ogni 1h; qui intervallo/limite sono configurabili.
public class ReceiptDeliverySender
{
    readonly IHttpClientFactory _http;
    readonly string? _stationUrl;
    public int MaxAttempts { get; }

    public ReceiptDeliverySender(IHttpClientFactory http, IConfiguration cfg)
    {
        _http = http;
        _stationUrl = cfg["Gpd:StationUrl"];
        MaxAttempts = cfg.GetValue("Gpd:StationMaxAttempts", 48);
    }

    public bool StationConfigured => !string.IsNullOrWhiteSpace(_stationUrl);

    public async Task<bool> TryDeliverAsync(Receipt r)
    {
        if (!StationConfigured) return false;
        string A(long cents) => (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        string Esc(string? s) => System.Security.SecurityElement.Escape(s ?? "");
        string env = $@"<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:t=""{PaForNodeNs.Xsd}""><soap:Body>
<t:PaSendRTV2><t:request><t:Receipt>
<t:ReceiptId>{Esc(r.ReceiptId)}</t:ReceiptId><t:NoticeNumber>{Esc(r.Nav)}</t:NoticeNumber>
<t:CreditorReferenceId>{Esc(r.Iuv)}</t:CreditorReferenceId><t:PaymentAmount>{A(r.PaymentAmount)}</t:PaymentAmount>
<t:Fee>{A(r.Fee)}</t:Fee><t:PSPCompanyName>{Esc(r.PspCompanyName)}</t:PSPCompanyName><t:Outcome>{Esc(r.Outcome)}</t:Outcome>
</t:Receipt></t:request></t:PaSendRTV2></soap:Body></soap:Envelope>";
        try
        {
            var content = new StringContent(env, Encoding.UTF8, "text/xml");
            content.Headers.Add("SOAPAction", "\"paSendRTV2\"");
            var resp = await _http.CreateClient("station").PostAsync(_stationUrl, content);
            if (!resp.IsSuccessStatusCode) return false;
            var body = await resp.Content.ReadAsStringAsync();
            return body.Contains("<Outcome>OK</Outcome>");
        }
        catch { return false; }
    }
}

// Worker che ritenta le RT NOTICE_PENDING fino a MaxAttempts.
public class ReceiptRetryWorker : BackgroundService
{
    readonly ReceiptDeliverySender _sender;
    readonly IServiceScopeFactory _scopes;
    readonly int _intervalSec;

    public ReceiptRetryWorker(ReceiptDeliverySender sender, IServiceScopeFactory scopes, IConfiguration cfg)
    { _sender = sender; _scopes = scopes; _intervalSec = cfg.GetValue("Gpd:StationRetrySeconds", 30); }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_sender.StationConfigured) return;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<GpdDbContext>();
                var pending = await db.Receipts
                    .Where(x => x.DeliveryStatus == ReceiptDeliveryStatus.NoticePending && x.DeliveryAttempts < _sender.MaxAttempts)
                    .ToListAsync(ct);
                foreach (var r in pending)
                {
                    r.DeliveryAttempts++;
                    if (await _sender.TryDeliverAsync(r)) r.DeliveryStatus = ReceiptDeliveryStatus.Delivered;
                }
                if (pending.Count > 0) await db.SaveChangesAsync(ct);
            }
            catch { /* loop */ }
            await Task.Delay(TimeSpan.FromSeconds(_intervalSec), ct);
        }
    }
}
