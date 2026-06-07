using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace PagoPaDevKit.Tests;

// Caso d'uso "pagamento presso frontend EC": carrello multi-avviso (max 5) + returnUrl.
[Collection("pagopa")]
public class CartPaymentTests
{
    const string Ofc = "77777777777";
    readonly PagoPaFixture _fx;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public CartPaymentTests(PagoPaFixture fx) => _fx = fx;

    static string Iuv(int n) => n.ToString("D17");
    static string Nav(int n) => "3" + Iuv(n);

    async Task<(string iupd, string nav)> Create(int seed)
    {
        var body = new
        {
            iupd = $"CART-{seed}", payStandIn = true, companyName = "Comune Demo",
            paymentOption = new[]
            {
                new
                {
                    description = "TARI", switchToExpired = false,
                    debtor = new { type = "F", fiscalCode = "RSSMRA80A01H501U", fullName = "Mario Rossi" },
                    installments = new[]
                    {
                        new { iuv = Iuv(seed), amount = 1000L, description = "TARI", dueDate = DateTimeOffset.UtcNow.AddDays(30),
                              transfer = new[] { new { idTransfer = "1", amount = 1000L, remittanceInformation = "TARI",
                                                       category = "9/0101002IM/", iban = "IT60X0542811101000000123456" } } }
                    }
                }
            }
        };
        (await _fx.GpdClient().PostAsJsonAsync($"/organizations/{Ofc}/debtpositions?toPublish=true", body, Json)).EnsureSuccessStatusCode();
        return ($"CART-{seed}", Nav(seed));
    }

    [Fact]
    public async Task Cart_TwoNotices_PaidTogether_RedirectsToReturnUrl()
    {
        var (iupd1, nav1) = await Create(8101);
        var (iupd2, nav2) = await Create(8102);

        // Carrello: due avvisi.
        var page = await _fx.CheckoutClient().GetAsync($"/pagamento?nav={nav1}&nav={nav2}&fiscalCode={Ofc}&returnUrl=http://ente/esito");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains(nav1, html);
        Assert.Contains(nav2, html);
        Assert.Contains("20.00", html); // totale 2 x 10.00

        // Pagamento unico, redirect al returnUrl dell'EC.
        var client = _fx.Checkout.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var form = new List<KeyValuePair<string, string>>
        {
            new("nav", nav1), new("nav", nav2), new("fiscalCode", Ofc),
            new("returnUrl", "http://ente/esito"), new("pan", "4242424242424242")
        };
        var pay = await client.PostAsync("/pagamento", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, pay.StatusCode);
        var loc = pay.Headers.Location!.OriginalString;
        Assert.StartsWith("http://ente/esito?outcome=OK", loc);
        Assert.Contains($"nav={nav1}", loc);
        Assert.Contains($"nav={nav2}", loc);

        // Entrambe le posizioni PAID.
        foreach (var iupd in new[] { iupd1, iupd2 })
        {
            var dp = await _fx.GpdClient().GetFromJsonAsync<JsonElement>($"/organizations/{Ofc}/debtpositions/{iupd}");
            Assert.Equal("PAID", dp.GetProperty("status").GetString());
        }
    }

    [Fact]
    public async Task Cart_KoCard_NoRedirect_PositionsStayValid()
    {
        var (iupd, nav) = await Create(8103);
        var form = new List<KeyValuePair<string, string>>
        {
            new("nav", nav), new("fiscalCode", Ofc), new("returnUrl", "http://ente/esito"), new("pan", "4000000000000002")
        };
        var client = _fx.Checkout.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var pay = await client.PostAsync("/pagamento", new FormUrlEncodedContent(form));
        // KO carta -> redirect returnUrl con outcome=KO.
        Assert.Equal(HttpStatusCode.Redirect, pay.StatusCode);
        Assert.Contains("outcome=KO", pay.Headers.Location!.OriginalString);
        var dp = await _fx.GpdClient().GetFromJsonAsync<JsonElement>($"/organizations/{Ofc}/debtpositions/{iupd}");
        Assert.Equal("VALID", dp.GetProperty("status").GetString());
    }
}
