using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PagoPaDevKit.DemoEnte;
using Xunit;

namespace PagoPaDevKit.Tests;

// End-to-end fedele: DemoEnte (crea+pubblica su GPD v3) -> Checkout/Nodo (SOAP paForNode)
// -> GPD (PAID + ricevuta) -> DemoEnte rilegge lo stato in PULL.
[Collection("pagopa")]
public class EndToEndTests
{
    const string Ofc = "77777777777";
    readonly PagoPaFixture _fx;

    public EndToEndTests(PagoPaFixture fx) => _fx = fx;

    static FormUrlEncodedContent PayForm(string nav, string pan) =>
        new(new Dictionary<string, string> { ["nav"] = nav, ["fiscalCode"] = Ofc, ["pan"] = pan, ["exp"] = "12/30", ["cvv"] = "123" });

    (string nav, string iupd, string iuv) ReadDovuto(string description)
    {
        using var scope = _fx.Ente.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EnteDbContext>();
        var d = db.Dovuti.Single(x => x.Description == description);
        return (d.Nav, d.Iupd, d.Iuv);
    }

    const string Iban = "IT60X0542811101000000123456";

    async Task SeedIban()
    {
        // Configura un IBAN per l'ente su ApiConfig (idempotente).
        var json = $@"{{""iban"":""{Iban}"",""company_name"":""Comune Demo"",""is_active"":true}}";
        var resp = await _fx.ApiConfigClient().PostAsync($"/creditorinstitutions/{Ofc}/ibans",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.True(resp.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict);
    }

    async Task<(string nav, string iupd, string iuv)> CreateDovuto(string desc)
    {
        await SeedIban();
        var form = new Dictionary<string, string>
        {
            ["debtorFiscalCode"] = "RSSMRA80A01H501U", ["debtorName"] = "Mario Rossi",
            ["description"] = desc, ["amountEuro"] = "12.50", ["dueDate"] = "2030-12-31",
            ["category"] = "9/0101002IM/", ["iban"] = Iban
        };
        var r = await _fx.EnteClient().PostAsync("/dovuti", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return ReadDovuto(desc);
    }

    [Fact]
    public async Task HappyPath_PayViaNode_PositionPaid_EnteSeesPagato()
    {
        var (nav, iupd, iuv) = await CreateDovuto("E2E TARI rata unica");

        // Pagina checkout: verifica SOAP OK, mostra importo/ente.
        var page = await _fx.CheckoutClient().GetAsync($"/pagamento?nav={nav}&fiscalCode={Ofc}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Comune Demo", await page.Content.ReadAsStringAsync());

        // Pagamento OK (carta test) -> paSendRTV2.
        var pay = await _fx.CheckoutClient().PostAsync("/pagamento", PayForm(nav, "4242424242424242"));
        Assert.Equal(HttpStatusCode.OK, pay.StatusCode);
        Assert.Contains("riuscito", await pay.Content.ReadAsStringAsync());

        // GPD: posizione PAID.
        var dp = await _fx.GpdClient().GetFromJsonAsync<JsonElement>($"/organizations/{Ofc}/debtpositions/{iupd}");
        Assert.Equal("PAID", dp.GetProperty("status").GetString());

        // DemoEnte rilegge in pull -> PAGATO.
        var home = await _fx.EnteClient().GetAsync("/");
        Assert.Contains("PAGATO", await home.Content.ReadAsStringAsync());

        // Ricevuta disponibile su GPD (Receipts REST).
        var receipt = await _fx.GpdClient().GetAsync($"/payments/{Ofc}/receipts/{iuv}");
        Assert.Equal(HttpStatusCode.OK, receipt.StatusCode);
    }

    [Fact]
    public async Task KoCard_PositionStaysPayable()
    {
        var (nav, iupd, _) = await CreateDovuto("E2E KO carta");
        var pay = await _fx.CheckoutClient().PostAsync("/pagamento", PayForm(nav, "4000000000000002"));
        Assert.Contains("non riuscito", await pay.Content.ReadAsStringAsync());

        var dp = await _fx.GpdClient().GetFromJsonAsync<JsonElement>($"/organizations/{Ofc}/debtpositions/{iupd}");
        Assert.Equal("VALID", dp.GetProperty("status").GetString());
    }
}
