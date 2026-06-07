using Microsoft.EntityFrameworkCore;
using PagoPaDevKit.Mock.Gpd;
using SoapCore;

var builder = WebApplication.CreateBuilder(args);

// SQLite: file in produzione mock, in-memory condiviso nei test (override via config).
var conn = builder.Configuration.GetConnectionString("Gpd") ?? "Data Source=gpd.db";
builder.Services.AddDbContext<GpdDbContext>(o => o.UseSqlite(conn));

// camelCase + niente null come le API pagoPA reali.
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// SOAP paForNode (primitive Nodo->EC). Servizio scoped: usa il GpdDbContext.
builder.Services.AddSoapCore();
builder.Services.AddScoped<IPaForNode, PaForNodeService>();

// Certificato dev per firmare i FlussoRiversamento (XML-DSig).
builder.Services.AddSingleton<DevSigningCert>();

// Generatore IUV lato EC per i pagamenti spontanei (avviso on-demand).
builder.Services.AddSingleton<SpontaneousIuv>();

// Consegna RT alla stazione broadcast dell'EC (paSendRT push) + worker di retry.
builder.Services.AddHttpClient("station");
builder.Services.AddSingleton<ReceiptDeliverySender>();
builder.Services.AddHostedService<ReceiptRetryWorker>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<GpdDbContext>().Database.EnsureCreated();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => "PagoPaDevKit Mock GPD - non affiliato a PagoPA S.p.A.");
app.MapGpd();
app.MapReceipts();
app.MapPaymentOptions();
app.MapEbollo();
app.MapFdr();
app.MapDev(); // endpoint dev-only (reset dati ente) — non fa parte del contratto pagoPA

// Endpoint SOAP paForNode: il Nodo (qui il mock Checkout) chiama qui.
((IApplicationBuilder)app).UseSoapEndpoint<IPaForNode>("/gpd/nodo/paForNode.svc", new SoapEncoderOptions(), SoapSerializer.DataContractSerializer);

app.Run();

// Esposto per WebApplicationFactory<Program> nei test di integrazione.
public partial class Program { }
