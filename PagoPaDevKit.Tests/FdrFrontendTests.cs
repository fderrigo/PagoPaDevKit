using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PagoPaDevKit.DemoEnte;
using Xunit;

namespace PagoPaDevKit.Tests;

// Flusso di rendicontazione (FdR) lato gestionale: crea+paga -> ricevuta -> il gestionale
// (da PSP) pubblica il FdR via le API reali GPD -> l'ente lo recupera, vede i pagamenti,
// scarica il FlussoRiversamento firmato, e le rate citate passano a REPORTED.
[Collection("pagopa")]
public class FdrFrontendTests
{
    const string Ofc = "77777777777";
    const string Iban = "IT60X0542811101000000123456";
    readonly PagoPaFixture _fx;
    public FdrFrontendTests(PagoPaFixture fx) => _fx = fx;

    static FormUrlEncodedContent PayForm(string nav) =>
        new(new Dictionary<string, string> { ["nav"] = nav, ["fiscalCode"] = Ofc, ["pan"] = "4242424242424242", ["exp"] = "12/30", ["cvv"] = "123" });

    (string nav, string iupd, string iuv) ReadDovuto(string desc)
    {
        using var scope = _fx.Ente.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EnteDbContext>();
        var d = db.Dovuti.Single(x => x.Description == desc);
        return (d.Nav, d.Iupd, d.Iuv);
    }

    async Task SeedIban()
    {
        var json = $@"{{""iban"":""{Iban}"",""company_name"":""Comune Demo"",""is_active"":true}}";
        await _fx.ApiConfigClient().PostAsync($"/creditorinstitutions/{Ofc}/ibans", new StringContent(json, Encoding.UTF8, "application/json"));
    }

    [Fact]
    public async Task Fdr_PublishedByPsp_RetrievedByEnte_RatesReported()
    {
        await SeedIban();
        string desc = "FDR rendicontazione E2E";

        // 1) crea + pubblica
        var form = new Dictionary<string, string>
        {
            ["debtorFiscalCode"] = "RSSMRA80A01H501U", ["debtorName"] = "Mario Rossi",
            ["description"] = desc, ["amountEuro"] = "33.00", ["dueDate"] = "2030-12-31",
            ["category"] = "9/0101108TS/", ["iban"] = Iban
        };
        var create = await _fx.EnteClient().PostAsync("/dovuti", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var (nav, iupd, iuv) = ReadDovuto(desc);

        // 2) paga (carta OK) -> RT
        var pay = await _fx.CheckoutClient().PostAsync("/pagamento", PayForm(nav));
        Assert.Contains("riuscito", await pay.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await _fx.GpdClient().GetAsync($"/payments/{Ofc}/receipts/{iuv}")).StatusCode);

        // 3) il gestionale (da PSP) pubblica il FdR dalle ricevute
        var sim = await _fx.EnteClient().PostAsync("/riconciliazione/simula", null);
        Assert.Equal(HttpStatusCode.Redirect, sim.StatusCode);

        // 4) GPD ha un FdR pubblicato per l'ente
        var fdrs = await _fx.GpdClient().GetFromJsonAsync<JsonElement>($"/organizations/{Ofc}/fdrs?size=50");
        Assert.True(fdrs.GetProperty("count").GetInt64() >= 1);
        var item = fdrs.GetProperty("data").EnumerateArray().First();
        string fdr = item.GetProperty("fdr").GetString()!;
        string psp = item.GetProperty("pspId").GetString()!;
        int rev = item.GetProperty("revision").GetInt32();

        // 5) la pagina riconciliazione mostra il FdR reale
        var page = await _fx.EnteClient().GetAsync("/riconciliazione");
        page.EnsureSuccessStatusCode();
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains(fdr, html);
        Assert.Contains("dati reali", html);

        // 6) dettaglio FdR (pagamenti) + FlussoRiversamento firmato
        var det = await _fx.EnteClient().GetAsync($"/riconciliazione/fdr/{psp}/{fdr}/{rev}");
        Assert.Equal(HttpStatusCode.OK, det.StatusCode);
        Assert.Contains(iuv, await det.Content.ReadAsStringAsync());

        var xml = await _fx.EnteClient().GetAsync($"/riconciliazione/fdr/{psp}/{fdr}/{rev}/xml");
        Assert.Equal(HttpStatusCode.OK, xml.StatusCode);
        var xmlBody = await xml.Content.ReadAsStringAsync();
        Assert.Contains("FlussoRiversamento", xmlBody);
        Assert.Contains("Signature", xmlBody); // firma XML-DSig

        // 7) la rata citata nel flusso passa a REPORTED
        var dp = await _fx.GpdClient().GetFromJsonAsync<JsonElement>($"/organizations/{Ofc}/debtpositions/{iupd}");
        var instStatus = dp.GetProperty("paymentOption")[0].GetProperty("installments")[0].GetProperty("status").GetString();
        Assert.Equal("REPORTED", instStatus);
    }

    [Fact]
    public async Task Fdr_NoReceipts_NothingPublished()
    {
        // Senza ricevute non si pubblica nulla; la pagina resta valida (vuota lato FdR).
        var sim = await _fx.EnteClient().PostAsync("/riconciliazione/simula", null);
        Assert.Equal(HttpStatusCode.Redirect, sim.StatusCode);
        var page = await _fx.EnteClient().GetAsync("/riconciliazione");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("riconciliazione contabile", (await page.Content.ReadAsStringAsync()).ToLowerInvariant());
    }
}
