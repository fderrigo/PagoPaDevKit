using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PagoPaDevKit.Tests;

// Test API GPD Debt Positions v3 (REST). Fase 1 del devkit.
[Collection("pagopa")]
public class GpdV3Tests
{
    const string Ofc = "77777777777";
    readonly PagoPaFixture _fx;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public GpdV3Tests(PagoPaFixture fx) => _fx = fx;

    static string Iuv(int n) => n.ToString("D17");
    static string Nav(int n) => "3" + Iuv(n);

    static object Body(int seed, DateTimeOffset due, long amount = 1000) => new
    {
        iupd = $"IUPD-{seed}",
        payStandIn = true,
        companyName = "Comune Demo",
        officeName = "Ufficio Tributi",
        paymentOption = new[]
        {
            new
            {
                description = "TARI 2026",
                switchToExpired = true,  // scade dopo la dueDate (necessario per il caso "scaduto")
                debtor = new { type = "F", fiscalCode = "RSSMRA80A01H501U", fullName = "Mario Rossi" },
                installments = new[]
                {
                    new
                    {
                        iuv = Iuv(seed), amount, description = "TARI 2026", dueDate = due,
                        transfer = new[]
                        {
                            new { idTransfer = "1", amount, remittanceInformation = "TARI",
                                  category = "9/0101108TS/", iban = "IT60X0542811101000000123456" }
                        }
                    }
                }
            }
        }
    };

    async Task<HttpResponseMessage> Create(int seed, bool publish, DateTimeOffset due, long amount = 1000) =>
        await _fx.GpdClient().PostAsJsonAsync(
            $"/organizations/{Ofc}/debtpositions?toPublish={publish.ToString().ToLowerInvariant()}", Body(seed, due, amount), Json);

    [Fact]
    public async Task Create_WithToPublish_Returns201_Valid_WithNav()
    {
        var resp = await Create(1001, publish: true, due: DateTimeOffset.UtcNow.AddDays(30));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var dp = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("VALID", dp.GetProperty("status").GetString());
        var inst = dp.GetProperty("paymentOption")[0].GetProperty("installments")[0];
        Assert.Equal(Nav(1001), inst.GetProperty("nav").GetString());
        Assert.Equal("Mario Rossi", dp.GetProperty("paymentOption")[0].GetProperty("debtor").GetProperty("fullName").GetString());
    }

    [Fact]
    public async Task Get_ReturnsPosition()
    {
        await Create(1002, publish: true, due: DateTimeOffset.UtcNow.AddDays(30));
        var dp = await _fx.GpdClient().GetFromJsonAsync<JsonElement>($"/organizations/{Ofc}/debtpositions/IUPD-1002");
        Assert.Equal("IUPD-1002", dp.GetProperty("iupd").GetString());
    }

    [Fact]
    public async Task List_IsPaginated_AndFinds()
    {
        await Create(1003, publish: true, due: DateTimeOffset.UtcNow.AddDays(30));
        var list = await _fx.GpdClient().GetFromJsonAsync<JsonElement>($"/organizations/{Ofc}/debtpositions?limit=50");
        Assert.True(list.GetProperty("page_info").GetProperty("total_items").GetInt64() >= 1);
        Assert.True(list.GetProperty("payment_position_list").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Update_ResetsToDraft()
    {
        await Create(1004, publish: true, due: DateTimeOffset.UtcNow.AddDays(30));
        var resp = await _fx.GpdClient().PutAsJsonAsync(
            $"/organizations/{Ofc}/debtpositions/IUPD-1004", Body(1004, DateTimeOffset.UtcNow.AddDays(60)), Json);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dp = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DRAFT", dp.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Delete_ThenGet404()
    {
        await Create(1005, publish: false, due: DateTimeOffset.UtcNow.AddDays(30));
        var del = await _fx.GpdClient().DeleteAsync($"/organizations/{Ofc}/debtpositions/IUPD-1005");
        Assert.Equal(HttpStatusCode.OK, del.StatusCode);
        var get = await _fx.GpdClient().GetAsync($"/organizations/{Ofc}/debtpositions/IUPD-1005");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    [Fact]
    public async Task Publish_DraftBecomesValid()
    {
        await Create(1006, publish: false, due: DateTimeOffset.UtcNow.AddDays(30));
        var pub = await _fx.GpdClient().PostAsync($"/organizations/{Ofc}/debtpositions/IUPD-1006/publish", null);
        Assert.Equal(HttpStatusCode.OK, pub.StatusCode);
        var dp = await pub.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("VALID", dp.GetProperty("status").GetString());
    }

    [Fact]
    public async Task DuplicateIupd_409()
    {
        await Create(1007, publish: false, due: DateTimeOffset.UtcNow.AddDays(30));
        var dup = await Create(1007, publish: false, due: DateTimeOffset.UtcNow.AddDays(30));
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task MissingDebtor_400()
    {
        var body = new
        {
            iupd = "IUPD-1008", payStandIn = true, companyName = "Comune Demo",
            paymentOption = new[]
            {
                new
                {
                    description = "x", switchToExpired = false,
                    debtor = new { type = "F" }, // fiscalCode/fullName mancanti
                    installments = new[]
                    {
                        new { iuv = Iuv(1008), amount = 1000L, description = "x", dueDate = DateTimeOffset.UtcNow.AddDays(10),
                              transfer = new[] { new { idTransfer = "1", amount = 1000L, remittanceInformation = "x",
                                                       category = "9/x/", iban = "IT60X0542811101000000123456" } } }
                    }
                }
            }
        };
        var resp = await _fx.GpdClient().PostAsJsonAsync($"/organizations/{Ofc}/debtpositions", body, Json);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("fiscalCode", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Expired_BecomesUnpayable()
    {
        await Create(1009, publish: true, due: DateTimeOffset.UtcNow.AddDays(-1));
        var dp = await _fx.GpdClient().GetFromJsonAsync<JsonElement>($"/organizations/{Ofc}/debtpositions/IUPD-1009");
        Assert.Equal("UNPAYABLE", dp.GetProperty("status").GetString());
    }

    [Fact]
    public async Task WrongOrMissingKey_401()
    {
        var noKey = _fx.GpdClient(withKey: false);
        var r1 = await noKey.GetAsync($"/organizations/{Ofc}/debtpositions/whatever");
        Assert.Equal(HttpStatusCode.Unauthorized, r1.StatusCode);
        Assert.Contains("missing subscription key", await r1.Content.ReadAsStringAsync());

        var bad = _fx.Gpd.CreateClient();
        bad.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", "wrong");
        var r2 = await bad.GetAsync($"/organizations/{Ofc}/debtpositions/whatever");
        Assert.Equal(HttpStatusCode.Unauthorized, r2.StatusCode);
        Assert.Contains("invalid subscription key", await r2.Content.ReadAsStringAsync());
    }
}
