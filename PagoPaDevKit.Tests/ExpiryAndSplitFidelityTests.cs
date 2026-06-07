using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PagoPaDevKit.Tests;

// Fedeltà GPD v3: switchToExpired (default false = pagabile dopo scadenza), scadenza inclusiva
// (oggi pagabile), e organizationFiscalCode per transfer (split su più enti).
[Collection("pagopa")]
public class ExpiryAndSplitFidelityTests
{
    const string Ofc = "77777777777";
    readonly PagoPaFixture _fx;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public ExpiryAndSplitFidelityTests(PagoPaFixture fx) => _fx = fx;

    static string Iuv(int n) => n.ToString("D17");

    static object Body(int seed, DateTimeOffset due, bool switchToExpired, object[]? transfers = null) => new
    {
        iupd = $"FID-{seed}", payStandIn = true, companyName = "Comune Demo",
        paymentOption = new[]
        {
            new
            {
                description = "x", switchToExpired,
                debtor = new { type = "F", fiscalCode = "RSSMRA80A01H501U", fullName = "Mario Rossi" },
                installments = new[]
                {
                    new { iuv = Iuv(seed), amount = 1000L, description = "x", dueDate = due,
                          transfer = transfers ?? new object[] { new { idTransfer = "1", amount = 1000L, remittanceInformation = "x",
                              category = "9/0101108TS/", iban = "IT60X0542811101000000123456" } } }
                }
            }
        }
    };

    async Task<string> Create(int seed, DateTimeOffset due, bool switchToExpired, object[]? transfers = null)
    {
        var r = await _fx.GpdClient().PostAsJsonAsync($"/organizations/{Ofc}/debtpositions?toPublish=true", Body(seed, due, switchToExpired, transfers), Json);
        r.EnsureSuccessStatusCode();
        return $"FID-{seed}";
    }

    async Task<string?> Status(string iupd) =>
        (await _fx.GpdClient().GetFromJsonAsync<JsonElement>($"/organizations/{Ofc}/debtpositions/{iupd}")).GetProperty("status").GetString();

    [Fact]
    public async Task DueToday_StillPayable()
    {
        // Scadenza = oggi: l'avviso e ancora pagabile (scadenza inclusiva), anche con switchToExpired.
        var iupd = await Create(60101, DateTimeOffset.UtcNow, switchToExpired: true);
        Assert.Equal("VALID", await Status(iupd));
    }

    [Fact]
    public async Task PastDue_DefaultSwitchToExpired_StaysPayable()
    {
        // switchToExpired=false (default): scaduta da giorni ma RESTA pagabile (VALID).
        var iupd = await Create(60102, DateTimeOffset.UtcNow.AddDays(-5), switchToExpired: false);
        Assert.Equal("VALID", await Status(iupd));
    }

    [Fact]
    public async Task PastDue_SwitchToExpired_BecomesUnpayable()
    {
        var iupd = await Create(60103, DateTimeOffset.UtcNow.AddDays(-5), switchToExpired: true);
        Assert.Equal("UNPAYABLE", await Status(iupd));
    }

    [Fact]
    public async Task Transfer_DefaultsOrganizationFiscalCode_ToOfc()
    {
        // organizationFiscalCode omesso -> default all'EC primario (la PA del path).
        var iupd = await Create(60104, DateTimeOffset.UtcNow.AddDays(30), switchToExpired: false);
        var dp = await _fx.GpdClient().GetFromJsonAsync<JsonElement>($"/organizations/{Ofc}/debtpositions/{iupd}");
        var t = dp.GetProperty("paymentOption")[0].GetProperty("installments")[0].GetProperty("transfer")[0];
        Assert.Equal(Ofc, t.GetProperty("organizationFiscalCode").GetString());
    }

    [Fact]
    public async Task Split_TwoEnti_StoredPerTransfer()
    {
        var transfers = new object[]
        {
            new { idTransfer = "1", amount = 600L, remittanceInformation = "quota ente", category = "9/0101108TS/",
                  iban = "IT60X0542811101000000123456", organizationFiscalCode = Ofc, companyName = "Comune Demo" },
            new { idTransfer = "2", amount = 400L, remittanceInformation = "quota regione", category = "9/0101108TS/",
                  iban = "IT60X0542811101000000123456", organizationFiscalCode = "80012345678", companyName = "Regione Demo" },
        };
        var iupd = await Create(60105, DateTimeOffset.UtcNow.AddDays(30), switchToExpired: false, transfers);
        var dp = await _fx.GpdClient().GetFromJsonAsync<JsonElement>($"/organizations/{Ofc}/debtpositions/{iupd}");
        var arr = dp.GetProperty("paymentOption")[0].GetProperty("installments")[0].GetProperty("transfer");
        Assert.Equal(2, arr.GetArrayLength());
        Assert.Equal(Ofc, arr[0].GetProperty("organizationFiscalCode").GetString());
        Assert.Equal("80012345678", arr[1].GetProperty("organizationFiscalCode").GetString());
    }
}
