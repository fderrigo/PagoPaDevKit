using Microsoft.EntityFrameworkCore;

namespace PagoPaDevKit.DemoEnte;

// Stato locale del "dovuto" nel gestionale dell'ente. È la vista dell'ente,
// separata da GPD: l'ente tiene traccia di cosa ha emesso e di cosa è stato pagato.
public class Dovuto
{
    public int Id { get; set; }
    public string Iupd { get; set; } = "";
    public string Nav { get; set; } = "";
    public string Iuv { get; set; } = "";
    public string DebtorFiscalCode { get; set; } = "";
    public string DebtorName { get; set; } = "";
    public string Description { get; set; } = "";
    public long Amount { get; set; }                 // centesimi
    public DateTimeOffset DueDate { get; set; }
    public string Status { get; set; } = "DA PAGARE"; // DA PAGARE / PAGATO
    public DateTimeOffset CreatedAt { get; set; }
    // Dati ricevuti dal webhook a pagamento avvenuto:
    public string? ReceiptId { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public string? PspCompanyName { get; set; }
    public long? Fee { get; set; }

    // Forma della posizione (riempiti leggendo GPD; non persistiti). Servono a mostrare in lista
    // se e rata unica / rateale / opzioni alternative senza un secondo giro su GPD.
    [System.ComponentModel.DataAnnotations.Schema.NotMapped] public int OptionCount { get; set; } = 1;
    [System.ComponentModel.DataAnnotations.Schema.NotMapped] public int InstallmentCount { get; set; } = 1;
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string Tipo => OptionCount > 1 ? "opzioni alternative" : InstallmentCount > 1 ? "rateale" : "rata unica";
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool IsMulti => OptionCount > 1 || InstallmentCount > 1;
}

// Log di una notifica webhook (paSendRTV2) ricevuta dalla stazione broadcast.
// Serve alla pagina "Log webhook" di Test Ente per ispezionare timestamp/payload/esito.
public class WebhookLog
{
    public int Id { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public string Nav { get; set; } = "";
    public string ReceiptId { get; set; } = "";
    public string Outcome { get; set; } = "";
    public long Fee { get; set; }                  // centesimi
    public decimal PaymentAmount { get; set; }
    public string PspCompanyName { get; set; } = "";
    public bool Matched { get; set; }              // true se il NAV corrispondeva a un dovuto
    public string ProcessResult { get; set; } = ""; // OK / KO + descrizione
    public string Payload { get; set; } = "";       // dump del receipt ricevuto
}

public class EnteDbContext : DbContext
{
    public EnteDbContext(DbContextOptions<EnteDbContext> options) : base(options) { }
    public DbSet<Dovuto> Dovuti => Set<Dovuto>();
    public DbSet<WebhookLog> WebhookLogs => Set<WebhookLog>();
    protected override void OnModelCreating(ModelBuilder b) =>
        b.Entity<Dovuto>().HasIndex(x => x.Nav).IsUnique();
}

// Form di creazione dovuto.
public class CreaDovutoForm
{
    public string DebtorFiscalCode { get; set; } = "";
    public string DebtorName { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal AmountEuro { get; set; }
    public DateTimeOffset DueDate { get; set; }
}
