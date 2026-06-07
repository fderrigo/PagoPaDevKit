namespace PagoPaDevKit.Client;

// =====================================================================
// DTO client GPD v3. Ricalcano PaymentPositionModelV3 reale: stesso oggetto
// serializza identico verso mock e UAT. Gerarchia: posizione -> paymentOption
// (con debitore) -> installments (rate) -> transfer.
// =====================================================================

public class PaymentPositionRequest
{
    public string Iupd { get; set; } = "";
    public bool PayStandIn { get; set; } = true;
    public string CompanyName { get; set; } = "";
    public string? OfficeName { get; set; }
    public List<PaymentOptionDto> PaymentOption { get; set; } = new();
}

public class PaymentOptionDto
{
    public string Description { get; set; } = "";
    public DateTimeOffset? ValidityDate { get; set; }
    public DateTimeOffset? RetentionDate { get; set; }
    public bool SwitchToExpired { get; set; }
    public DebtorDto Debtor { get; set; } = new();
    public List<InstallmentDto> Installments { get; set; } = new();
}

public class DebtorDto
{
    public string? Type { get; set; }        // F / G
    public string FiscalCode { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? StreetName { get; set; }
    public string? CivicNumber { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? Province { get; set; }
    public string? Region { get; set; }
    public string? Country { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
}

public class InstallmentDto
{
    public string Iuv { get; set; } = "";
    public string? Nav { get; set; }
    public long Amount { get; set; }                 // centesimi
    public string Description { get; set; } = "";
    public DateTimeOffset DueDate { get; set; }
    public long Fee { get; set; }
    public long NotificationFee { get; set; }
    public string? Status { get; set; }
    public List<TransferDto> Transfer { get; set; } = new();
    // Metadata della rata (key/value), parte del modello v3.
    public List<MetadataKv> InstallmentMetadata { get; set; } = new();
}

public class TransferDto
{
    public string IdTransfer { get; set; } = "";
    public long Amount { get; set; }
    public string? OrganizationFiscalCode { get; set; }
    public string RemittanceInformation { get; set; } = "";
    public string Category { get; set; } = "";
    public string? Iban { get; set; }
    public string? PostalIban { get; set; }
    public StampDto? Stamp { get; set; }
    public string? CompanyName { get; set; }
    // Metadata di riconciliazione (key/value), propagati in paGetPayment/paSendRT.
    public List<MetadataKv> TransferMetadata { get; set; } = new();
}

public class MetadataKv { public string Key { get; set; } = ""; public string? Value { get; set; } }

public class StampDto
{
    public string HashDocument { get; set; } = "";
    public string StampType { get; set; } = "";
    public string ProvincialResidence { get; set; } = "";
}

public class PaymentPositionResponse
{
    public string Iupd { get; set; } = "";
    public string OrganizationFiscalCode { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset InsertedDate { get; set; }
    public DateTimeOffset? PublishDate { get; set; }
    public DateTimeOffset? PaymentDate { get; set; }
    public List<PaymentOptionDto> PaymentOption { get; set; } = new();

    // Comodità: NAV della prima rata.
    public string? FirstNav() => PaymentOption.FirstOrDefault()?.Installments.FirstOrDefault()?.Nav;
}

// Ricevuta letta da Receipts REST (parsata dalla RT XML).
public class ReceiptDto
{
    public string ReceiptId { get; set; } = "";
    public string Iuv { get; set; } = "";
    public string Nav { get; set; } = "";
    public decimal PaymentAmount { get; set; }   // euro
    public decimal Fee { get; set; }             // euro
    public string PspCompanyName { get; set; } = "";
    public string Outcome { get; set; } = "";
    public DateTimeOffset PaymentDateTime { get; set; }
}

// Riepilogo ricevuta (Receipts REST) per la riconciliazione.
public class ReceiptSummary
{
    public string Iuv { get; set; } = "";
    public string Nav { get; set; } = "";
    public string Debtor { get; set; } = "";
    public long PaymentAmount { get; set; }   // centesimi
    public long Fee { get; set; }
    public DateTimeOffset PaymentDateTime { get; set; }
    public string DeliveryStatus { get; set; } = "";
    public bool HasMetadata { get; set; }
}

public class PaymentsResultDto
{
    public int CurrentPageNumber { get; set; }
    public int TotalPages { get; set; }
    public List<ReceiptSummary> Results { get; set; } = new();
}

public class PagoPaApiException : Exception
{
    public int StatusCode { get; }
    public string? ResponseBody { get; }
    public PagoPaApiException(int statusCode, string? body, string message) : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = body;
    }
}
