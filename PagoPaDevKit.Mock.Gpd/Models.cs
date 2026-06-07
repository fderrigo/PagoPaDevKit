using System.Text.Json.Serialization;

namespace PagoPaDevKit.Mock.Gpd;

// =====================================================================
// Modello dati GPD v3 (PaymentPositionModelV3). Gerarchia reale:
//   DebtPosition (posizione)
//     └─ PaymentOption (opzione di pagamento, contiene il DEBITORE)
//          └─ Installment (rata: nav/iuv/importo, l'unità pagabile)
//               └─ Transfer (quota di ripartizione: iban OPPURE marca da bollo)
// In v3 il debitore è dentro la paymentOption e l'avviso (NAV) è sulla rata.
// =====================================================================

public static class DebtPositionStatus
{
    public const string Draft = "DRAFT";
    public const string Published = "PUBLISHED";
    public const string Valid = "VALID";
    public const string Unpayable = "UNPAYABLE";          // sostituisce EXPIRED/INVALID di v1
    public const string PartiallyPaid = "PARTIALLY_PAID";
    public const string Paid = "PAID";
}

public static class InstallmentStatus
{
    public const string Unpaid = "UNPAID";
    public const string Paid = "PAID";
    public const string PartiallyReported = "PARTIALLY_REPORTED";
    public const string Reported = "REPORTED";
    public const string Unpayable = "UNPAYABLE";
    public const string Expired = "EXPIRED";
}

// ---------------------- Entità EF ----------------------

public class DebtPosition
{
    public int Id { get; set; }
    public string Iupd { get; set; } = "";
    public string OrganizationFiscalCode { get; set; } = "";
    public bool PayStandIn { get; set; } = true;
    public string CompanyName { get; set; } = "";
    public string? OfficeName { get; set; }
    public string Status { get; set; } = DebtPositionStatus.Draft;
    // Origine: AVVISO (posizione emessa dall'ente) o SPONTANEO (generata on-demand dal cittadino).
    public string Origin { get; set; } = PaymentOrigin.Avviso;
    public DateTimeOffset InsertedDate { get; set; }
    public DateTimeOffset? PublishDate { get; set; }
    public DateTimeOffset? PaymentDate { get; set; }
    public List<PaymentOption> PaymentOptions { get; set; } = new();
}

public static class PaymentOrigin
{
    public const string Avviso = "AVVISO";       // pagamento a seguito di avviso rilasciato
    public const string Spontaneo = "SPONTANEO"; // pagamento spontaneo su dovuto
}

