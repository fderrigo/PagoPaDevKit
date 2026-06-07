using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PagoPaDevKit.Tests;

// Riconciliazione contabile: i metadata transitano createPosition -> paGetPaymentV2 -> paSendRTV2 -> RT.
[Collection("pagopa")]
public class ReconciliationTests
{
    const string Ofc = "77777777777";
    readonly PagoPaFixture _fx;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public ReconciliationTests(PagoPaFixture fx) => _fx = fx;

    static string Iuv(int n) => n.ToString("D17");
    static string Nav(int n) => "3" + Iuv(n);

    [Fact]
    public async Task TransferMetadata_PropagatesToReceipt()
    {
        int seed = 95001;
        var body = new
        {
            iupd = $"RIC-{seed}", payStandIn = true, companyName = "Comune Demo",
            paymentOption = new[]
            {
                new
                {
                    description = "TARI", switchToExpired = false,
                    debtor = new { type = "F", fiscalCode = "RSSMRA80A01H501U", fullName = "Mario Rossi" },
                    installments = new[]
                    {
                        new { iuv = Iuv(seed), amount = 1000L, description = "TARI", dueDate = DateTimeOffset.UtcNow.AddDays(30),
                              transfer = new[] { new {
                                  idTransfer = "1", amount = 1000L, remittanceInformation = "TARI", category = "9/0101002IM/",
                                  iban = "IT60X0542811101000000123456",
                                  transferMetadata = new[] { new { key = "codiceCapitolo", value = "1.01.01" }, new { key = "codiceUfficio", value = "TRIB" } }
                              } } }
                    }
                }
            }
        };
        (await _fx.GpdClient().PostAsJsonAsync($"/organizations/{Ofc}/debtpositions?toPublish=true", body, Json)).EnsureSuccessStatusCode();

        // Paga (Checkout -> Nodo -> EC). I metadata viaggiano in paGetPaymentV2 e paSendRTV2.
        (await _fx.CheckoutClient().PostAsync("/pagamento",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["nav"] = Nav(seed), ["fiscalCode"] = Ofc, ["pan"] = "4242424242424242" }))).EnsureSuccessStatusCode();

        // La RT (XML) contiene i metadata di riconciliazione.
        var rt = await (await _fx.GpdClient().GetAsync($"/payments/{Ofc}/receipts/{Iuv(seed)}")).Content.ReadAsStringAsync();
        Assert.Contains("<metadata>", rt);
        Assert.Contains("codiceCapitolo", rt);
        Assert.Contains("1.01.01", rt);

        // La lista ricevute segnala la presenza dei metadata.
        var list = await _fx.GpdClient().GetFromJsonAsync<JsonElement>($"/payments/{Ofc}/receipts?pageSize=100");
        var item = list.GetProperty("results").EnumerateArray().First(x => x.GetProperty("iuv").GetString() == Iuv(seed));
        Assert.True(item.GetProperty("hasMetadata").GetBoolean());
    }

    [Fact]
    public async Task DemoEnte_RiconciliazionePage_Renders()
    {
        var resp = await _fx.EnteClient().GetAsync("/riconciliazione");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("riconciliazione contabile", (await resp.Content.ReadAsStringAsync()).ToLowerInvariant());
    }
}
