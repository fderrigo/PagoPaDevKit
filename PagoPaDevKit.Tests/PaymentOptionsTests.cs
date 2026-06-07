using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PagoPaDevKit.Tests;

// Opzioni di pagamento: piano rateale, opzioni alternative (scelta), stato PARTIALLY_PAID/PAID.
[Collection("pagopa")]
public class PaymentOptionsTests
{
    const string Ofc = "77777777777";
    const string Nns = "http://pagopa-api.pagopa.gov.it/node/nodeForPsp.xsd";
    readonly PagoPaFixture _fx;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public PaymentOptionsTests(PagoPaFixture fx) => _fx = fx;

    static string Iuv(int n) => n.ToString("D17");
    static string Nav(int n) => "3" + Iuv(n);

    static object Po(params (int iuv, long amount)[] insts) => new
    {
        description = "opzione",
        switchToExpired = false,
        debtor = new { type = "F", fiscalCode = "RSSMRA80A01H501U", fullName = "Mario Rossi" },
        installments = insts.Select(x => new
        {
            iuv = Iuv(x.iuv), amount = x.amount, description = "rata", dueDate = DateTimeOffset.UtcNow.AddDays(30),
            transfer = new[] { new { idTransfer = "1", amount = x.amount, remittanceInformation = "r",
                                     category = "9/0101002IM/", iban = "IT60X0542811101000000123456" } }
        }).ToArray()
    };

    async Task Create(string iupd, params object[] pos) =>
        (await _fx.GpdClient().PostAsJsonAsync($"/organizations/{Ofc}/debtpositions?toPublish=true",
            new { iupd, payStandIn = true, companyName = "Comune Demo", paymentOption = pos }, Json)).EnsureSuccessStatusCode();

    Task Pay(int iuv) => _fx.CheckoutClient().PostAsync("/pagamento",
        new FormUrlEncodedContent(new Dictionary<string, string> { ["nav"] = Nav(iuv), ["fiscalCode"] = Ofc, ["pan"] = "4242424242424242" }));

    async Task<string> Status(string iupd) =>
        (await _fx.GpdClient().GetFromJsonAsync<JsonElement>($"/organizations/{Ofc}/debtpositions/{iupd}")).GetProperty("status").GetString()!;

    [Fact]
    public async Task InstallmentPlan_PartialThenPaid()
    {
        await Create("PLAN-1", Po((110011, 500), (110012, 500)));

        var opts = await _fx.GpdClient().GetFromJsonAsync<JsonElement>($"/payment-options/organizations/{Ofc}/notices/{Nav(110011)}");
        Assert.Equal("INSTALLMENTS", opts.GetProperty("paymentOption")[0].GetProperty("optionType").GetString());
        Assert.Equal(2, opts.GetProperty("paymentOption")[0].GetProperty("numberOfInstallments").GetInt32());

        await Pay(110011);
        Assert.Equal("PARTIALLY_PAID", await Status("PLAN-1"));
        await Pay(110012);
        Assert.Equal("PAID", await Status("PLAN-1"));
    }

    [Fact]
    public async Task AlternativeOptions_PayingOneInvalidatesOther()
    {
        // Opzione A (piano rateale 2 rate) + Opzione B (unica soluzione).
        await Create("ALT-1", Po((110021, 500), (110022, 500)), Po((110023, 1000)));

        await Pay(110021); // pago una rata dell'opzione A
        Assert.Equal("PARTIALLY_PAID", await Status("ALT-1"));

        // L'opzione B ora è annullata: verify -> KO, il Nodo incapsula PAA_PAGAMENTO_ANNULLATO.
        string env = $@"<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:n=""{Nns}""><soap:Body>
<n:VerifyPaymentNotice><n:request><n:IdPSP>BANCADEMO</n:IdPSP><n:IdBrokerPSP>BANCADEMO</n:IdBrokerPSP><n:IdChannel>BANCADEMO_01</n:IdChannel>
<n:QrCode><n:FiscalCode>{Ofc}</n:FiscalCode><n:NoticeNumber>{Nav(110023)}</n:NoticeNumber></n:QrCode></n:request></n:VerifyPaymentNotice>
</soap:Body></soap:Envelope>";
        var c = new StringContent(env, Encoding.UTF8, "text/xml"); c.Headers.Add("SOAPAction", "\"verifyPaymentNotice\"");
        var xml = await (await _fx.NodoClient().PostAsync("/nodo/node/nodeForPsp.svc", c)).Content.ReadAsStringAsync();
        Assert.Contains("<Outcome>KO</Outcome>", xml);
        Assert.Contains("<originalFaultCode>PAA_PAGAMENTO_ANNULLATO</originalFaultCode>", xml);

        // Completo l'opzione A -> PAID.
        await Pay(110022);
        Assert.Equal("PAID", await Status("ALT-1"));
    }

    [Fact]
    public async Task Checkout_OpzioniPage_ListsInstallments()
    {
        await Create("OPZ-1", Po((110031, 500), (110032, 500)));
        var page = await _fx.CheckoutClient().GetAsync($"/opzioni?nav={Nav(110031)}&fiscalCode={Ofc}");
        var html = await page.Content.ReadAsStringAsync();
        Assert.True(page.StatusCode == HttpStatusCode.OK, $"{(int)page.StatusCode}: {html}");
        Assert.Contains("piano rateale", html);
        Assert.Contains(Nav(110032), html);
    }
}
