using Microsoft.EntityFrameworkCore;

namespace PagoPaDevKit.Mock.Gpd;

// Endpoint DEV-ONLY (fuori dal contratto pagoPA): reset dei dati di un ente per ripartire da zero
// in locale. NON fa parte delle API ufficiali; serve solo al devkit. Protetto da subscription key.
public static class DevEndpoints
{
    public static void MapDev(this WebApplication app)
    {
        var key = app.Configuration["Gpd:SubscriptionKey"] ?? "dev-gpd-key";

        // Azzera posizioni debitorie + ricevute + marche da bollo + FdR dell'ente.
        // La tassonomia e gli IBAN vivono su ApiConfig (altro servizio) e NON vengono toccati.
        app.MapPost("/dev/reset/{organizationFiscalCode}", async (string organizationFiscalCode, HttpRequest http, GpdDbContext db) =>
        {
            var auth = ApiGuards.CheckSubscriptionKey(http, key);
            if (auth is not null) return auth;

            var positions = await GpdEndpoints.FullGraph(db)
                .Where(x => x.OrganizationFiscalCode == organizationFiscalCode).ToListAsync();
            db.DebtPositions.RemoveRange(positions);

            var receipts = await db.Receipts.Where(x => x.OrganizationFiscalCode == organizationFiscalCode).ToListAsync();
            db.Receipts.RemoveRange(receipts);

            var stamps = await db.DigitalStamps.Where(x => x.OrganizationFiscalCode == organizationFiscalCode).ToListAsync();
            db.DigitalStamps.RemoveRange(stamps);

            var fdrs = await db.Fdrs.Include(f => f.Payments).Where(x => x.OrganizationId == organizationFiscalCode).ToListAsync();
            db.Fdrs.RemoveRange(fdrs);

            await db.SaveChangesAsync();
            return Results.Ok(new
            {
                positions = positions.Count,
                receipts = receipts.Count,
                stamps = stamps.Count,
                fdrs = fdrs.Count
            });
        });
    }
}
