namespace PagoPaDevKit.Mock.Gpd;

// ProblemJson come GPD reale: { title, status, detail } (RFC7807 ridotto).
public class ProblemJson
{
    public string? Title { get; set; }
    public int Status { get; set; }
    public string? Detail { get; set; }
}

// Voce del catalogo errori (titolo + formato detail + status), come AppError di pagopa-debt-position.
public record AppError(int Status, string Title, string DetailFormat)
{
    public ProblemJson To(params object[] args) =>
        new() { Title = Title, Status = Status, Detail = args.Length == 0 ? DetailFormat : string.Format(DetailFormat, args) };
}

// Catalogo errori GPD: titoli e diciture IDENTICI a pagopa-debt-position (AppError.java).
public static class GpdErrors
{
    public static readonly AppError RequestDataError =
        new(400, "Error in the debt position request data", "{0}");
    public static readonly AppError UniqueViolation =
        new(409, "The debt position violated constraints of uniqueness",
            "Already exists a debt position for the Organization Fiscal Code {0} or one of its payment options is not unique");
    public static readonly AppError NotFound =
        new(404, "Not found the debt position", "Not found a debt position for Organization Fiscal Code {0} and IUPD {1}");
    public static readonly AppError NotUpdatable =
        new(409, "Existing related payment found or not in updatable state",
            "A payment transaction has already been carried out or, the debt position with Organization Fiscal Code {0} and IUPD {1}, is not in updatable state");
    public static readonly AppError NotPublishable =
        new(409, "Existing related payment found or not in publishable state",
            "A payment transaction has already been carried out or, the debt position with Organization Fiscal Code {0} and IUPD {1}, is not in publishable state");
    public static readonly AppError NotInvalidable =
        new(409, "Existing related payment found or not in invalidable state",
            "A payment transaction has already been carried out or, the debt position with Organization Fiscal Code {0} and IUPD {1}, is not in invalidable state");
    public static readonly AppError PaymentOptionNotFound =
        new(404, "Not found the payment option", "Not found a payment option for Organization Fiscal Code {0} and NAV {1}");
    public static readonly AppError PaymentOptionNotPayable =
        new(422, "Not in payable state", "The payment option with Organization Fiscal Code {0} and NAV {1} is not in payable state");
    public static readonly AppError PaymentOptionAlreadyPaid =
        new(409, "Existing related payment found", "A payment transaction has already been carried out for the Organization Fiscal Code {0} and NAV {1}");

    // Receipt non trovata (gpd-payments).
    public static readonly AppError ReceiptNotFound =
        new(404, "Receipt not found", "Receipt not found for Organization Fiscal Code {0} and IUV {1}");
    public static readonly AppError Generic =
        new(500, "Internal server error", "{0}");
}
