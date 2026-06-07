using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace PagoPaDevKit.Mock.Gpd;

// Corpo 401 identico a quello restituito da Azure APIM davanti alle API GPD reali.
public record ApimError(int StatusCode, string Message);

public static class ApiGuards
{
    public const string SubKeyHeader = "Ocp-Apim-Subscription-Key";

    // Verifica subscription key. In produzione la chiave la rilascia il BackOffice pagoPA.
    // APIM risponde 401 con un body specifico, diverso dai ProblemDetails delle API a valle.
    public static IResult? CheckSubscriptionKey(HttpRequest req, string expectedKey)
    {
        if (!req.Headers.TryGetValue(SubKeyHeader, out var provided) || string.IsNullOrWhiteSpace(provided))
        {
            return Results.Json(new ApimError(401,
                "Access denied due to missing subscription key. Make sure to include subscription key when making requests to an API."),
                statusCode: 401);
        }
        if (provided != expectedKey)
        {
            return Results.Json(new ApimError(401,
                "Access denied due to invalid subscription key. Make sure to provide a valid key for an active subscription."),
                statusCode: 401);
        }
        return null;
    }

    // Errore ProblemJson { title, status, detail } come GPD reale.
    public static IResult Problem(int status, string title, string detail) =>
        Results.Json(new ProblemJson { Title = title, Status = status, Detail = detail }, statusCode: status);

    // Errore dal catalogo ufficiale (titolo + dicitura identici a pagopa-debt-position).
    public static IResult Error(AppError err, params object[] args)
    {
        var pj = err.To(args);
        return Results.Json(pj, statusCode: pj.Status);
    }
}

// Regole di validazione fedeli a GPD v3.
public static class GpdRules
{
    // IUV: 17 cifre. NAV: aux digit "3" + IUV (totale 18 cifre).
    public static bool IsValidIuv(string iuv) => iuv.Length == 17 && iuv.All(char.IsDigit);

    public static string NavFromIuv(string iuv) => "3" + iuv;

    public static bool IsValidNav(string nav) =>
        nav.Length == 18 && nav[0] == '3' && nav.All(char.IsDigit);

    // L'importo della rata deve coincidere con la somma dei transfer (regola GPD reale).
    public static bool AmountMatchesTransfers(InstallmentDto i) =>
        i.Transfer.Sum(t => t.Amount) == i.Amount;

    // idTransfer ammessi: "1".."5" (enum GPD reale).
    public static bool IsValidIdTransfer(string id) => id is "1" or "2" or "3" or "4" or "5";

    // Un transfer accredita su IBAN oppure è una marca da bollo (stamp): mutuamente esclusivi.
    public static bool HasValidAccredito(TransferDto t)
    {
        bool hasIban = !string.IsNullOrWhiteSpace(t.Iban);
        bool hasStamp = t.Stamp is not null && !string.IsNullOrWhiteSpace(t.Stamp.HashDocument);
        return hasIban ^ hasStamp; // esattamente uno dei due
    }

    public static bool IsValidType(string? type) => type is "F" or "G";
}
