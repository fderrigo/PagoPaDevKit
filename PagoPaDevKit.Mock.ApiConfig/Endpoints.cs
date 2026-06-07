using Microsoft.EntityFrameworkCore;

namespace PagoPaDevKit.Mock.ApiConfig;

public record ApimError(int StatusCode, string Message);

// ProblemJson { title, status, detail } come apiconfig.
public class ProblemJson { public string? Title { get; set; } public int Status { get; set; } public string? Detail { get; set; } }

// Catalogo errori IBAN: titoli e diciture IDENTICI a pagopa-api-config (AppError.java).
public static class ApiConfigErrors
{
    public static IResult IbanAlreadyExist(string iban) => Pj(409, "IBAN already exist", $"The IBAN with code {iban} already exists");
    public static IResult IbanNotValid(string iban) => Pj(422, "IBAN not valid", $"The IBAN value {iban} is not valid");
    public static IResult IbanNotFound(string iban) => Pj(404, "IBAN value not found", $"The IBAN with value {iban} is not present");
    static IResult Pj(int s, string t, string d) => Results.Json(new ProblemJson { Title = t, Status = s, Detail = d }, statusCode: s);
}

public static class Endpoints
{
    public static void MapApiConfig(this WebApplication app)
    {
        var key = app.Configuration["ApiConfig:SubscriptionKey"] ?? "dev-gpd-key";

        IResult? Auth(HttpRequest req)
        {
            if (!req.Headers.TryGetValue("Ocp-Apim-Subscription-Key", out var k) || string.IsNullOrWhiteSpace(k))
                return Results.Json(new ApimError(401, "Access denied due to missing subscription key. Make sure to include subscription key when making requests to an API."), statusCode: 401);
            if (k != key)
                return Results.Json(new ApimError(401, "Access denied due to invalid subscription key. Make sure to provide a valid key for an active subscription."), statusCode: 401);
            return null;
        }
        var ci = app.MapGroup("/creditorinstitutions/{creditorInstitutionCode}");

        // GET tutti gli IBAN dell'ente.
        ci.MapGet("/ibans", async (string creditorInstitutionCode, HttpRequest http, ApiConfigDbContext db) =>
        {
            if (Auth(http) is { } a) return a;
            var list = await db.Ibans.Include(x => x.Labels).Where(x => x.CreditorInstitutionCode == creditorInstitutionCode).ToListAsync();
            var items = list.Select(IbanEnhanced.From).ToList();
            return Results.Ok(new IbansEnhanced
            {
                IbansEnhancedList = items,
                PageInfo = new ApiConfigPageInfo { Page = 0, Limit = items.Count, ItemsFound = items.Count, TotalItems = items.Count, TotalPages = 1 }
            });
        });

        // GET lista IBAN paginata.
        ci.MapGet("/ibans/list", async (string creditorInstitutionCode, HttpRequest http, ApiConfigDbContext db, int? page, int? limit) =>
        {
            if (Auth(http) is { } a) return a;
            int p = page ?? 0, lim = Math.Clamp(limit ?? 50, 1, 100);
            var all = await db.Ibans.Include(x => x.Labels).Where(x => x.CreditorInstitutionCode == creditorInstitutionCode).ToListAsync();
            var pageItems = all.Skip(p * lim).Take(lim).Select(IbanEnhanced.From).ToList();
            return Results.Ok(new IbansEnhanced
            {
                IbansEnhancedList = pageItems,
                PageInfo = new ApiConfigPageInfo { Page = p, Limit = lim, ItemsFound = pageItems.Count, TotalItems = all.Count, TotalPages = (int)Math.Ceiling(all.Count / (double)lim) }
            });
        });

        // POST crea IBAN.
        ci.MapPost("/ibans", async (string creditorInstitutionCode, IbanEnhanced body, HttpRequest http, ApiConfigDbContext db) =>
        {
            if (Auth(http) is { } a) return a;
            if (string.IsNullOrWhiteSpace(body.Iban) || body.Iban.Length is < 15 or > 35)
                return ApiConfigErrors.IbanNotValid(body.Iban ?? "");
            if (await db.Ibans.AnyAsync(x => x.CreditorInstitutionCode == creditorInstitutionCode && x.Iban == body.Iban))
                return ApiConfigErrors.IbanAlreadyExist(body.Iban);
            var now = DateTimeOffset.UtcNow;
            var e = new IbanEntity
            {
                CreditorInstitutionCode = creditorInstitutionCode, Iban = body.Iban,
                CompanyName = body.CompanyName, Description = body.Description, IsActive = body.IsActive,
                ValidityDate = body.ValidityDate == default ? now : body.ValidityDate,
                PublicationDate = now, DueDate = body.DueDate,
                Labels = body.Labels.Select(l => new IbanLabelEntity { Name = l.Name, Description = l.Description }).ToList()
            };
            db.Ibans.Add(e);
            await db.SaveChangesAsync();
            return Results.Created($"/creditorinstitutions/{creditorInstitutionCode}/ibans", IbanEnhanced.From(e));
        });

        // PUT aggiorna IBAN.
        ci.MapPut("/ibans/{ibanValue}", async (string creditorInstitutionCode, string ibanValue, IbanEnhanced body, HttpRequest http, ApiConfigDbContext db) =>
        {
            if (Auth(http) is { } a) return a;
            var e = await db.Ibans.Include(x => x.Labels).FirstOrDefaultAsync(x => x.CreditorInstitutionCode == creditorInstitutionCode && x.Iban == ibanValue);
            if (e is null) return ApiConfigErrors.IbanNotFound(ibanValue);
            e.CompanyName = body.CompanyName; e.Description = body.Description; e.IsActive = body.IsActive;
            if (body.ValidityDate != default) e.ValidityDate = body.ValidityDate;
            e.DueDate = body.DueDate;
            db.RemoveRange(e.Labels);
            e.Labels = body.Labels.Select(l => new IbanLabelEntity { Name = l.Name, Description = l.Description }).ToList();
            await db.SaveChangesAsync();
            return Results.Ok(IbanEnhanced.From(e));
        });

        // DELETE IBAN.
        ci.MapDelete("/ibans/{ibanValue}", async (string creditorInstitutionCode, string ibanValue, HttpRequest http, ApiConfigDbContext db) =>
        {
            if (Auth(http) is { } a) return a;
            var e = await db.Ibans.FirstOrDefaultAsync(x => x.CreditorInstitutionCode == creditorInstitutionCode && x.Iban == ibanValue);
            if (e is null) return ApiConfigErrors.IbanNotFound(ibanValue);
            db.Ibans.Remove(e);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        // GET tassonomia ufficiale (tipi dovuto). Filtri: onlyValid, q (ricerca), tipoEnte, limit.
        app.MapGet("/taxonomies", (HttpRequest http, TaxonomyCatalog catalog, bool? onlyValid, string? q, string? tipoEnte, int? limit) =>
        {
            if (Auth(http) is { } a) return a;
            var now = DateTimeOffset.UtcNow;
            IEnumerable<TaxonomyEntry> entries = catalog.Entries;
            if (onlyValid != false) entries = entries.Where(e => e.DataFineValidita is null || e.DataFineValidita > now);
            if (!string.IsNullOrWhiteSpace(tipoEnte)) entries = entries.Where(e => e.CodiceTipoEnte == tipoEnte);
            if (!string.IsNullOrWhiteSpace(q))
                entries = entries.Where(e => e.DenominazioneServizio.Contains(q, StringComparison.OrdinalIgnoreCase)
                                          || e.CodiceTassonomico.Contains(q, StringComparison.OrdinalIgnoreCase)
                                          || e.MacroArea.Contains(q, StringComparison.OrdinalIgnoreCase));
            // limit di default per evitare risposte enormi (la tassonomia ufficiale ha migliaia di voci).
            int lim = limit is > 0 ? limit.Value : 500;
            return Results.Ok(entries.Take(lim).Select(e => new
            {
                e.CodiceTassonomico, e.TipoEnte, e.MacroArea, e.DenominazioneServizio,
                e.DescrizioneMotivoRiscossione, e.MotivoRiscossione, e.Versione,
                e.CodiceTipoEnte, e.ProgressivoMacroArea, e.CodiceTipoServizio
            }).ToList());
        });
    }
}
