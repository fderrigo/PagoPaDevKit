using PagoPaDevKit.Mock.Checkout;

var builder = WebApplication.CreateBuilder(args);

// Frontend ASP.NET Core MVC (Controller + Razor views).
builder.Services.AddControllersWithViews();

// HttpClient verso il NODO (SOAP nodeForPsp).
builder.Services.AddHttpClient("node", c =>
    c.BaseAddress = new Uri(builder.Configuration["Checkout:NodoBaseUrl"] ?? "http://localhost:5004"));
builder.Services.AddScoped<NodeForPspClient>();
builder.Services.AddSingleton<PagoPaDevKit.Mock.Checkout.Models.CartStore>();

// Client REST verso GPD per le opzioni di pagamento (GET /payment-options).
builder.Services.AddHttpClient("gpd", c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Checkout:GpdBaseUrl"] ?? "http://localhost:5001");
    c.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", builder.Configuration["Checkout:SubscriptionKey"] ?? "dev-gpd-key");
});

var app = builder.Build();

app.UseStaticFiles();
app.MapControllers();

app.Run();

// Esposto per WebApplicationFactory<Program> nei test di integrazione.
public partial class Program { }