public class PaymentOption
{
    public int Id { get; set; }
    public int DebtPositionId { get; set; }
    public string Description { get; set; } = "";
    public DateTimeOffset? ValidityDate { get; set; }
    public DateTimeOffset? RetentionDate { get; set; }
    public bool SwitchToExpired { get; set; }
    // Debitore (in v3 vive nella payment option).
    public string DebtorType { get; set; } = "F";        // F / G
    public string DebtorFiscalCode { get; set; } = "";
    public string DebtorFullName { get; set; } = "";
    public string? StreetName { get; set; }
    public string? CivicNumber { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string? Province { get; set; }
    public string? Region { get; set; }
    public string? Country { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public List<Installment> Installments { get; set; } = new();
}

public class Installment
{
    public int Id { get; set; }
    public int PaymentOptionId { get; set; }
    public string Iuv { get; set; } = "";
    public string Nav { get; set; } = "";
    public long Amount { get; set; }
    public string Description { get; set; } = "";
    public DateTimeOffset DueDate { get; set; }
    public long Fee { get; set; }
    public long NotificationFee { get; set; }
    public string Status { get; set; } = InstallmentStatus.Unpaid;
    public List<Transfer> Transfers { get; set; } = new();
    public List<InstallmentMetadata> InstallmentMetadata { get; set; } = new();
}

public class Transfer
{
    public int Id { get; set; }
    public int InstallmentId { get; set; }
    public string IdTransfer { get; set; } = "";       // "1".."5"
    public long Amount { get; set; }
    public string? OrganizationFiscalCode { get; set; }
    public string RemittanceInformation { get; set; } = "";
    public string Category { get; set; } = "";
    public string? Iban { get; set; }
    public string? PostalIban { get; set; }
    public string? StampHashDocument { get; set; }
    public string? StampType { get; set; }
    public string? StampProvincialResidence { get; set; }
    public string? CompanyName { get; set; }
    public List<TransferMetadata> TransferMetadata { get; set; } = new();
}

// Ricevuta (RT) prodotta da paSendRT. Letta poi dalla Receipts REST (gpd-payments).
public class Receipt
{
    public int Id { get; set; }
    public string ReceiptId { get; set; } = "";
    public string Iuv { get; set; } = "";
    public string Nav { get; set; } = "";
    public string OrganizationFiscalCode { get; set; } = "";
    public string DebtorFiscalCode { get; set; } = "";
    public long PaymentAmount { get; set; }
    public long Fee { get; set; }
    public string Outcome { get; set; } = "OK";
    public string PspCompanyName { get; set; } = "";
    public string? IdPsp { get; set; }
    public string? IdChannel { get; set; }
    public string? PaymentMethod { get; set; }
    public DateTimeOffset PaymentDateTime { get; set; }
    // Metadata di riconciliazione (JSON dei mapEntry dei transfer) ricevuti nella RT.
    public string? MetadataJson { get; set; }
    // Consegna RT alla stazione broadcast dell'EC (modello async, opzione consigliata).
    public string DeliveryStatus { get; set; } = ReceiptDeliveryStatus.NotRequired;
    public int DeliveryAttempts { get; set; }
}

// Marca da bollo digitale (E-bollo 2.0) emessa al pagamento di un transfer "stamp".
public class DigitalStamp
{
    public int Id { get; set; }
    public string OrganizationFiscalCode { get; set; } = "";
    public string Iuv { get; set; } = "";
    public string TipoBollo { get; set; } = "";          // es. "01" imposta di bollo
    public string HashDocumento { get; set; } = "";      // hash del documento (base64)
    public string ProvinciaResidenza { get; set; } = ""; // [A-Z]{2}
    public string IdentificativoMarca { get; set; } = ""; // identificativo univoco MBD
    public string MarcaBase64 { get; set; } = "";        // marca (placeholder firmato)
    public DateTimeOffset IssuedAt { get; set; }
}

public static class ReceiptDeliveryStatus
{
    public const string NotRequired = "NOT_REQUIRED"; // nessuna stazione configurata -> pull
    public const string Delivered = "DELIVERED";       // paSendRT consegnata alla stazione
    public const string NoticePending = "NOTICE_PENDING"; // consegna fallita, in coda di retry
}

public class InstallmentMetadata { public int Id { get; set; } public int InstallmentId { get; set; } public string Key { get; set; } = ""; public string? Value { get; set; } }
public class TransferMetadata { public int Id { get; set; } public int TransferId { get; set; } public string Key { get; set; } = ""; public string? Value { get; set; } }

// Risposta lista ricevute (gpd-payments PaymentsResult).
public class PaymentsResult
{
    public int CurrentPageNumber { get; set; }
    public int Length { get; set; }
    public int TotalPages { get; set; }
    public List<ReceiptListItem> Results { get; set; } = new();
}

public class ReceiptListItem
{
    public string OrganizationFiscalCode { get; set; } = "";
    public string Iuv { get; set; } = "";
    public string Nav { get; set; } = "";
    public string Debtor { get; set; } = "";
    public long PaymentAmount { get; set; }
    public long Fee { get; set; }
    public DateTimeOffset PaymentDateTime { get; set; }
    public string DeliveryStatus { get; set; } = "";
    public bool HasMetadata { get; set; }
}

// Opzioni di pagamento per un avviso (GET /payment-options/.../notices/{nav}).
public class PaymentOptionsResponse
{
    public string OrganizationFiscalCode { get; set; } = "";
    public string Iupd { get; set; } = "";
    public string Status { get; set; } = "";
    public List<PaymentOptionView> PaymentOption { get; set; } = new();
}

public class PaymentOptionView
{
    public string Description { get; set; } = "";
    public string OptionType { get; set; } = "";   // SINGLE_SOLUTION / INSTALLMENTS
    public int NumberOfInstallments { get; set; }
    public long TotalAmount { get; set; }
    public List<InstallmentView> Installments { get; set; } = new();
}

public class InstallmentView
{
    public string Nav { get; set; } = "";
    public string Iuv { get; set; } = "";
    public long Amount { get; set; }
    public DateTimeOffset DueDate { get; set; }
    public string Status { get; set; } = "";
    public string Description { get; set; } = "";
}

// ---------------------- DTO v3 (request/response, camelCase) ----------------------

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
    public long Amount { get; set; }
    public string Description { get; set; } = "";
    public DateTimeOffset DueDate { get; set; }
    public long Fee { get; set; }
    public long NotificationFee { get; set; }
    public string? Status { get; set; }
    public List<TransferDto> Transfer { get; set; } = new();
    public List<MetadataDto> InstallmentMetadata { get; set; } = new();
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
    public List<MetadataDto> TransferMetadata { get; set; } = new();
}

public class StampDto
{
    public string HashDocument { get; set; } = "";
    public string StampType { get; set; } = "";
    public string ProvincialResidence { get; set; } = "";
}

public class MetadataDto { public string Key { get; set; } = ""; public string? Value { get; set; } }

// ---------------------- Response ----------------------

public class PaymentPositionResponse
{
    public string Iupd { get; set; } = "";
    public string OrganizationFiscalCode { get; set; } = "";
    public bool PayStandIn { get; set; }
    public string CompanyName { get; set; } = "";
    public string? OfficeName { get; set; }
    public string Status { get; set; } = "";
    public string Origin { get; set; } = "";
    public DateTimeOffset InsertedDate { get; set; }
    public DateTimeOffset? PublishDate { get; set; }
    public DateTimeOffset? PaymentDate { get; set; }
    public List<PaymentOptionDto> PaymentOption { get; set; } = new();

