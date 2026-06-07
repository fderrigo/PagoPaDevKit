using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PagoPaDevKit.Client;
using PagoPaDevKit.Mock.Checkout;
using PagoPaDevKit.Mock.Gpd;
using Xunit;

namespace PagoPaDevKit.Tests;

// =====================================================================
// Fixture end-to-end: avvia i tre servizi in-memory (GPD, Checkout,
// DemoEnte) e li collega tra loro instradando gli HttpClient verso i
// rispettivi TestServer. Riproduce il giro reale Ente -> GPD -> Checkout
// -> webhook senza porte TCP reali.
// =====================================================================
public class PagoPaFixture : IDisposable
{
    public const string Key = "test-key";

    public WebApplicationFactory<GpdApiMarker> Gpd { get; }
    public WebApplicationFactory<Mock.ApiConfig.ApiConfigApiMarker> ApiConfig { get; }
    public WebApplicationFactory<Mock.Nodo.NodoApiMarker> Nodo { get; }
    public WebApplicationFactory<CheckoutApiMarker> Checkout { get; }
    public WebApplicationFactory<DemoEnte.EnteApiMarker> Ente { get; }

    readonly SqliteConnection _gpdConn = new("DataSource=:memory:");
    readonly SqliteConnection _apiConfigConn = new("DataSource=:memory:");
    readonly SqliteConnection _enteConn = new("DataSource=:memory:");

    public PagoPaFixture()
    {
        _gpdConn.Open(); _apiConfigConn.Open(); _enteConn.Open();

        Gpd = new WebApplicationFactory<GpdApiMarker>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Gpd:SubscriptionKey", Key);
            b.UseSetting("Gpd:StationUrl", "http://localhost/stazione/paForNode.svc"); // stazione broadcast = DemoEnte
            b.UseSetting("Gpd:StationRetrySeconds", "3600"); // worker quasi inattivo nei test
            b.ConfigureTestServices(s =>
            {
                SwapSqlite<GpdDbContext>(s, _gpdConn);
                // Push paSendRT del GPD -> stazione SOAP del DemoEnte (lazy: Ente assegnato dopo).
                s.AddHttpClient("station").ConfigurePrimaryHttpMessageHandler(() => Ente.Server.CreateHandler());
            });
        });

        ApiConfig = new WebApplicationFactory<Mock.ApiConfig.ApiConfigApiMarker>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ApiConfig:SubscriptionKey", Key);
            b.UseSetting("ApiConfig:Seed", "false"); // niente seed nei test: registri deterministici
            b.ConfigureTestServices(s => SwapSqlite<Mock.ApiConfig.ApiConfigDbContext>(s, _apiConfigConn));
        });

        Ente = new WebApplicationFactory<DemoEnte.EnteApiMarker>().WithWebHostBuilder(b =>
        {
            b.UseSetting("PagoPaGpd:BaseUrl", "http://localhost");
            b.UseSetting("PagoPaGpd:SubscriptionKey", Key);
            b.UseSetting("ApiConfig:BaseUrl", "http://localhost");
            b.UseSetting("ApiConfig:SubscriptionKey", Key);
            b.UseSetting("Ente:FiscalCode", "77777777777");
            b.ConfigureTestServices(s =>
            {
                SwapSqlite<DemoEnte.EnteDbContext>(s, _enteConn);
                // Il client GPD del gestionale parla col TestServer GPD.
                s.AddHttpClient<PagoPaGpdClient>().ConfigurePrimaryHttpMessageHandler(() => Gpd.Server.CreateHandler());
                // Il client ApiConfig parla col TestServer ApiConfig.
                s.AddHttpClient<ApiConfigClient>().ConfigurePrimaryHttpMessageHandler(() => ApiConfig.Server.CreateHandler());
                // Client REST grezzo verso GPD (usato da FdrService, ScenarioRunner, opzioni cittadino).
                s.AddHttpClient("gpd-raw", c => c.BaseAddress = new Uri("http://localhost"))
                 .ConfigurePrimaryHttpMessageHandler(() => Gpd.Server.CreateHandler());
            });
        });

        // Nodo: chiama l'EC (GPD) via SOAP paForNode.
        Nodo = new WebApplicationFactory<Mock.Nodo.NodoApiMarker>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Nodo:GpdBaseUrl", "http://localhost");
            b.UseSetting("Nodo:SubscriptionKey", Key);
            b.ConfigureTestServices(s =>
                s.AddHttpClient("gpd", c => c.BaseAddress = new Uri("http://localhost"))
                 .ConfigurePrimaryHttpMessageHandler(() => Gpd.Server.CreateHandler()));
        });

        // Checkout = PSP: chiama il NODO via SOAP nodeForPsp e GPD per le opzioni.
        Checkout = new WebApplicationFactory<CheckoutApiMarker>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Checkout:NodoBaseUrl", "http://localhost");
            b.UseSetting("Checkout:GpdBaseUrl", "http://localhost");
            b.UseSetting("Checkout:SubscriptionKey", Key);
            b.ConfigureTestServices(s =>
            {
                s.AddHttpClient("node", c => c.BaseAddress = new Uri("http://localhost"))
                 .ConfigurePrimaryHttpMessageHandler(() => Nodo.Server.CreateHandler());
                // BaseAddress e key li imposta già il Program (da config); qui solo l'handler.
                s.AddHttpClient("gpd").ConfigurePrimaryHttpMessageHandler(() => Gpd.Server.CreateHandler());
            });
        });
    }

    public HttpClient GpdClient(bool withKey = true)
    {
        var c = Gpd.CreateClient();
        if (withKey) c.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", Key);
        return c;
    }
    public HttpClient CheckoutClient() => Checkout.CreateClient();
    public HttpClient NodoClient() => Nodo.CreateClient();
    public HttpClient ApiConfigClient(bool withKey = true)
    {
        var c = ApiConfig.CreateClient();
        if (withKey) c.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", Key);
        return c;
    }
    public HttpClient EnteClient() => Ente.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // Legge il DB del gestionale ente per le asserzioni.
    public DemoEnte.Dovuto? FindDovuto(string nav)
    {
        using var scope = Ente.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DemoEnte.EnteDbContext>();
        return db.Dovuti.FirstOrDefault(x => x.Nav == nav);
    }

    static void SwapSqlite<TCtx>(IServiceCollection s, SqliteConnection conn) where TCtx : DbContext
    {
        foreach (var d in s.Where(d => d.ServiceType == typeof(DbContextOptions<TCtx>)
                                    || d.ServiceType == typeof(DbContextOptions)).ToList())
            s.Remove(d);
        s.AddDbContext<TCtx>(o => o.UseSqlite(conn));
    }

    public void Dispose()
    {
        Gpd.Dispose(); ApiConfig.Dispose(); Nodo.Dispose(); Checkout.Dispose(); Ente.Dispose();
        _gpdConn.Dispose(); _apiConfigConn.Dispose(); _enteConn.Dispose();
    }
}

[CollectionDefinition("pagopa")]
public class PagoPaCollection : ICollectionFixture<PagoPaFixture> { }
