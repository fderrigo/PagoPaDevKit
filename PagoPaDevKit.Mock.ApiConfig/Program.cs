using Microsoft.EntityFrameworkCore;
using PagoPaDevKit.Mock.ApiConfig;

var builder = WebApplication.CreateBuilder(args);

var conn = builder.Configuration.GetConnectionString("ApiConfig") ?? "Data Source=apiconfig.db";
builder.Services.AddDbContext<ApiConfigDbContext>(o => o.UseSqlite(conn));

// Tassonomia ufficiale pagoPA caricata una volta in memoria.
builder.Services.AddSingleton<TaxonomyCatalog>();

// apiconfig usa snake_case (es. ci_owner, is_active, validity_date).
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
    o.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApiConfigDbContext>();
    db.Database.EnsureCreated();

    // Seed idempotente: ogni ente (creditor institution) ha il PROPRIO registro IBAN, come nel reale.
    // Garantisce IBAN attivi anche dopo recreate del container. Disabilitabile con ApiConfig:Seed=false.
    if (builder.Configuration.GetValue("ApiConfig:Seed", true))
    {
        // Lista da config "ApiConfig:SeedIbans" oppure default (ente di piattaforma + enti secondari per lo split).
        var seeds = builder.Configuration.GetSection("ApiConfig:SeedIbans").Get<List<SeedIbanCfg>>();
        if (seeds is null || seeds.Count == 0)
            seeds = new()
            {
                new("77777777777", "IT60X0542811101000000123456", "Comune Demo"),
                new("80012345678", "IT75A0300203280123456789012", "Regione Demo"),
                new("97103880585", "IT40S0306234210000001234567", "Agenzia Demo Servizi"),
            };
        foreach (var s in seeds.Where(s => !string.IsNullOrWhiteSpace(s.Ci) && !string.IsNullOrWhiteSpace(s.Iban)))
        {
            if (db.Ibans.Any(x => x.CreditorInstitutionCode == s.Ci && x.Iban == s.Iban)) continue;
            db.Ibans.Add(new IbanEntity
            {
                CreditorInstitutionCode = s.Ci, Iban = s.Iban, CompanyName = s.Company,
                Description = "IBAN demo (seed automatico)", IsActive = true,
                ValidityDate = DateTimeOffset.UtcNow, PublicationDate = DateTimeOffset.UtcNow
            });
        }
        db.SaveChanges();
    }
}

app.MapGet("/", () => "PagoPaDevKit Mock ApiConfig - non affiliato a PagoPA S.p.A.");
app.MapApiConfig();

app.Run();

// Esposto per WebApplicationFactory<ApiConfigApiMarker> nei test.
public partial class Program { }

// Voce di seed IBAN (ci = creditor institution / ente, iban, ragione sociale).
record SeedIbanCfg(string Ci, string Iban, string Company);
