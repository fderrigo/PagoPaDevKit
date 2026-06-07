using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PagoPaDevKit.Tests;

// Pagamento SPONTANEO (avviso on-demand) vs pagamento DA AVVISO.
[Collection("pagopa")]
public class SpontaneousPaymentTests
{
    const string Ofc = "77777777777";
    const string Nns = "http://pagopa-api.pagopa.gov.it/node/nodeForPsp.xsd";
    readonly PagoPaFixture _fx;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public SpontaneousPaymentTests(PagoPaFixture fx) => _fx = fx;

    static string V(System.Xml.Linq.XDocument d, string l) =>
        d.Descendants().FirstOrDefault(e => string.Equals(e.Name.LocalName, l, StringComparison.OrdinalIgnoreCase))?.Value ?? "";

    [Fact]
    public async Task DemandPaymentNotice_GeneratesSpontaneousNotice_ThenPayable()
    {
        // Il Nodo chiede all'EC di generare l'avviso on-demand (servizio = tassonomia).
        string env = $@"<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:n=""{Nns}""><soap:Body>
<n:DemandPaymentNotice><n:request>
<n:IdPSP>BANCADEMO</n:IdPSP><n:IdBrokerPSP>BANCADEMO</n:IdBrokerPSP><n:IdChannel>BANCADEMO_01</n:IdChannel>
<n:IdPA>{Ofc}</n:IdPA><n:IdServizio>9/0101002IM/</n:IdServizio><n:IdSoggettoServizio>RSSMRA80A01H501U</n:IdSoggettoServizio>
<n:DatiSpecificiServizioRequest><n:Amount>42.00</n:Amount><n:Causale>Spontaneo TARI</n:Causale><n:DebtorFullName>Mario Rossi</n:DebtorFullName></n:DatiSpecificiServizioRequest>
</n:request></n:DemandPaymentNotice></soap:Body></soap:Envelope>";
        var c = new StringContent(env, Encoding.UTF8, "text/xml"); c.Headers.Add("SOAPAction", "\"demandPaymentNotice\"");
        var resp = await _fx.NodoClient().PostAsync("/nodo/node/nodeForPsp.svc", c);
        var d = System.Xml.Linq.XDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("OK", V(d, "Outcome"));
        string nav = V(d, "NoticeNumber");
        Assert.False(string.IsNullOrEmpty(nav));

        // La posizione esiste su GPD con Origin = SPONTANEO.
        var list = await _fx.GpdClient().GetFromJsonAsync<JsonElement>($"/organizations/{Ofc}/debtpositions?limit=100");
        var pos = list.GetProperty("payment_position_list").EnumerateArray()
            .First(p => p.GetProperty("paymentOption")[0].GetProperty("installments")[0].GetProperty("nav").GetString() == nav);
        Assert.Equal("SPONTANEO", pos.GetProperty("origin").GetString());
        Assert.Equal("VALID", pos.GetProperty("status").GetString());

        // Pagabile come un avviso normale: paga via Checkout.
        var pay = await _fx.CheckoutClient().PostAsync("/pagamento",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["nav"] = nav, ["fiscalCode"] = Ofc, ["pan"] = "4242424242424242", ["exp"] = "12/30", ["cvv"] = "123" }));
        Assert.Contains("riuscito", await pay.Content.ReadAsStringAsync());
        var dp = await _fx.GpdClient().GetFromJsonAsync<JsonElement>($"/organizations/{Ofc}/debtpositions/{pos.GetProperty("iupd").GetString()}");
        Assert.Equal("PAID", dp.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Checkout_SpontaneousForm_RedirectsToPayment()
    {
        var form = new Dictionary<string, string>
        {
            ["fiscalCode"] = Ofc, ["idServizio"] = "9/0101002IM/", ["debtorFiscalCode"] = "RSSMRA80A01H501U",
            ["debtorName"] = "Mario Rossi", ["causale"] = "Spontaneo mensa", ["amountEuro"] = "15.00"
        };
        var client = _fx.Checkout.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var resp = await client.PostAsync("/spontaneo", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/pagamento?nav=", resp.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Demand_ZeroAmount_KO_PaaSemantica()
    {
        string env = $@"<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:n=""{Nns}""><soap:Body>
<n:DemandPaymentNotice><n:request>
<n:IdPSP>BANCADEMO</n:IdPSP><n:IdBrokerPSP>BANCADEMO</n:IdBrokerPSP><n:IdChannel>BANCADEMO_01</n:IdChannel>
<n:IdPA>{Ofc}</n:IdPA><n:IdServizio>9/0101002IM/</n:IdServizio><n:IdSoggettoServizio>RSSMRA80A01H501U</n:IdSoggettoServizio>
<n:DatiSpecificiServizioRequest><n:Amount>0.00</n:Amount><n:Causale>x</n:Causale></n:DatiSpecificiServizioRequest>
</n:request></n:DemandPaymentNotice></soap:Body></soap:Envelope>";
        var c = new StringContent(env, Encoding.UTF8, "text/xml"); c.Headers.Add("SOAPAction", "\"demandPaymentNotice\"");
        var resp = await _fx.NodoClient().PostAsync("/nodo/node/nodeForPsp.svc", c);
        var d = System.Xml.Linq.XDocument.Parse(await resp.Content.ReadAsStringAsync());
        Assert.Equal("KO", V(d, "Outcome"));
        // Il Nodo incapsula l'errore EC: faultCode PPT_, original* col fault PAA dell'ente.
        Assert.Equal("PPT_ERRORE_EMESSO_DA_PAA", V(d, "faultCode"));
        Assert.Equal("PAA_SEMANTICA", V(d, "originalFaultCode"));
    }
}
