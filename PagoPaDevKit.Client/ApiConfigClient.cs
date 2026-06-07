using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace PagoPaDevKit.Client;

public class ApiConfigOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5005";
    public string SubscriptionKey { get; set; } = "dev-gpd-key";
}

// DTO IBAN (snake_case, come apiconfig).
public class IbanDto
{
    public string Iban { get; set; } = "";
    public string? CiOwner { get; set; }
    public string? CompanyName { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset ValidityDate { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public List<IbanLabelDto> Labels { get; set; } = new();
}
public class IbanLabelDto { public string Name { get; set; } = ""; public string? Description { get; set; } }

class IbansEnhancedDto { public List<IbanDto> IbansEnhanced { get; set; } = new(); }

// DTO Tassonomia (tipo dovuto).
public class TaxonomyDto
{
    public string CodiceTassonomico { get; set; } = "";
    public string TipoEnte { get; set; } = "";
    public string MacroArea { get; set; } = "";
    public string DenominazioneServizio { get; set; } = "";
    public string DescrizioneMotivoRiscossione { get; set; } = "";
    public string MotivoRiscossione { get; set; } = "";
    public int Versione { get; set; }
}

// Typed client verso ApiConfig (registro IBAN + tassonomia). snake_case JSON.
public class ApiConfigClient
{
    readonly HttpClient _http;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public ApiConfigClient(HttpClient http, IOptions<ApiConfigOptions> opt)
    {
        _http = http;
        _http.BaseAddress ??= new Uri(opt.Value.BaseUrl);
        if (!_http.DefaultRequestHeaders.Contains("Ocp-Apim-Subscription-Key"))
            _http.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", opt.Value.SubscriptionKey);
    }

    public async Task<List<IbanDto>> GetIbansAsync(string ci, CancellationToken ct = default)
    {
        var wrap = await _http.GetFromJsonAsync<IbansEnhancedDto>($"/creditorinstitutions/{ci}/ibans", Json, ct);
        return wrap?.IbansEnhanced ?? new();
    }

    public async Task CreateIbanAsync(string ci, IbanDto iban, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync($"/creditorinstitutions/{ci}/ibans", iban, Json, ct);
        await EnsureOk(resp, ct);
    }

    public async Task DeleteIbanAsync(string ci, string ibanValue, CancellationToken ct = default)
    {
        var resp = await _http.DeleteAsync($"/creditorinstitutions/{ci}/ibans/{ibanValue}", ct);
        await EnsureOk(resp, ct);
    }

    public async Task<List<TaxonomyDto>> GetTaxonomiesAsync(bool onlyValid = true, CancellationToken ct = default)
    {
        var list = await _http.GetFromJsonAsync<List<TaxonomyDto>>($"/taxonomies?onlyValid={onlyValid.ToString().ToLowerInvariant()}", Json, ct);
        return list ?? new();
    }

    // Ricerca tassonomia (typeahead): q filtra denominazione/codice, limit limita i risultati.
    public async Task<List<TaxonomyDto>> SearchTaxonomiesAsync(string? q, int limit = 50, bool onlyValid = true, CancellationToken ct = default)
    {
        var url = $"/taxonomies?onlyValid={onlyValid.ToString().ToLowerInvariant()}&limit={limit}";
        if (!string.IsNullOrWhiteSpace(q)) url += $"&q={Uri.EscapeDataString(q)}";
        var list = await _http.GetFromJsonAsync<List<TaxonomyDto>>(url, Json, ct);
        return list ?? new();
    }

    static async Task EnsureOk(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = await resp.Content.ReadAsStringAsync(ct);
        throw new PagoPaApiException((int)resp.StatusCode, body, $"ApiConfig ha risposto {(int)resp.StatusCode}.");
    }
}

public static class ApiConfigClientServiceCollectionExtensions
{
    public static IServiceCollection AddApiConfigClient(this IServiceCollection services, Action<ApiConfigOptions> configure)
    {
        services.Configure(configure);
        services.AddHttpClient<ApiConfigClient>();
        return services;
    }
}
