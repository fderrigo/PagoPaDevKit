using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PagoPaDevKit.Tests;

// Test delle primitive SOAP paForNode sul filo reale (envelope SOAP 1.1 -> TestServer GPD).
[Collection("pagopa")]
public class PaForNodeSoapTests
{
    const string Ofc = "77777777777";
    const string Ns = "http://pagopa-api.pagopa.gov.it/pa/paForNode.xsd";
    readonly PagoPaFixture _fx;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public PaForNodeSoapTests(PagoPaFixture fx) => _fx = fx;

    static string Iuv(int n) => n.ToString("D17");
    static string Nav(int n) => "3" + Iuv(n);

    async Task Seed(int seed, DateTimeOffset due)
    {
        var body = new
        {
            iupd = $"SOAP-{seed}", payStandIn = true, companyName = "Comune Demo", officeName = "Tributi",
            paymentOption = new[]
            {
                new
                {
                    description = "TARI", switchToExpired = true,  // scade dopo la dueDate (caso "scaduto")
                    debtor = new { type = "F", fiscalCode = "RSSMRA80A01H501U", fullName = "Mario Rossi" },
                    installments = new[]
                    {
                        new { iuv = Iuv(seed), amount = 1050L, description = "TARI", dueDate = due,
                              transfer = new[] { new { idTransfer = "1", amount = 1050L, remittanceInformation = "TARI",
                                                       category = "9/0101108TS/", iban = "IT60X0542811101000000123456" } } }
                    }
                }
            }
        };
        var r = await _fx.GpdClient().PostAsJsonAsync($"/organizations/{Ofc}/debtpositions?toPublish=true", body, Json);
        r.EnsureSuccessStatusCode();
    }

    async Task<string> PostSoap(string action, string innerBody)
    {
        string env = $@"<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:t=""{Ns}"">
<soap:Body>{innerBody}</soap:Body></soap:Envelope>";
        var content = new StringContent(env, Encoding.UTF8, "text/xml");
        content.Headers.Add("SOAPAction", $"\"{action}\"");
        var http = _fx.GpdClient(withKey: false); // il Nodo non usa la subscription key APIM
        var r = await http.PostAsync("/gpd/nodo/paForNode.svc", content);
        return await r.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Verify_OK_ForValidNotice()
    {
        await Seed(2001, DateTimeOffset.UtcNow.AddDays(30));
        var body = $@"<t:PaVerifyPaymentNotice><t:request>
            <t:IdPA>{Ofc}</t:IdPA><t:IdBrokerPA>{Ofc}</t:IdBrokerPA><t:IdStation>STATION01</t:IdStation>
            <t:QrCode><t:FiscalCode>{Ofc}</t:FiscalCode><t:NoticeNumber>{Nav(2001)}</t:NoticeNumber></t:QrCode>
            </t:request></t:PaVerifyPaymentNotice>";
        var xml = await PostSoap("paVerifyPaymentNotice", body);
        Assert.Contains("<Outcome>OK</Outcome>", xml);
        Assert.Contains("Comune Demo", xml);
    }

    [Fact]
    public async Task Verify_KO_Sconosciuto_ForUnknownNav()
    {
        var body = $@"<t:PaVerifyPaymentNotice><t:request>
            <t:IdPA>{Ofc}</t:IdPA><t:IdBrokerPA>{Ofc}</t:IdBrokerPA><t:IdStation>STATION01</t:IdStation>
            <t:QrCode><t:FiscalCode>{Ofc}</t:FiscalCode><t:NoticeNumber>{Nav(999999)}</t:NoticeNumber></t:QrCode>
            </t:request></t:PaVerifyPaymentNotice>";
        var xml = await PostSoap("paVerifyPaymentNotice", body);
        Assert.Contains("<Outcome>KO</Outcome>", xml);
        Assert.Contains("PAA_PAGAMENTO_SCONOSCIUTO", xml);
    }

    [Fact]
    public async Task Verify_KO_Scaduto_ForExpiredNotice()
    {
        await Seed(2002, DateTimeOffset.UtcNow.AddDays(-2));
        var body = $@"<t:PaVerifyPaymentNotice><t:request>
            <t:IdPA>{Ofc}</t:IdPA><t:IdBrokerPA>{Ofc}</t:IdBrokerPA><t:IdStation>S</t:IdStation>
            <t:QrCode><t:FiscalCode>{Ofc}</t:FiscalCode><t:NoticeNumber>{Nav(2002)}</t:NoticeNumber></t:QrCode>
            </t:request></t:PaVerifyPaymentNotice>";
        var xml = await PostSoap("paVerifyPaymentNotice", body);
        Assert.Contains("<Outcome>KO</Outcome>", xml);
        Assert.Contains("PAA_PAGAMENTO_SCADUTO", xml);
    }

    [Fact]
    public async Task GetPaymentV2_ReturnsData()
    {
        await Seed(2003, DateTimeOffset.UtcNow.AddDays(30));
        var body = $@"<t:PaGetPaymentV2><t:request>
            <t:IdPA>{Ofc}</t:IdPA><t:IdBrokerPA>{Ofc}</t:IdBrokerPA><t:IdStation>S</t:IdStation>
            <t:QrCode><t:FiscalCode>{Ofc}</t:FiscalCode><t:NoticeNumber>{Nav(2003)}</t:NoticeNumber></t:QrCode>
            </t:request></t:PaGetPaymentV2>";
        var xml = await PostSoap("paGetPaymentV2", body);
        Assert.Contains("<Outcome>OK</Outcome>", xml);
        Assert.Contains(Iuv(2003), xml);          // creditorReferenceId
        Assert.Contains("RSSMRA80A01H501U", xml); // debtor
    }

    [Fact]
    public async Task SendRTV2_MarksPaid_AndStoresReceipt()
    {
        await Seed(2004, DateTimeOffset.UtcNow.AddDays(30));
        string nav = Nav(2004), iuv = Iuv(2004);
        var body = $@"<t:PaSendRTV2><t:request>
            <t:IdPA>{Ofc}</t:IdPA><t:IdBrokerPA>{Ofc}</t:IdBrokerPA><t:IdStation>S</t:IdStation>
            <t:Receipt>
              <t:ReceiptId>RCPT-2004</t:ReceiptId>
              <t:NoticeNumber>{nav}</t:NoticeNumber>
              <t:FiscalCode>{Ofc}</t:FiscalCode>
              <t:Outcome>OK</t:Outcome>
              <t:CreditorReferenceId>{iuv}</t:CreditorReferenceId>
              <t:PaymentAmount>10.50</t:PaymentAmount>
              <t:Description>TARI</t:Description>
              <t:CompanyName>Comune Demo</t:CompanyName>
              <t:PSPCompanyName>Banca Demo S.p.A.</t:PSPCompanyName>
              <t:Fee>1.50</t:Fee>
            </t:Receipt>
            </t:request></t:PaSendRTV2>";
        var xml = await PostSoap("paSendRTV2", body);
        Assert.Contains("<Outcome>OK</Outcome>", xml);

        // Posizione ora PAID.
        var dp = await _fx.GpdClient().GetFromJsonAsync<JsonElement>($"/organizations/{Ofc}/debtpositions/SOAP-2004");
        Assert.Equal("PAID", dp.GetProperty("status").GetString());
        var inst = dp.GetProperty("paymentOption")[0].GetProperty("installments")[0];
        Assert.Equal("PAID", inst.GetProperty("status").GetString());

        // Secondo invio -> duplicato.
        var xml2 = await PostSoap("paSendRTV2", body);
        Assert.Contains("PAA_PAGAMENTO_DUPLICATO", xml2);
    }
}
