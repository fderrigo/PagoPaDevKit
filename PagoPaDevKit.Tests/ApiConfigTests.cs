using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PagoPaDevKit.Tests;

// Test ApiConfig: registro IBAN per ente (fedele a pagopa-api-config) + tassonomia.
[Collection("pagopa")]
public class ApiConfigTests
{
    const string Ci = "88888888888"; // ente dedicato per non interferire
    readonly PagoPaFixture _fx;
    public ApiConfigTests(PagoPaFixture fx) => _fx = fx;

    StringContent Json(string s) => new(s, Encoding.UTF8, "application/json");

    [Fact]
    public async Task CreateIban_ThenListed_SnakeCase()
    {
        string iban = "IT60X0542811101000000999001";
        var body = $@"{{""iban"":""{iban}"",""company_name"":""Comune Demo"",""description"":""Tesoreria"",""is_active"":true,
            ""labels"":[{{""name"":""CONTO_TESORERIA"",""description"":""conto""}}]}}";
        var create = await _fx.ApiConfigClient().PostAsync($"/creditorinstitutions/{Ci}/ibans", Json(body));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var list = await _fx.ApiConfigClient().GetFromJsonAsync<JsonElement>($"/creditorinstitutions/{Ci}/ibans");
        var arr = list.GetProperty("ibans_enhanced");
        Assert.Contains(arr.EnumerateArray(), x => x.GetProperty("iban").GetString() == iban);
        var item = arr.EnumerateArray().First(x => x.GetProperty("iban").GetString() == iban);
        Assert.Equal(Ci, item.GetProperty("ci_owner").GetString());        // snake_case
        Assert.True(item.GetProperty("is_active").GetBoolean());
        Assert.Equal("CONTO_TESORERIA", item.GetProperty("labels")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task DuplicateIban_409()
    {
        string iban = "IT60X0542811101000000999002";
        var body = $@"{{""iban"":""{iban}"",""is_active"":true}}";
        await _fx.ApiConfigClient().PostAsync($"/creditorinstitutions/{Ci}/ibans", Json(body));
        var dup = await _fx.ApiConfigClient().PostAsync($"/creditorinstitutions/{Ci}/ibans", Json(body));
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task DeleteIban_RemovesIt()
    {
        string iban = "IT60X0542811101000000999003";
        await _fx.ApiConfigClient().PostAsync($"/creditorinstitutions/{Ci}/ibans", Json($@"{{""iban"":""{iban}"",""is_active"":true}}"));
        var del = await _fx.ApiConfigClient().DeleteAsync($"/creditorinstitutions/{Ci}/ibans/{iban}");
        Assert.Equal(HttpStatusCode.OK, del.StatusCode);
        var list = await _fx.ApiConfigClient().GetFromJsonAsync<JsonElement>($"/creditorinstitutions/{Ci}/ibans");
        Assert.DoesNotContain(list.GetProperty("ibans_enhanced").EnumerateArray(), x => x.GetProperty("iban").GetString() == iban);
    }

    [Fact]
    public async Task Taxonomy_ListsTipiDovuto_WithCode()
    {
        var tax = await _fx.ApiConfigClient().GetFromJsonAsync<JsonElement>("/taxonomies?onlyValid=true");
        Assert.True(tax.GetArrayLength() >= 1);
        var first = tax[0];
        string code = first.GetProperty("codice_tassonomico").GetString()!;
        Assert.Matches(@"^[6-9]/\d{2}\d{2}\d{3}[A-Z]{2}/$", code);   // struttura codice tassonomico
        Assert.False(string.IsNullOrEmpty(first.GetProperty("denominazione_servizio").GetString()));
    }

    [Fact]
    public async Task MissingKey_401()
    {
        var r = await _fx.ApiConfigClient(withKey: false).GetAsync($"/creditorinstitutions/{Ci}/ibans");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }
}
