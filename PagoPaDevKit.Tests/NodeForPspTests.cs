using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace PagoPaDevKit.Tests;

// Test del NODO sul filo SOAP nodeForPsp (PSP -> Nodo -> EC/GPD).
[Collection("pagopa")]
public class NodeForPspTests
{
    const string Ofc = "77777777777";
    const string Nns = "http://pagopa-api.pagopa.gov.it/node/nodeForPsp.xsd";
    readonly PagoPaFixture _fx;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public NodeForPspTests(PagoPaFixture fx) => _fx = fx;

    static string Iuv(int n) => n.ToString("D17");
    static string Nav(int n) => "3" + Iuv(n);

    async Task Seed(int seed)
    {
        var body = new
        {
            iupd = $"NODE-{seed}", payStandIn = true, companyName = "Comune Demo",
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
    }

    async Task<XDocument> Soap(string action, string inner)
    {
        string env = $@"<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:n=""{Nns}"">
<soap:Body>{inner}</soap:Body></soap:Envelope>";
        var c = new StringContent(env, Encoding.UTF8, "text/xml");
        c.Headers.Add("SOAPAction", $"\"{action}\"");
        var r = await _fx.NodoClient().PostAsync("/nodo/node/nodeForPsp.svc", c);
        return XDocument.Parse(await r.Content.ReadAsStringAsync());
    }
    static string V(XDocument d, string l) => d.Descendants().FirstOrDefault(e => e.Name.LocalName == l)?.Value ?? "";

    [Fact]
    public async Task FullNodeFlow_Verify_Activate_SendOutcome_PaysPosition()
    {
        int seed = 5001;
        await Seed(seed);
        string nav = Nav(seed);

        // verifyPaymentNotice
        var verify = await Soap("verifyPaymentNotice", $@"<n:VerifyPaymentNotice><n:request>
<n:IdPSP>BANCADEMO</n:IdPSP><n:IdBrokerPSP>BANCADEMO</n:IdBrokerPSP><n:IdChannel>BANCADEMO_01</n:IdChannel>
<n:QrCode><n:FiscalCode>{Ofc}</n:FiscalCode><n:NoticeNumber>{nav}</n:NoticeNumber></n:QrCode>
</n:request></n:VerifyPaymentNotice>");
        Assert.Equal("OK", V(verify, "Outcome"));
        Assert.Contains("Comune Demo", verify.ToString());

        // activatePaymentNotice -> paymentToken
        var act = await Soap("activatePaymentNotice", $@"<n:ActivatePaymentNotice><n:request>
<n:IdPSP>BANCADEMO</n:IdPSP><n:IdBrokerPSP>BANCADEMO</n:IdBrokerPSP><n:IdChannel>BANCADEMO_01</n:IdChannel>
<n:IdempotencyKey>idem-1</n:IdempotencyKey>
<n:QrCode><n:FiscalCode>{Ofc}</n:FiscalCode><n:NoticeNumber>{nav}</n:NoticeNumber></n:QrCode>
<n:Amount>10.50</n:Amount></n:request></n:ActivatePaymentNotice>");
        Assert.Equal("OK", V(act, "Outcome"));
        string token = V(act, "PaymentToken");
        Assert.False(string.IsNullOrEmpty(token));
        Assert.Equal(Iuv(seed), V(act, "CreditorReferenceId"));

        // pspNotifyPaymentV2 (step 7-8) con paymentList
        var notify = await Soap("pspNotifyPaymentV2", $@"<n:PspNotifyPaymentV2><n:request>
<n:IdPSP>BANCADEMO</n:IdPSP><n:IdBrokerPSP>BANCADEMO</n:IdBrokerPSP><n:IdChannel>BANCADEMO_01</n:IdChannel>
<n:TransactionId>tx-1</n:TransactionId><n:TotalAmount>10.50</n:TotalAmount><n:Fee>1.50</n:Fee>
<n:PaymentList><n:payment><n:PaymentToken>{token}</n:PaymentToken><n:FiscalCodePA>{Ofc}</n:FiscalCodePA>
<n:CompanyName>Comune Demo</n:CompanyName><n:DebtAmount>10.50</n:DebtAmount></n:payment></n:PaymentList>
</n:request></n:PspNotifyPaymentV2>");
        Assert.Equal("OK", V(notify, "Outcome"));

        // sendPaymentOutcomeV2 (carrello: lista token)
        var send = await Soap("sendPaymentOutcomeV2", $@"<n:SendPaymentOutcomeV2><n:request>
<n:IdPSP>BANCADEMO</n:IdPSP><n:IdBrokerPSP>BANCADEMO</n:IdBrokerPSP><n:IdChannel>BANCADEMO_01</n:IdChannel>
<n:IdempotencyKey>idem-1</n:IdempotencyKey><n:PaymentTokens><n:paymentToken>{token}</n:paymentToken></n:PaymentTokens><n:Outcome>OK</n:Outcome>
<n:Details><n:PaymentMethod>CP</n:PaymentMethod><n:Fee>1.50</n:Fee></n:Details>
</n:request></n:SendPaymentOutcomeV2>");
        Assert.Equal("OK", V(send, "Outcome"));

        // Posizione su GPD ora PAID + ricevuta disponibile.
        var dp = await _fx.GpdClient().GetFromJsonAsync<JsonElement>($"/organizations/{Ofc}/debtpositions/NODE-{seed}");
        Assert.Equal("PAID", dp.GetProperty("status").GetString());
        var rec = await _fx.GpdClient().GetAsync($"/payments/{Ofc}/receipts/{Iuv(seed)}");
        Assert.Equal(System.Net.HttpStatusCode.OK, rec.StatusCode);
    }

    [Fact]
    public async Task Verify_UnknownNotice_KO()
    {
        var verify = await Soap("verifyPaymentNotice", $@"<n:VerifyPaymentNotice><n:request>
<n:IdPSP>BANCADEMO</n:IdPSP><n:IdBrokerPSP>BANCADEMO</n:IdBrokerPSP><n:IdChannel>BANCADEMO_01</n:IdChannel>
<n:QrCode><n:FiscalCode>{Ofc}</n:FiscalCode><n:NoticeNumber>{Nav(5999)}</n:NoticeNumber></n:QrCode>
</n:request></n:VerifyPaymentNotice>");
        Assert.Equal("KO", V(verify, "Outcome"));
    }
}
