using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PagoPaDevKit.Tests;

// Test Receipts REST (gpd-payments): la ricevuta nasce da paSendRTV2.
[Collection("pagopa")]
public class ReceiptsTests
{
    const string Ofc = "77777777777";
    const string Ns = "http://pagopa-api.pagopa.gov.it/pa/paForNode.xsd";
    readonly PagoPaFixture _fx;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public ReceiptsTests(PagoPaFixture fx) => _fx = fx;

    static string Iuv(int n) => n.ToString("D17");
    static string Nav(int n) => "3" + Iuv(n);

    async Task SeedAndPay(int seed)
    {
        var body = new
        {
            iupd = $"RCP-{seed}", payStandIn = true, companyName = "Comune Demo",
            paymentOption = new[]
            {
                new
                {
                    description = "TARI", switchToExpired = false,
                    debtor = new { type = "F", fiscalCode = "RSSMRA80A01H501U", fullName = "Mario Rossi" },
                    installments = new[]
                    {
                        new { iuv = Iuv(seed), amount = 1050L, description = "TARI", dueDate = DateTimeOffset.UtcNow.AddDays(30),
                              transfer = new[] { new { idTransfer = "1", amount = 1050L, remittanceInformation = "TARI",
                                                       category = "9/0101108TS/", iban = "IT60X0542811101000000123456" } } }
                    }
                }
            }
        };
        (await _fx.GpdClient().PostAsJsonAsync($"/organizations/{Ofc}/debtpositions?toPublish=true", body, Json)).EnsureSuccessStatusCode();

        string env = $@"<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:t=""{Ns}"">
<soap:Body><t:PaSendRTV2><t:request>
 <t:IdPA>{Ofc}</t:IdPA><t:IdBrokerPA>{Ofc}</t:IdBrokerPA><t:IdStation>S</t:IdStation>
 <t:Receipt><t:ReceiptId>RCPT-{seed}</t:ReceiptId><t:NoticeNumber>{Nav(seed)}</t:NoticeNumber>
  <t:FiscalCode>{Ofc}</t:FiscalCode><t:Outcome>OK</t:Outcome><t:CreditorReferenceId>{Iuv(seed)}</t:CreditorReferenceId>
  <t:PaymentAmount>10.50</t:PaymentAmount><t:Description>TARI</t:Description><t:CompanyName>Comune Demo</t:CompanyName>
  <t:PSPCompanyName>Banca Demo S.p.A.</t:PSPCompanyName><t:Fee>1.50</t:Fee></t:Receipt>
</t:request></t:PaSendRTV2></soap:Body></soap:Envelope>";
        var content = new StringContent(env, Encoding.UTF8, "text/xml");
        content.Headers.Add("SOAPAction", "\"paSendRTV2\"");
        (await _fx.GpdClient(withKey: false).PostAsync("/gpd/nodo/paForNode.svc", content)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ListReceipts_ReturnsPaidOne()
    {
        await SeedAndPay(3001);
        var res = await _fx.GpdClient().GetFromJsonAsync<JsonElement>($"/payments/{Ofc}/receipts?pageSize=100");
        Assert.True(res.GetProperty("totalPages").GetInt32() >= 1);
        bool found = res.GetProperty("results").EnumerateArray().Any(x => x.GetProperty("iuv").GetString() == Iuv(3001));
        Assert.True(found);
    }

    [Fact]
    public async Task GetReceiptByIuv_ReturnsXml()
    {
        await SeedAndPay(3002);
        var resp = await _fx.GpdClient().GetAsync($"/payments/{Ofc}/receipts/{Iuv(3002)}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("application/xml", resp.Content.Headers.ContentType!.MediaType);
        var xml = await resp.Content.ReadAsStringAsync();
        Assert.Contains("RCPT-3002", xml);
        Assert.Contains(Iuv(3002), xml);
    }

    [Fact]
    public async Task GetReceiptByIuv_404_WhenMissing()
    {
        var resp = await _fx.GpdClient().GetAsync($"/payments/{Ofc}/receipts/{Iuv(399999)}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
