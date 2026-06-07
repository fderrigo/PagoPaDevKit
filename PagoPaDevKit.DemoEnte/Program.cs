using Microsoft.EntityFrameworkCore;
using PagoPaDevKit.Client;
using PagoPaDevKit.DemoEnte;
using SoapCore;

var builder = WebApplication.CreateBuilder(args);

// Frontend ASP.NET Core MVC (Controller + Razor views) — console PagoPaDevKit.
builder.Services.AddControllersWithViews();

// Stazione broadcast: endpoint SOAP paSendRTV2 che riceve la RT in push dall'EC/Nodo.
builder.Services.AddSoapCore();
builder.Services.AddScoped<IStation, StationService>();

var conn = builder.Configuration.GetConnectionString("Ente") ?? "Data Source=ente.db";
builder.Services.AddDbContext<EnteDbContext>(o => o.UseSqlite(conn));

// Client GPD: si binda dal profilo attivo (Mock o Uat) in appsettings.
builder.Services.AddPagoPaGpdClient(o => builder.Configuration.GetSection("PagoPaGpd").Bind(o));

// Client ApiConfig: registro IBAN + tassonomia dell'ente.
builder.Services.AddApiConfigClient(o => builder.Configuration.GetSection("ApiConfig").Bind(o));

// Client verso Checkout (Carts API). Niente auto-redirect: serve leggere il Location del 302.
builder.Services.AddHttpClient("checkout", c =>
    c.BaseAddress = new Uri(builder.Configuration["Ente:CheckoutApiUrl"] ?? "http://localhost:5002"))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

// Client SOAP verso il NODO (nodeForPsp): usato dallo stepper "Test Cittadino" in-console.
builder.Services.AddHttpClient("node", c =>
    c.BaseAddress = new Uri(builder.Configuration["Ente:NodoBaseUrl"] ?? "http://localhost:5004"));
builder.Services.AddScoped<NodeForPspClient>();

// Client REST grezzi verso GPD/ApiConfig per gli Scenari di test (es. chiave errata, IUPD duplicato).
builder.Services.AddHttpClient("gpd-raw", c =>
    c.BaseAddress = new Uri(builder.Configuration["PagoPaGpd:BaseUrl"] ?? "http://localhost:5001"));

// Generatore IUV con segregation code dell'ente. Singleton: mantiene il progressivo.
// Progressivo iniziale seminato dal tempo: evita collisioni IUV (409) tra riavvii del
// processo quando il DB GPD persiste posizioni create in run precedenti.
int seg = builder.Configuration.GetValue("Ente:SegregationCode", 47);
long progIniziale = DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 9_000_000_000L + 1;
builder.Services.AddSingleton(new GeneratoreIuv(seg, progIniziale));

// Health check live dei servizi + runner degli scenari end-to-end.
builder.Services.AddHttpClient("health");
builder.Services.AddScoped<ServiceHealthService>();
builder.Services.AddScoped<ScenarioRunner>();

// Flussi di Rendicontazione (FdR): recupero lato Organization + simulazione pubblicazione lato PSP.
builder.Services.AddScoped<FdrService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<EnteDbContext>().Database.EnsureCreated();

app.UseStaticFiles();

// Endpoint leggero per health check / self-ping (niente render MVC).
app.MapGet("/healthz", () => Results.Text("ok"));

app.MapControllers();
app.MapControllerRoute(name: "default", pattern: "{controller=Dashboard}/{action=Index}/{id?}");

// Endpoint SOAP della stazione broadcast (riceve paSendRTV2 in push).
((IApplicationBuilder)app).UseSoapEndpoint<IStation>("/stazione/paForNode.svc", new SoapEncoderOptions(), SoapSerializer.DataContractSerializer);

app.Run();

// Esposto per WebApplicationFactory<Program> nei test di integrazione.
public partial class Program { }
