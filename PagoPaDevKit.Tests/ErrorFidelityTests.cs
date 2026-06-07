using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PagoPaDevKit.Tests;

// Verifica che il backend generi gli STESSI errori di pagoPA: codici e diciture esatte.
[Collection("pagopa")]
public class ErrorFidelityTests
{
    const string Ofc = "77777777777";
    const string PaNs = "http://pagopa-api.pagopa.gov.it/pa/paForNode.xsd";
    readonly PagoPaFixture _fx;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public ErrorFidelityTests(PagoPaFixture fx) => _fx = fx;

    static string Iuv(int n) => n.ToString("D17");
    static string Nav(int n) => "3" + Iuv(n);

    static object Body(int seed, DateTimeOffset due) => new
    {
        iupd = $"ERR-{seed}", payStandIn = true, companyName = "Comune Demo",
        paymentOption = new[]
        {
            new
            {
                description = "x", switchToExpired = true,  // scade dopo la dueDate (per il caso "scaduto")
                debtor = new { type = "F", fiscalCode = "RSSMRA80A01H501U", fullName = "Mario Rossi" },
                installments = new[]
                {
                    new { iuv = Iuv(seed), amount = 1000L, description = "x", dueDate = due,
                          transfer = new[] { new { idTransfer = "1", amount = 1000L, remittanceInformation = "x",
                                                   category = "9/0101002IM/", iban = "IT60X0542811101000000123456" } } }
                }
            }
        }
    };

    async Task Create(int seed, DateTimeOffset due) =>
        (await _fx.GpdClient().PostAsJsonAsync($"/organizations/{Ofc}/debtpositions?toPublish=true", Body(seed, due), Json)).EnsureSuccessStatusCode();

