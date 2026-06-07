using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PagoPaDevKit.Tests;

// E-bollo 2.0: transfer "marca da bollo" -> al pagamento viene emessa la marca da bollo digitale (MBD).
[Collection("pagopa")]
public class EbolloTests
{
    const string Ofc = "77777777777";
    const string PaNs = "http://pagopa-api.pagopa.gov.it/pa/paForNode.xsd";
    readonly PagoPaFixture _fx;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public EbolloTests(PagoPaFixture fx) => _fx = fx;

    static string Iuv(int n) => n.ToString("D17");
    static string Nav(int n) => "3" + Iuv(n);

    async Task CreateStamp(int seed)
    {
        var body = new
        {
            iupd = $"BOLLO-{seed}", payStandIn = true, companyName = "Comune Demo",
            paymentOption = new[]
            {
                new
                {
                    description = "Imposta di bollo", switchToExpired = false,
                    debtor = new { type = "F", fiscalCode = "RSSMRA80A01H501U", fullName = "Mario Rossi" },
                    installments = new[]
                    {
                        new { iuv = Iuv(seed), amount = 1600L, description = "Marca da bollo", dueDate = DateTimeOffset.UtcNow.AddDays(30),
                              transfer = new[] { new {
                                  idTransfer = "1", amount = 1600L, remittanceInformation = "Imposta di bollo", category = "9/0101108TS/",
                                  stamp = new { hashDocument = "aGFzaERvY3VtZW50bw==", stampType = "01", provincialResidence = "RM" }
                              } } }
                    }
                }
            }
        };
        (await _fx.GpdClient().PostAsJsonAsync($"/organizations/{Ofc}/debtpositions?toPublish=true", body, Json)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task StampTransfer_PaGetPaymentV2_HasRichiestaMarcaDaBollo()
    {
        await CreateStamp(97001);
        string env = $@"<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:t=""{PaNs}""><soap:Body>
<t:PaGetPaymentV2><t:request><t:IdPA>{Ofc}</t:IdPA><t:IdBrokerPA>{Ofc}</t:IdBrokerPA><t:IdStation>S</t:IdStation>
<t:QrCode><t:FiscalCode>{Ofc}</t:FiscalCode><t:NoticeNumber>{Nav(97001)}</t:NoticeNumber></t:QrCode></t:request></t:PaGetPaymentV2>
</soap:Body></soap:Envelope>";
        var c = new StringContent(env, Encoding.UTF8, "text/xml"); c.Headers.Add("SOAPAction", "\"paGetPaymentV2\"");
        var xml = await (await _fx.GpdClient(withKey: false).PostAsync("/gpd/nodo/paForNode.svc", c)).Content.ReadAsStringAsync();
        Assert.Contains("RichiestaMarcaDaBollo", xml);
        Assert.Contains("aGFzaERvY3VtZW50bw==", xml);
    }

    [Fact]
    public async Task PayStampNotice_IssuesDigitalStamp()
    {
        int seed = 97002;
        await CreateStamp(seed);
        (await _fx.CheckoutClient().PostAsync("/pagamento",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["nav"] = Nav(seed), ["fiscalCode"] = Ofc, ["pan"] = "4242424242424242" }))).EnsureSuccessStatusCode();

        var resp = await _fx.GpdClient().GetAsync($"/e-bollo/organizations/{Ofc}/stamps/{Iuv(seed)}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var stamps = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var s = stamps[0];
        Assert.Equal("01", s.GetProperty("tipoBollo").GetString());
        Assert.Equal("RM", s.GetProperty("provinciaResidenza").GetString());
        Assert.False(string.IsNullOrEmpty(s.GetProperty("identificativoMarca").GetString()));
        Assert.False(string.IsNullOrEmpty(s.GetProperty("marcaBase64").GetString()));
    }

    [Fact]
    public async Task NoStamp_404()
    {
        var resp = await _fx.GpdClient().GetAsync($"/e-bollo/organizations/{Ofc}/stamps/{Iuv(979999)}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
