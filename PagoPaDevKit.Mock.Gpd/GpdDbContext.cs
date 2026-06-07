using Microsoft.EntityFrameworkCore;

namespace PagoPaDevKit.Mock.Gpd;

// DbContext usato direttamente negli endpoint. Nessun Repository/UoW.
public class GpdDbContext : DbContext
{
    public GpdDbContext(DbContextOptions<GpdDbContext> options) : base(options) { }

    public DbSet<DebtPosition> DebtPositions => Set<DebtPosition>();
    public DbSet<PaymentOption> PaymentOptions => Set<PaymentOption>();
    public DbSet<Installment> Installments => Set<Installment>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<Fdr> Fdrs => Set<Fdr>();
    public DbSet<FdrPayment> FdrPayments => Set<FdrPayment>();
    public DbSet<DigitalStamp> DigitalStamps => Set<DigitalStamp>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<DebtPosition>(e =>
        {
            // IUPD univoco per ente -> 409 su duplicato (vincolo GPD reale).
            e.HasIndex(x => new { x.OrganizationFiscalCode, x.Iupd }).IsUnique();
            e.HasMany(x => x.PaymentOptions).WithOne().HasForeignKey(x => x.DebtPositionId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<PaymentOption>()
            .HasMany(x => x.Installments).WithOne().HasForeignKey(x => x.PaymentOptionId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Installment>(e =>
        {
            e.HasIndex(x => x.Nav).IsUnique();
            e.HasMany(x => x.Transfers).WithOne().HasForeignKey(x => x.InstallmentId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.InstallmentMetadata).WithOne().HasForeignKey(x => x.InstallmentId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Transfer>()
            .HasMany(x => x.TransferMetadata).WithOne().HasForeignKey(x => x.TransferId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Receipt>().HasIndex(x => x.Iuv);
        b.Entity<Fdr>(e =>
        {
            e.HasIndex(x => new { x.PspId, x.Name }).IsUnique();
            e.HasMany(x => x.Payments).WithOne().HasForeignKey(x => x.FdrId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
