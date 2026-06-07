using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace PagoPaDevKit.Tests;

// Carts API (eCommerce pagoPA): POST /carts -> 302 -> pagina pagamento -> pay -> redirect returnUrls.
[Collection("pagopa")]
public class CartsApiTests
{
    const string Ofc = "77777777777";
    readonly PagoPaFixture _fx;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public CartsApiTests(PagoPaFixture fx) => _fx = fx;

    static string Iuv(int n) => n.ToString("D17");
    static string Nav(int n) => "3" + Iuv(n);

    async Task<(string iupd, string nav)> Create(int seed)
    {
        var body = new
        {
            iupd = $"CARTS-{seed}", payStandIn = true, companyName = "Comune Demo",
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
        return ($"CARTS-{seed}", Nav(seed));
    }

    [Fact]
    public async Task PostCarts_302_ThenPay_RedirectsToReturnOkUrl_PositionPaid()
    {
        var (iupd, nav) = await Create(8201);

        var cart = new
        {
            paymentNotices = new[] { new { noticeNumber = nav, fiscalCode = Ofc, amount = 1000L, companyName = "Comune Demo", description = "TARI", allCCP = false } },
            returnUrls = new { returnOkUrl = "http://ente/esito", returnCancelUrl = "http://ente/cancel", returnErrorUrl = "http://ente/error", returnWaitingUrl = "http://ente/waiting" },
            idCart = "CART-1"
        };

        var noRedirect = _fx.Checkout.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var post = await noRedirect.PostAsJsonAsync("/carts", cart);
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        var loc = post.Headers.Location!.OriginalString;
        Assert.StartsWith("/pagamento?cartId=", loc);

        // Pagina di pagamento dal carrello.
        var page = await _fx.CheckoutClient().GetAsync(loc);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains(nav, await page.Content.ReadAsStringAsync());

        string cartId = loc.Split("cartId=")[1];
        var pay = await noRedirect.PostAsync("/pagamento", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["cartId"] = cartId, ["pan"] = "4242424242424242"
        }));
        Assert.Equal(HttpStatusCode.Redirect, pay.StatusCode);
        Assert.StartsWith("http://ente/esito?outcome=OK", pay.Headers.Location!.OriginalString);

        var dp = await _fx.GpdClient().GetFromJsonAsync<JsonElement>($"/organizations/{Ofc}/debtpositions/{iupd}");
        Assert.Equal("PAID", dp.GetProperty("status").GetString());
    }

    [Fact]
    public async Task PostCarts_TooManyNotices_400()
    {
        var notices = Enumerable.Range(1, 6).Select(i => new { noticeNumber = Nav(82100 + i), fiscalCode = Ofc, amount = 1000L, companyName = "C", description = "d", allCCP = false }).ToArray();
        var cart = new { paymentNotices = notices, returnUrls = new { returnOkUrl = "http://ente/esito" }, idCart = "X" };
        var post = await _fx.CheckoutClient().PostAsJsonAsync("/carts", cart);
        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
    }
}