    // ---- GPD ProblemJson: titoli/diciture identici a pagopa-debt-position ----
    [Fact]
    public async Task Gpd_DuplicateIupd_ExactProblemJson()
    {
        await Create(70001, DateTimeOffset.UtcNow.AddDays(30));
        var resp = await _fx.GpdClient().PostAsJsonAsync($"/organizations/{Ofc}/debtpositions", Body(70001, DateTimeOffset.UtcNow.AddDays(30)), Json);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var pj = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("The debt position violated constraints of uniqueness", pj.GetProperty("title").GetString());
        Assert.Equal(409, pj.GetProperty("status").GetInt32());
        Assert.Contains("Already exists a debt position for the Organization Fiscal Code", pj.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Gpd_NotFound_ExactProblemJson()
    {
        var resp = await _fx.GpdClient().GetAsync($"/organizations/{Ofc}/debtpositions/DOES-NOT-EXIST");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        var pj = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Not found the debt position", pj.GetProperty("title").GetString());
        Assert.Equal("Not found a debt position for Organization Fiscal Code 77777777777 and IUPD DOES-NOT-EXIST", pj.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Gpd_BadRequest_ExactTitle()
    {
        var bad = new
        {
            iupd = "ERR-70003", companyName = "Comune Demo",
            paymentOption = new[] { new { description = "x", switchToExpired = false,
                debtor = new { type = "F" }, installments = new object[0] } }
        };
        var resp = await _fx.GpdClient().PostAsJsonAsync($"/organizations/{Ofc}/debtpositions", bad, Json);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var pj = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Error in the debt position request data", pj.GetProperty("title").GetString());
    }

    // ---- SOAP PAA: faultCode/faultString/description identici a pagopa-gpd-payments ----
    async Task<string> Verify(string nav)
    {
        string env = $@"<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:t=""{PaNs}""><soap:Body>
<t:PaVerifyPaymentNotice><t:request><t:IdPA>{Ofc}</t:IdPA><t:IdBrokerPA>{Ofc}</t:IdBrokerPA><t:IdStation>S</t:IdStation>
<t:QrCode><t:FiscalCode>{Ofc}</t:FiscalCode><t:NoticeNumber>{nav}</t:NoticeNumber></t:QrCode></t:request></t:PaVerifyPaymentNotice>
</soap:Body></soap:Envelope>";
        var c = new StringContent(env, Encoding.UTF8, "text/xml"); c.Headers.Add("SOAPAction", "\"paVerifyPaymentNotice\"");
        var r = await _fx.GpdClient(withKey: false).PostAsync("/gpd/nodo/paForNode.svc", c);
        return await r.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Soap_UnknownNotice_PaaSconosciuto_ExactStrings()
    {
        var xml = await Verify(Nav(709999));
        Assert.Contains("<faultCode>PAA_PAGAMENTO_SCONOSCIUTO</faultCode>", xml);
        Assert.Contains("<faultString>pagamento sconosciuto</faultString>", xml);
        Assert.Contains("<description>L'id del pagamento ricevuto non esiste</description>", xml);
    }

    [Fact]
    public async Task Soap_ExpiredNotice_PaaScaduto_ExactStrings()
    {
        await Create(70005, DateTimeOffset.UtcNow.AddDays(-2));
        var xml = await Verify(Nav(70005));
        Assert.Contains("<faultCode>PAA_PAGAMENTO_SCADUTO</faultCode>", xml);
        Assert.Contains("<faultString>pagamento scaduto</faultString>", xml);
        Assert.Contains("<description>L'id del pagamento ricevuto e' scaduto</description>", xml);
    }

    // ---- Nodo: errore EC incapsulato (PPT_ERRORE_EMESSO_DA_PAA + original*) come da guida ----
    [Fact]
    public async Task Node_WrapsEcFault_AsPptErroreEmessoDaPaa()
    {
        const string Nns = "http://pagopa-api.pagopa.gov.it/node/nodeForPsp.xsd";
        string nav = Nav(709998);
        string env = $@"<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:n=""{Nns}""><soap:Body>
<n:VerifyPaymentNotice><n:request><n:IdPSP>BANCADEMO</n:IdPSP><n:IdBrokerPSP>BANCADEMO</n:IdBrokerPSP><n:IdChannel>BANCADEMO_01</n:IdChannel>
<n:QrCode><n:FiscalCode>{Ofc}</n:FiscalCode><n:NoticeNumber>{nav}</n:NoticeNumber></n:QrCode></n:request></n:VerifyPaymentNotice>
</soap:Body></soap:Envelope>";
        var c = new StringContent(env, Encoding.UTF8, "text/xml"); c.Headers.Add("SOAPAction", "\"verifyPaymentNotice\"");
        var r = await _fx.NodoClient().PostAsync("/nodo/node/nodeForPsp.svc", c);
        var xml = await r.Content.ReadAsStringAsync();
        Assert.Contains("<Outcome>KO</Outcome>", xml);
        Assert.Contains("<faultCode>PPT_ERRORE_EMESSO_DA_PAA</faultCode>", xml);
        Assert.Contains("<faultString>Errore restituito dall'ente creditore</faultString>", xml);
        Assert.Contains("<originalFaultCode>PAA_PAGAMENTO_SCONOSCIUTO</originalFaultCode>", xml);
        Assert.Contains("<originalFaultString>pagamento sconosciuto</originalFaultString>", xml);
    }

    // ---- ApiConfig: errori IBAN identici a pagopa-api-config ----
    [Fact]
    public async Task ApiConfig_DuplicateIban_ExactProblemJson()
    {
        string iban = "IT60X0542811101000000700099";
        var body = new StringContent($@"{{""iban"":""{iban}"",""is_active"":true}}", Encoding.UTF8, "application/json");
        await _fx.ApiConfigClient().PostAsync($"/creditorinstitutions/{Ofc}/ibans", body);
        var dup = await _fx.ApiConfigClient().PostAsync($"/creditorinstitutions/{Ofc}/ibans",
            new StringContent($@"{{""iban"":""{iban}"",""is_active"":true}}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        var pj = await dup.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("IBAN already exist", pj.GetProperty("title").GetString());
        Assert.Equal($"The IBAN with code {iban} already exists", pj.GetProperty("detail").GetString());
    }
}
