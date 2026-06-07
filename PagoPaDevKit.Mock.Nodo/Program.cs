using PagoPaDevKit.Mock.Nodo;
using SoapCore;

var builder = WebApplication.CreateBuilder(args);

// Il Nodo chiama l'EC (GPD) via SOAP paForNode.
builder.Services.AddHttpClient("gpd", c =>
    c.BaseAddress = new Uri(builder.Configuration["Nodo:GpdBaseUrl"] ?? "http://localhost:5001"));

builder.Services.AddSingleton<Wisp>();
builder.Services.AddSingleton<PaymentTokenStore>();
builder.Services.AddScoped<PaForNodeSoapClient>();
builder.Services.AddScoped<INodeForPsp, NodeForPspService>();
builder.Services.AddScoped<INodeForPa, NodeForPaService>();
builder.Services.AddSoapCore();

var app = builder.Build();

app.MapGet("/", () => "PagoPaDevKit Mock Nodo - non affiliato a PagoPA S.p.A.");

// Endpoint SOAP nodeForPsp: il PSP/Checkout chiama qui.
((IApplicationBuilder)app).UseSoapEndpoint<INodeForPsp>("/nodo/node/nodeForPsp.svc", new SoapEncoderOptions(), SoapSerializer.DataContractSerializer);
// Endpoint SOAP nodeForPa: l'EC chiede elenco/flusso di rendicontazione.
((IApplicationBuilder)app).UseSoapEndpoint<INodeForPa>("/nodo/node/nodeForPa.svc", new SoapEncoderOptions(), SoapSerializer.DataContractSerializer);

app.Run();

// Esposto per WebApplicationFactory<NodoApiMarker> nei test.
public partial class Program { }
