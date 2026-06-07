using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;

namespace PagoPaDevKit.Mock.Gpd;

// API Receipts (gpd-payments). Recupero ricevute lato Ente Creditore.
// Path /payments/{ofc}/receipts[/{iuv}], fedele alla spec gpd-payments.
public static class ReceiptsEndpoints
{
    public static void MapReceipts(this WebApplication app)
    {
        var key = app.Configuration["Gpd:SubscriptionKey"] ?? "dev-gpd-key";
        var grp = app.MapGroup("/payments/{organizationFiscalCode}");

        // ---- GET lista ricevute (PaymentsResult) --------------------------------------
        grp.MapGet("/receipts", async (string organizationFiscalCode, HttpRequest http, GpdDbContext db,
            int? pageNum, int? pageSize, string? debtor, string? from, string? to, string? debtorOrIuv) =>
        {
            var auth = ApiGuards.CheckSubscriptionKey(http, key);
            if (auth is not null) return auth;

            int pn = pageNum ?? 0, ps = Math.Clamp(pageSize ?? 20, 1, 100);
            var q = db.Receipts.Where(x => x.OrganizationFiscalCode == organizationFiscalCode);
            if (!string.IsNullOrWhiteSpace(debtor)) q = q.Where(x => x.DebtorFiscalCode == debtor);
            if (!string.IsNullOrWhiteSpace(debtorOrIuv)) q = q.Where(x => x.DebtorFiscalCode == debtorOrIuv || x.Iuv == debtorOrIuv);
            if (DateTimeOffset.TryParse(from, out var f)) q = q.Where(x => x.PaymentDateTime >= f);
            if (DateTimeOffset.TryParse(to, out var t)) q = q.Where(x => x.PaymentDateTime <= t);

            var all = await q.ToListAsync();
            var ordered = all.OrderByDescending(x => x.PaymentDateTime).ToList(); // SQLite no DateTimeOffset ORDER BY
            int total = ordered.Count;
            var results = ordered.Skip(pn * ps).Take(ps).Select(r => new ReceiptListItem
            {
                OrganizationFiscalCode = r.OrganizationFiscalCode,
                Iuv = r.Iuv,
                Nav = r.Nav,
                Debtor = r.DebtorFiscalCode,
                PaymentAmount = r.PaymentAmount,
                Fee = r.Fee,
                PaymentDateTime = r.PaymentDateTime,
                DeliveryStatus = r.DeliveryStatus,
                HasMetadata = !string.IsNullOrEmpty(r.MetadataJson)
            }).ToList();

            return Results.Ok(new PaymentsResult
            {
                CurrentPageNumber = pn,
                Length = results.Count,
                TotalPages = (int)Math.Ceiling(total / (double)ps),
                Results = results
            });
        });

        // ---- GET ricevuta singola per IUV (RT in XML) ---------------------------------
        grp.MapGet("/receipts/{iuv}", async (string organizationFiscalCode, string iuv, HttpRequest http, GpdDbContext db) =>
        {
            var auth = ApiGuards.CheckSubscriptionKey(http, key);
            if (auth is not null) return auth;

            var r = await db.Receipts.FirstOrDefaultAsync(x => x.OrganizationFiscalCode == organizationFiscalCode && x.Iuv == iuv);
            if (r is null) return ApiGuards.Error(GpdErrors.ReceiptNotFound, organizationFiscalCode, iuv);

            // La RT reale è un documento XML: qui una rappresentazione fedele dei campi ricevuta.
            XNamespace ns = "http://pagopa-api.pagopa.gov.it/pa/paForNode.xsd";
            var doc = new XElement(ns + "receipt",
                new XElement(ns + "receiptId", r.ReceiptId),
                new XElement(ns + "noticeNumber", r.Nav),
                new XElement(ns + "fiscalCode", r.OrganizationFiscalCode),
                new XElement(ns + "outcome", r.Outcome),
                new XElement(ns + "creditorReferenceId", r.Iuv),
                new XElement(ns + "paymentAmount", (r.PaymentAmount / 100m).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new XElement(ns + "fee", (r.Fee / 100m).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new XElement(ns + "debtor", r.DebtorFiscalCode),
                new XElement(ns + "PSPCompanyName", r.PspCompanyName),
                new XElement(ns + "idPSP", r.IdPsp ?? ""),
                new XElement(ns + "idChannel", r.IdChannel ?? ""),
                new XElement(ns + "paymentMethod", r.PaymentMethod ?? ""),
                new XElement(ns + "paymentDateTime", r.PaymentDateTime.UtcDateTime.ToString("o")),
                MetadataElement(ns, r.MetadataJson));
            var xml = new XDeclaration("1.0", "utf-8", null) + doc.ToString();
            return Results.Content(xml, "application/xml", Encoding.UTF8);
        });
    }

    // <metadata><mapEntry><key/><value/></mapEntry>…</metadata> dal JSON salvato sulla RT.
    static XElement? MetadataElement(System.Xml.Linq.XNamespace ns, string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(metadataJson);
            var meta = new XElement(ns + "metadata");
            foreach (var e in doc.RootElement.EnumerateArray())
                meta.Add(new XElement(ns + "mapEntry",
                    new XElement(ns + "key", e.GetProperty("Key").GetString() ?? ""),
                    new XElement(ns + "value", e.GetProperty("Value").GetString() ?? "")));
            return meta;
        }
        catch { return null; }
    }
}
