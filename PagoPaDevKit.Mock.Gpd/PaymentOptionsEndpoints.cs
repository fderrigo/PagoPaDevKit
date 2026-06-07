using Microsoft.EntityFrameworkCore;

namespace PagoPaDevKit.Mock.Gpd;

// GET /payment-options/organizations/{ofc}/notices/{nav}: opzioni di pagamento disponibili
// per un avviso (unica soluzione / piano rateale / parziale / alternative). Il cittadino sceglie.
public static class PaymentOptionsEndpoints
{
    public static void MapPaymentOptions(this WebApplication app)
    {
        var key = app.Configuration["Gpd:SubscriptionKey"] ?? "dev-gpd-key";

        app.MapGet("/payment-options/organizations/{organizationFiscalCode}/notices/{nav}",
            async (string organizationFiscalCode, string nav, HttpRequest http, GpdDbContext db) =>
        {
            var auth = ApiGuards.CheckSubscriptionKey(http, key);
            if (auth is not null) return auth;

            var d = await GpdEndpoints.FullGraph(db).FirstOrDefaultAsync(x =>
                x.OrganizationFiscalCode == organizationFiscalCode &&
                x.PaymentOptions.Any(po => po.Installments.Any(i => i.Nav == nav)));
            if (d is null) return ApiGuards.Error(GpdErrors.PaymentOptionNotFound, organizationFiscalCode, nav);
            GpdEndpoints.RefreshExpiry(d);

            return Results.Ok(new PaymentOptionsResponse
            {
                OrganizationFiscalCode = d.OrganizationFiscalCode,
                Iupd = d.Iupd,
                Status = d.Status,
                PaymentOption = d.PaymentOptions.Select(po => new PaymentOptionView
                {
                    Description = po.Description,
                    NumberOfInstallments = po.Installments.Count,
                    // Piano rateale se >1 rata; unica soluzione se 1.
                    OptionType = po.Installments.Count > 1 ? "INSTALLMENTS" : "SINGLE_SOLUTION",
                    TotalAmount = po.Installments.Sum(i => i.Amount),
                    Installments = po.Installments.Select(i => new InstallmentView
                    {
                        Nav = i.Nav, Iuv = i.Iuv, Amount = i.Amount, DueDate = i.DueDate,
                        Status = i.Status, Description = i.Description
                    }).ToList()
                }).ToList()
            });
        });
    }
}
