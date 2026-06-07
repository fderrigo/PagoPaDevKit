using Microsoft.EntityFrameworkCore;

namespace PagoPaDevKit.Mock.Gpd;

// E-bollo 2.0: recupero delle marche da bollo digitali (MBD) emesse al pagamento.
public static class EbolloEndpoints
{
    public static void MapEbollo(this WebApplication app)
    {
        var key = app.Configuration["Gpd:SubscriptionKey"] ?? "dev-gpd-key";

        app.MapGet("/e-bollo/organizations/{organizationFiscalCode}/stamps/{iuv}",
            async (string organizationFiscalCode, string iuv, HttpRequest http, GpdDbContext db) =>
        {
            var auth = ApiGuards.CheckSubscriptionKey(http, key);
            if (auth is not null) return auth;

            var stamps = await db.DigitalStamps
                .Where(x => x.OrganizationFiscalCode == organizationFiscalCode && x.Iuv == iuv)
                .ToListAsync();
            if (stamps.Count == 0)
                return ApiGuards.Problem(404, "Not Found", $"Nessuna marca da bollo per IUV {iuv}.");

            return Results.Ok(stamps.Select(s => new
            {
                iuv = s.Iuv,
                identificativoMarca = s.IdentificativoMarca,
                tipoBollo = s.TipoBollo,
                provinciaResidenza = s.ProvinciaResidenza,
                hashDocumento = s.HashDocumento,
                marcaBase64 = s.MarcaBase64,
                issuedAt = s.IssuedAt
            }).ToList());
        });
    }
}
