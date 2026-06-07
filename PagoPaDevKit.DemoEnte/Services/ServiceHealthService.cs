using System.Diagnostics;

namespace PagoPaDevKit.DemoEnte;

// Health check live dei servizi della solution. Pinga la root di ogni servizio e misura
// la latenza. "Healthy" = il servizio risponde (qualunque status HTTP), ovvero la porta e su.
public record ServiceHealth(string Name, string Url, bool Healthy, long? LatencyMs, string? Detail);

public class ServiceHealthService
{
    readonly IHttpClientFactory _http;
    readonly IConfiguration _cfg;
    public ServiceHealthService(IHttpClientFactory http, IConfiguration cfg) { _http = http; _cfg = cfg; }

    public IReadOnlyDictionary<string, string> Servizi() =>
        _cfg.GetSection("Servizi").GetChildren().ToDictionary(c => c.Key, c => c.Value ?? "");

    public async Task<List<ServiceHealth>> CheckAllAsync()
    {
        var tasks = Servizi().Select(kv => CheckOneAsync(kv.Key, kv.Value));
        return (await Task.WhenAll(tasks)).ToList();
    }

    public async Task<ServiceHealth> CheckOneAsync(string name, string url)
    {
        var client = _http.CreateClient("health");
        client.Timeout = TimeSpan.FromSeconds(2);
        var sw = Stopwatch.StartNew();
        try
        {
            // GET dell'URL cosi com'e (no append "/"): per il self-check si punta a /healthz,
            // evitando di rieseguire la Dashboard — che a sua volta farebbe health check (ricorsione).
            var resp = await client.GetAsync(url);
            sw.Stop();
            return new ServiceHealth(name, url, true, sw.ElapsedMilliseconds, $"HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ServiceHealth(name, url, false, null, ex.GetType().Name);
        }
    }
}