    public static PaymentPositionResponse From(DebtPosition d) => new()
    {
        Iupd = d.Iupd,
        OrganizationFiscalCode = d.OrganizationFiscalCode,
        PayStandIn = d.PayStandIn,
        CompanyName = d.CompanyName,
        OfficeName = d.OfficeName,
        Status = d.Status,
        Origin = d.Origin,
        InsertedDate = d.InsertedDate,
        PublishDate = d.PublishDate,
        PaymentDate = d.PaymentDate,
        PaymentOption = d.PaymentOptions.Select(po => new PaymentOptionDto
        {
            Description = po.Description,
            ValidityDate = po.ValidityDate,
            RetentionDate = po.RetentionDate,
            SwitchToExpired = po.SwitchToExpired,
            Debtor = new DebtorDto
            {
                Type = po.DebtorType, FiscalCode = po.DebtorFiscalCode, FullName = po.DebtorFullName,
                StreetName = po.StreetName, CivicNumber = po.CivicNumber, PostalCode = po.PostalCode,
                City = po.City, Province = po.Province, Region = po.Region, Country = po.Country,
                Email = po.Email, Phone = po.Phone
            },
            Installments = po.Installments.Select(i => new InstallmentDto
            {
                Iuv = i.Iuv, Nav = i.Nav, Amount = i.Amount, Description = i.Description, DueDate = i.DueDate,
                Fee = i.Fee, NotificationFee = i.NotificationFee, Status = i.Status,
                Transfer = i.Transfers.Select(t => new TransferDto
                {
                    IdTransfer = t.IdTransfer, Amount = t.Amount, OrganizationFiscalCode = t.OrganizationFiscalCode,
                    RemittanceInformation = t.RemittanceInformation, Category = t.Category,
                    Iban = t.Iban, PostalIban = t.PostalIban, CompanyName = t.CompanyName,
                    Stamp = t.StampHashDocument is null ? null : new StampDto
                    {
                        HashDocument = t.StampHashDocument, StampType = t.StampType ?? "",
                        ProvincialResidence = t.StampProvincialResidence ?? ""
                    },
                    TransferMetadata = t.TransferMetadata.Select(m => new MetadataDto { Key = m.Key, Value = m.Value }).ToList()
                }).ToList(),
                InstallmentMetadata = i.InstallmentMetadata.Select(m => new MetadataDto { Key = m.Key, Value = m.Value }).ToList()
            }).ToList()
        }).ToList()
    };
}

// Lista paginata (shape GPD reale).
public class PaymentPositionListResponse
{
    [JsonPropertyName("payment_position_list")]
    public List<PaymentPositionResponse> PaymentPositionList { get; set; } = new();
    [JsonPropertyName("page_info")]
    public PageInfo PageInfo { get; set; } = new();
}

public class PageInfo
{
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("limit")] public int Limit { get; set; }
    [JsonPropertyName("items_found")] public int ItemsFound { get; set; }
    [JsonPropertyName("total_pages")] public int TotalPages { get; set; }
    [JsonPropertyName("total_items")] public long TotalItems { get; set; }
}
