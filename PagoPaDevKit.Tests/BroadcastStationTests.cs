using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PagoPaDevKit.DemoEnte;
using Xunit;

namespace PagoPaDevKit.Tests;

// Modello async, opzione consigliata: la RT viene consegnata in PUSH alla stazione broadcast
// dell'EC (DemoEnte SOAP). Se la consegna fallisce -> NOTICE_PENDING (retry).
[Collection("pagopa")]
public class BroadcastStationTests
{
    const string Ofc = "77777777777";
    const string Iban = "IT60X0542811101000000123456";
    readonly PagoPaFixture _fx;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public BroadcastStationTests(PagoPaFixture fx) => _fx = fx;

    static string Iuv(int n) => n.ToString("D17");
    static string Nav(int n) => "3" + Iuv(n);

    static FormUrlEncodedContent Pay(string nav) =>
        new(new Dictionary<string, string> { ["nav"] = nav, ["fiscalCode"] = Ofc, ["pan"] = "4242424242424242" });

    async Task SeedIban()
    {
        var json = $@"{{""iban"":""{Iban}"",""company_name"":""Comune Demo"",""is_active"":true}}";
        await _fx.ApiConfigClient().PostAsync($"/creditorinstitutions/{Ofc}/ibans", new StringContent(json, Encoding.UTF8, "application/json"));
    }

    string DeliveryStatus(JsonElement receipts, string iuv) =>
        receipts.GetProperty("results").EnumerateArray().First(x => x.GetProperty("iuv").GetString() == iuv).GetProperty("deliveryStatus").GetString()!;

    [Fact]
    public async Task PaidDovuto_ReceiptPushedToStation_DovutoPagato_AndDelivered()
    {
        await SeedIban();
        string desc = "BROADCAST TARI " + Guid.NewGuid().ToString("N")[..6];
        var form = new Dictionary<string, string>
        {
            ["debtorFiscalCode"] = "RSSMRA80A01H501U", ["debtorName"] = "Mario Rossi",
            ["description"] = desc, ["amountEuro"] = "12.50", ["dueDate"] = "2030-12-31",
            ["category"] = "9/0101002IM/", ["iban"] = Iban
        };
        var createResp = await _fx.EnteClient().PostAsync("/dovuti", new FormUrlEncodedContent(form));
        Assert.True(createResp.StatusCode == HttpStatusCode.Redirect, $"create -> {(int)createResp.StatusCode}: {await createResp.Content.ReadAsStringAsync()}");

        string nav, iuv;
        using (var scope = _fx.Ente.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EnteDbContext>();
            var d = db.Dovuti.Single(x => x.Description == desc);
            nav = d.Nav; iuv = d.Iuv;
        }

        var pay = await _fx.CheckoutClient().PostAsync("/pagamento", Pay(nav));
        Assert.Contains("riuscito", await pay.Content.ReadAsStringAsync());

        // PUSH: il dovuto è PAGATO senza che il gestionale abbia fatto pull.
        var dovuto = _fx.FindDovuto(nav);
        Assert.Equal("PAGATO", dovuto!.Status);
        Assert.False(string.IsNullOrEmpty(dovuto.ReceiptId));

        // GPD: RT consegnata alla stazione.
        var receipts = await _fx.GpdClient().GetFromJsonAsync<JsonElement>($"/payments/{Ofc}/receipts?pageSize=100");
        Assert.Equal("DELIVERED", DeliveryStatus(receipts, iuv));
    }

    [Fact]
    public async Task DeliveryToStationFails_ReceiptIsNoticePending()
    {
        // Posizione creata direttamente su GPD: la stazione (DemoEnte) NON ha questo dovuto -> KO -> NOTICE_PENDING.
        int seed = 90901;
        var body = new
        {
            iupd = $"NOSTATION-{seed}", payStandIn = true, companyName = "Comune Demo",
            paymentOption = new[]
            {
                new
                {
                    description = "x", switchToExpired = false,
                    debtor = new { type = "F", fiscalCode = "RSSMRA80A01H501U", fullName = "Mario Rossi" },
                    installments = new[]
                    {
                        new { iuv = Iuv(seed), amount = 1000L, description = "x", dueDate = DateTimeOffset.UtcNow.AddDays(30),
                              transfer = new[] { new { idTransfer = "1", amount = 1000L, remittanceInformation = "x",
                                                       category = "9/0101002IM/", iban = Iban } } }
                    }
                }
            }
        };
        (await _fx.GpdClient().PostAsJsonAsync($"/organizations/{Ofc}/debtpositions?toPublish=true", body, Json)).EnsureSuccessStatusCode();

        (await _fx.CheckoutClient().PostAsync("/pagamento", Pay(Nav(seed)))).EnsureSuccessStatusCode();

        var receipts = await _fx.GpdClient().GetFromJsonAsync<JsonElement>($"/payments/{Ofc}/receipts?pageSize=100");
        Assert.Equal("NOTICE_PENDING", DeliveryStatus(receipts, Iuv(seed)));
    }
}
