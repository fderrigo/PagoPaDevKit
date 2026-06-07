using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace PagoPaDevKit.Tests;

// Test FdR (pagopa-fdr): pubblicazione flusso lato PSP + recupero lato Organization,
// con rendicontazione (rata GPD -> REPORTED).
[Collection("pagopa")]
public class FdrTests
{
    const string Ofc = "77777777777";
    const string Psp = "BANCADEMO";
    const string Ns = "http://pagopa-api.pagopa.gov.it/pa/paForNode.xsd";
    readonly PagoPaFixture _fx;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public FdrTests(PagoPaFixture fx) => _fx = fx;

    static string Iuv(int n) => n.ToString("D17");
    static string Nav(int n) => "3" + Iuv(n);

    async Task SeedAndPay(int seed)
    {
        var body = new
        {
            iupd = $"FDR-{seed}", payStandIn = true, companyName = "Comune Demo",
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
        string env = $@"<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:t=""{Ns}""><soap:Body>
<t:PaSendRTV2><t:request><t:IdPA>{Ofc}</t:IdPA><t:IdBrokerPA>{Ofc}</t:IdBrokerPA><t:IdStation>S</t:IdStation>
<t:Receipt><t:ReceiptId>R-{seed}</t:ReceiptId><t:NoticeNumber>{Nav(seed)}</t:NoticeNumber><t:FiscalCode>{Ofc}</t:FiscalCode>
<t:Outcome>OK</t:Outcome><t:CreditorReferenceId>{Iuv(seed)}</t:CreditorReferenceId><t:PaymentAmount>10.50</t:PaymentAmount>
<t:Description>TARI</t:Description><t:CompanyName>Comune Demo</t:CompanyName><t:PSPCompanyName>Banca Demo</t:PSPCompanyName>
<t:Fee>1.50</t:Fee></t:Receipt></t:request></t:PaSendRTV2></soap:Body></soap:Envelope>";
        var c = new StringContent(env, Encoding.UTF8, "text/xml");
        c.Headers.Add("SOAPAction", "\"paSendRTV2\"");
        (await _fx.GpdClient(withKey: false).PostAsync("/gpd/nodo/paForNode.svc", c)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task FullFdrCycle_PublishReportsInstallment_AndOrgRetrieves()
    {
        int seed = 4001;
        await SeedAndPay(seed);
        string fdr = "2026-06-04PSPDEMO-1";
        var gpd = _fx.GpdClient();

        // PSP crea flusso
        var create = new
        {
            fdr, fdrDate = DateTimeOffset.UtcNow,
            sender = new { type = "LEGAL_PERSON", id = Psp, pspId = Psp, pspName = "Banca Demo" },
            receiver = new { id = Ofc, organizationId = Ofc, organizationName = "Comune Demo" },
            totPayments = 1, sumPayments = 10.50m
        };
        (await gpd.PostAsJsonAsync($"/psps/{Psp}/fdrs/{fdr}", create, Json)).EnsureSuccessStatusCode();

        // PSP aggiunge pagamento
        var add = new { payments = new[] { new { index = 1L, iuv = Iuv(seed), iur = "IUR-1", idTransfer = 1, pay = 10.50m, payStatus = "EXECUTED", payDate = DateTimeOffset.UtcNow } } };
        (await gpd.PutAsJsonAsync($"/psps/{Psp}/fdrs/{fdr}/payments/add", add, Json)).EnsureSuccessStatusCode();

        // PSP pubblica
        (await gpd.PostAsync($"/psps/{Psp}/fdrs/{fdr}/publish", null)).EnsureSuccessStatusCode();

        // Rata GPD ora REPORTED
        var dp = await gpd.GetFromJsonAsync<JsonElement>($"/organizations/{Ofc}/debtpositions/FDR-{seed}");
        Assert.Equal("REPORTED", dp.GetProperty("paymentOption")[0].GetProperty("installments")[0].GetProperty("status").GetString());

        // Organization recupera la lista flussi
        var flows = await gpd.GetFromJsonAsync<JsonElement>($"/organizations/{Ofc}/fdrs?pspId={Psp}");
        Assert.True(flows.GetProperty("count").GetInt64() >= 1);
        Assert.Contains(fdr, flows.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("fdr").GetString()));

        // Single flow + payments
        var single = await gpd.GetFromJsonAsync<JsonElement>($"/organizations/{Ofc}/fdrs/{fdr}/revisions/1/psps/{Psp}");
        Assert.Equal("PUBLISHED", single.GetProperty("status").GetString());
        Assert.Equal(1, single.GetProperty("computedTotPayments").GetInt32());

        var pays = await gpd.GetFromJsonAsync<JsonElement>($"/organizations/{Ofc}/fdrs/{fdr}/revisions/1/psps/{Psp}/payments");
        Assert.Equal(Iuv(seed), pays.GetProperty("data")[0].GetProperty("iuv").GetString());
    }

    [Fact]
    public async Task CreatedFlow_CanBeDeleted_BeforePublish()
    {
        string fdr = "DRAFT-FLOW-1";
        var gpd = _fx.GpdClient();
        var create = new
        {
            fdr, fdrDate = DateTimeOffset.UtcNow,
            sender = new { type = "LEGAL_PERSON", id = Psp, pspId = Psp, pspName = "Banca Demo" },
            receiver = new { id = Ofc, organizationId = Ofc, organizationName = "Comune Demo" },
            totPayments = 0, sumPayments = 0m
        };
        (await gpd.PostAsJsonAsync($"/psps/{Psp}/fdrs/{fdr}", create, Json)).EnsureSuccessStatusCode();
        var del = await gpd.DeleteAsync($"/psps/{Psp}/fdrs/{fdr}");
        Assert.Equal(System.Net.HttpStatusCode.OK, del.StatusCode);
    }
}
