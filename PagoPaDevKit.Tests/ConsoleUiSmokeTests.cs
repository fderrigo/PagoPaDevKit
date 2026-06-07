using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PagoPaDevKit.Tests;

// Smoke test del console MVC DemoEnte: ogni pagina principale risponde 200 con il markup chiave.
// Verifica che il refactor del frontend non abbia lasciato pagine rotte o orfane.
[Collection("pagopa")]
public class ConsoleUiSmokeTests
{
    readonly PagoPaFixture _fx;
    public ConsoleUiSmokeTests(PagoPaFixture fx) => _fx = fx;

    async Task Page(string url, params string[] mustContain)
    {
        var resp = await _fx.EnteClient().GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var html = await resp.Content.ReadAsStringAsync();
        foreach (var m in mustContain)
            Assert.Contains(m, html);
    }

    [Fact] public Task Dashboard() => Page("/", "Dashboard", "Stato servizi");
    [Fact] public Task CreaDovuto() => Page("/ente/crea", "Crea posizione", "Rata unica");
    [Fact] public Task Posizioni() => Page("/ente/posizioni", "Posizioni debitorie");
    [Fact] public Task Ricevute() => Page("/ente/ricevute", "Ricevute");
    [Fact] public Task Webhook() => Page("/ente/webhook", "Log webhook");
    [Fact] public Task Avvisi() => Page("/ente/avvisi", "Stampa avvisi");
    [Fact] public Task Riconciliazione() => Page("/riconciliazione", "DATI MOCKATI", "Flussi di Rendicontazione", "Simula pubblicazione FdR");
    [Fact] public Task PagaCittadino() => Page("/cittadino/paga", "Paga un avviso", "ppa-stepper");
    [Fact] public Task Opzioni() => Page("/cittadino/opzioni", "Opzioni di pagamento");
    [Fact] public Task Spontaneo() => Page("/cittadino/spontaneo", "Pagamento spontaneo");
    [Fact] public Task Carte() => Page("/cittadino/carte", "Carte di test", "4242424242424242");
    [Fact] public Task StatoServizi() => Page("/infrastruttura", "Stato servizi");
    [Fact] public Task Scenari() => Page("/infrastruttura/scenari", "Scenari di test", "Esegui tutti");
    [Fact] public Task Strumenti() => Page("/infrastruttura/strumenti", "Strumenti", "Generatore IUV");
    [Fact] public Task Documentazione() => Page("/documentazione", "Documentazione");

    [Fact]
    public async Task SidebarPresenteOvunque()
    {
        var html = await (await _fx.EnteClient().GetAsync("/")).Content.ReadAsStringAsync();
        Assert.Contains("PagoPaDevKit", html);
        Assert.Contains("Test Ente", html);
        Assert.Contains("Test Cittadino", html);
        Assert.Contains("Infrastruttura", html);
    }

    // FdR: l'azione "simula pubblicazione dal PSP" risponde con redirect (post-redirect-get).
    [Fact]
    public async Task SimulaFdr_Redirects()
    {
        var resp = await _fx.EnteClient().PostAsync("/riconciliazione/simula", null);
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal("/riconciliazione", resp.Headers.Location?.OriginalString);
    }

    // FdR inesistente -> dettaglio e XML rispondono 404 (non 500).
    [Fact]
    public async Task FdrDetail_Unknown_404()
    {
        var det = await _fx.EnteClient().GetAsync("/riconciliazione/fdr/BANCADEMO/NON-ESISTE/1");
        Assert.Equal(HttpStatusCode.NotFound, det.StatusCode);
        var xml = await _fx.EnteClient().GetAsync("/riconciliazione/fdr/BANCADEMO/NON-ESISTE/1/xml");
        Assert.Equal(HttpStatusCode.NotFound, xml.StatusCode);
    }

    [Fact]
    public async Task HealthJson_Ok()
    {
        var resp = await _fx.EnteClient().GetAsync("/infrastruttura/health");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("services", await resp.Content.ReadAsStringAsync());
    }

    // Builder: crea una posizione complessa (2 opzioni alternative, una rateale, con split)
    // e verifica che GPD abbia memorizzato l'intera struttura v3.
    [Fact]
    public async Task Builder_CreatesComplexPosition_AllCasesStored()
    {
        const string Ofc = "77777777777";
        var payload = new
        {
            paymentOption = new object[]
            {
                new // opzione 1: unica soluzione, split su 2 transfer
                {
                    description = "Unica soluzione",
                    debtor = new { type = "F", fiscalCode = "RSSMRA80A01H501U", fullName = "Mario Rossi" },
                    installments = new object[]
                    {
                        new { amount = 20000L, description = "Totale", dueDate = "2030-12-31T00:00:00Z",
                              transfer = new object[]
                              {
                                  new { amount = 12000L, iban = "IT60X0542811101000000123456", category = "9/0101108TS/" },
                                  new { amount = 8000L, iban = "IT60X0542811101000000123456", category = "9/0101108TS/" },
                              } }
                    }
                },
                new // opzione 2: rateale, 2 rate
                {
                    description = "Piano rateale",
                    debtor = new { type = "F", fiscalCode = "RSSMRA80A01H501U", fullName = "Mario Rossi" },
                    installments = new object[]
                    {
                        new { amount = 10000L, description = "Rata 1", dueDate = "2030-06-30T00:00:00Z",
                              transfer = new object[] { new { amount = 10000L, iban = "IT60X0542811101000000123456", category = "9/0101108TS/" } } },
                        new { amount = 10000L, description = "Rata 2", dueDate = "2030-09-30T00:00:00Z",
                              transfer = new object[] { new { amount = 10000L, iban = "IT60X0542811101000000123456", category = "9/0101108TS/" } } },
                    }
                }
            }
        };

        var resp = await _fx.EnteClient().PostAsJsonAsync("/ente/posizioni", payload);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var res = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(res.GetProperty("ok").GetBoolean());
        string iupd = res.GetProperty("iupd").GetString()!;

        // GPD deve avere 2 opzioni; la seconda con 2 rate; la prima con 2 transfer.
        var dp = await _fx.GpdClient().GetFromJsonAsync<JsonElement>($"/organizations/{Ofc}/debtpositions/{iupd}");
        var opts = dp.GetProperty("paymentOption");
        Assert.Equal(2, opts.GetArrayLength());
        Assert.Equal(2, opts[0].GetProperty("installments")[0].GetProperty("transfer").GetArrayLength());
        Assert.Equal(2, opts[1].GetProperty("installments").GetArrayLength());
    }

    // Il client NON deve fermarsi alla busta PPT_ERRORE_EMESSO_DA_PAA: deve mostrare la causa
    // EC reale presa dagli original* (es. PAA_PAGAMENTO_SCONOSCIUTO su NAV inesistente).
    [Fact]
    public async Task Fault_SurfacesOriginalCause_NotJustWrapper()
    {
        const string Ofc = "77777777777";
        var resp = await _fx.CheckoutClient().GetAsync($"/pagamento?nav=347000000099999999&fiscalCode={Ofc}");
        var html = await resp.Content.ReadAsStringAsync();
        Assert.Contains("PAA_PAGAMENTO_SCONOSCIUTO", html);     // causa vera
        Assert.Contains("pagamento sconosciuto", html);          // originalFaultString
    }
}
