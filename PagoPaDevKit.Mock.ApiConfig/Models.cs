using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace PagoPaDevKit.Mock.ApiConfig;

// =====================================================================
// ApiConfig: configurazione dell'Ente Creditore sul Nodo.
// Qui: registro IBAN per ente + Tassonomia dei servizi di incasso.
// Contratti fedeli a pagopa-api-config (IbanController / IbanEnhanced).
// =====================================================================

// ---------------------- Entità EF (IBAN) ----------------------
public class IbanEntity
{
    public int Id { get; set; }
    public string CreditorInstitutionCode { get; set; } = "";
    public string Iban { get; set; } = "";
    public string? CompanyName { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset ValidityDate { get; set; }
    public DateTimeOffset PublicationDate { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public List<IbanLabelEntity> Labels { get; set; } = new();
}

public class IbanLabelEntity
{
    public int Id { get; set; }
    public int IbanEntityId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
}

public class ApiConfigDbContext : DbContext
{
    public ApiConfigDbContext(DbContextOptions<ApiConfigDbContext> o) : base(o) { }
    public DbSet<IbanEntity> Ibans => Set<IbanEntity>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<IbanEntity>(e =>
        {
            e.HasIndex(x => new { x.CreditorInstitutionCode, x.Iban }).IsUnique();
            e.HasMany(x => x.Labels).WithOne().HasForeignKey(x => x.IbanEntityId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

// ---------------------- DTO IBAN (snake_case, come apiconfig) ----------------------
public class IbanLabel
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
}

public class IbanEnhanced
{
    public string Iban { get; set; } = "";          // "iban"
    public string? CiOwner { get; set; }             // "ci_owner"
    public string? CompanyName { get; set; }         // "company_name"
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;       // "is_active"
    public DateTimeOffset ValidityDate { get; set; } // "validity_date"
    public DateTimeOffset PublicationDate { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public List<IbanLabel> Labels { get; set; } = new();

    public static IbanEnhanced From(IbanEntity e) => new()
    {
        Iban = e.Iban, CiOwner = e.CreditorInstitutionCode, CompanyName = e.CompanyName,
        Description = e.Description, IsActive = e.IsActive, ValidityDate = e.ValidityDate,
        PublicationDate = e.PublicationDate, DueDate = e.DueDate,
        Labels = e.Labels.Select(l => new IbanLabel { Name = l.Name, Description = l.Description }).ToList()
    };
}

public class IbansEnhanced
{
    [System.Text.Json.Serialization.JsonPropertyName("ibans_enhanced")]
    public List<IbanEnhanced> IbansEnhancedList { get; set; } = new();
    [System.Text.Json.Serialization.JsonPropertyName("page_info")]
    public ApiConfigPageInfo PageInfo { get; set; } = new();
}

public class ApiConfigPageInfo
{
    public int Page { get; set; }
    public int Limit { get; set; }
    public int ItemsFound { get; set; }
    public int TotalPages { get; set; }
    public long TotalItems { get; set; }
}

// ---------------------- Tassonomia (Tipi Dovuto) ----------------------
// Caricata dal file ufficiale pagoPA (api.platform.pagopa.it/taxonomy). codiceTassonomico
// = campo "DATI SPECIFICI INCASSO" (es. "6/0708113AP/"), usato in transfer.category.
public class TaxonomyEntry
{
    public string CodiceTassonomico { get; set; } = "";
    public string CodiceTipoEnte { get; set; } = "";
    public string ProgressivoMacroArea { get; set; } = "";
    public string CodiceTipoServizio { get; set; } = "";
    public string MotivoRiscossione { get; set; } = "";
    public string TipoEnte { get; set; } = "";
    public string MacroArea { get; set; } = "";
    public string DenominazioneServizio { get; set; } = "";
    public string DescrizioneMotivoRiscossione { get; set; } = "";
    public int Versione { get; set; }
    public DateTimeOffset DataInizioValidita { get; set; }
    public DateTimeOffset? DataFineValidita { get; set; }
}

// Record del JSON ufficiale (chiavi con spazi/maiuscole).
public class TaxRecord
{
    [JsonPropertyName("CODICE TIPO ENTE CREDITORE")] public string? CodiceTipoEnte { get; set; }
    [JsonPropertyName("TIPO ENTE CREDITORE")] public string? TipoEnte { get; set; }
    [JsonPropertyName("PROGRESSIVO MACRO AREA PER ENTE CREDITORE")] public string? ProgMacroArea { get; set; }
    [JsonPropertyName("NOME MACRO AREA")] public string? NomeMacroArea { get; set; }
    [JsonPropertyName("CODICE TIPOLOGIA SERVIZIO")] public string? CodiceTipoServizio { get; set; }
    [JsonPropertyName("TIPO SERVIZIO")] public string? TipoServizio { get; set; }
    [JsonPropertyName("MOTIVO GIURIDICO DELLA RISCOSSIONE")] public string? Motivo { get; set; }
    [JsonPropertyName("DESCRIZIONE TIPO SERVIZIO")] public string? DescrizioneServizio { get; set; }
    [JsonPropertyName("VERSIONE TASSONOMIA")] public string? Versione { get; set; }
    [JsonPropertyName("DATI SPECIFICI INCASSO")] public string? CodiceTassonomico { get; set; }
    [JsonPropertyName("DATA INIZIO VALIDITA")] public string? DataInizio { get; set; }
    [JsonPropertyName("DATA FINE VALIDITA")] public string? DataFine { get; set; }
}

// Carica e tiene in memoria la tassonomia ufficiale (singleton).
public class TaxonomyCatalog
{
    public IReadOnlyList<TaxonomyEntry> Entries { get; }

    public TaxonomyCatalog(IWebHostEnvironment env, IConfiguration cfg)
    {
        var candidates = new[]
        {
            cfg["ApiConfig:TaxonomyFile"],
            Path.Combine(env.ContentRootPath, "Data", "tassonomia.json"),
            Path.Combine(AppContext.BaseDirectory, "Data", "tassonomia.json")
        };
        var path = candidates.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));
        if (path is null) { Entries = Array.Empty<TaxonomyEntry>(); return; }
        var records = JsonSerializer.Deserialize<List<TaxRecord>>(File.ReadAllText(path)) ?? new();
        Entries = records.Select(Map).ToList();
    }

    static DateTimeOffset? D(string? s) =>
        DateTimeOffset.TryParseExact(s, "dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var v) ? v : null;

    static TaxonomyEntry Map(TaxRecord r) => new()
    {
        CodiceTassonomico = r.CodiceTassonomico ?? "",
        CodiceTipoEnte = r.CodiceTipoEnte ?? "",
        ProgressivoMacroArea = r.ProgMacroArea ?? "",
        CodiceTipoServizio = r.CodiceTipoServizio ?? "",
        MotivoRiscossione = r.Motivo ?? "",
        TipoEnte = r.TipoEnte ?? "",
        MacroArea = r.NomeMacroArea ?? "",
        DenominazioneServizio = r.TipoServizio ?? "",
        DescrizioneMotivoRiscossione = r.DescrizioneServizio ?? "",
        Versione = int.TryParse(r.Versione, out var v) ? v : 0,
        DataInizioValidita = D(r.DataInizio) ?? default,
        DataFineValidita = D(r.DataFine)
    };
}
