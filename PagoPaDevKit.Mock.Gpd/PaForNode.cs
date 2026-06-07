using System.Runtime.Serialization;
using System.ServiceModel;

namespace PagoPaDevKit.Mock.Gpd;

// =====================================================================
// Contratti SOAP paForNode (primitive EC chiamate dal Nodo dei Pagamenti).
// Modellati su pagopa-api/wsdl/xsd/paForNode.xsd. Namespace fedele.
// Flusso: paVerifyPaymentNotice (verifica) -> paGetPaymentV2 (attivazione)
//         -> paSendRTV2 (invio ricevuta = pagamento concluso).
// =====================================================================

public static class PaForNodeNs
{
    public const string Xsd = "http://pagopa-api.pagopa.gov.it/pa/paForNode.xsd";
}

// Esito generale (ctResponse): OK o KO.
[DataContract(Namespace = PaForNodeNs.Xsd)]
public enum StOutcome { [EnumMember] OK, [EnumMember] KO }

// Fault con i codici PAA_* reali.
// Nomi elemento camelCase come il WSDL pagoPA reale (faultCode, faultString, id, description).
[DataContract(Namespace = PaForNodeNs.Xsd)]
public class CtFaultBean
{
    [DataMember(Order = 0, Name = "faultCode")] public string FaultCode { get; set; } = "";
    [DataMember(Order = 1, Name = "faultString")] public string FaultString { get; set; } = "";
    [DataMember(Order = 2, Name = "id")] public string Id { get; set; } = "";
    [DataMember(Order = 3, Name = "description", EmitDefaultValue = false)] public string? Description { get; set; }
    [DataMember(Order = 4, Name = "serial", EmitDefaultValue = false)] public string? Serial { get; set; }
}

// Catalogo fault PAA: faultString e description IDENTICI a pagopa-gpd-payments (PaaErrorEnum).
public static class PaaFault
{
    public const string Sconosciuto = "PAA_PAGAMENTO_SCONOSCIUTO";
    public const string Duplicato = "PAA_PAGAMENTO_DUPLICATO";
    public const string Scaduto = "PAA_PAGAMENTO_SCADUTO";
    public const string Annullato = "PAA_PAGAMENTO_ANNULLATO";
    public const string ReceiptDuplicata = "PAA_RECEIPT_DUPLICATA";
    public const string SintassiXsd = "PAA_SINTASSI_XSD";
    public const string Semantica = "PAA_SEMANTICA";
    public const string SystemError = "PAA_SYSTEM_ERROR";

    static readonly Dictionary<string, (string faultString, string description)> Catalog = new()
    {
        [Sconosciuto] = ("pagamento sconosciuto", "L'id del pagamento ricevuto non esiste"),
        [Duplicato] = ("pagamento duplicato", "L'id del pagamento ricevuto e' duplicato"),
        [Scaduto] = ("pagamento scaduto", "L'id del pagamento ricevuto e' scaduto"),
        [Annullato] = ("pagamento annullato", "L'id del pagamento ricevuto e' annullato"),
        [ReceiptDuplicata] = ("pagamento duplicato", "L'id del pagamento ricevuto e' duplicato"),
        [SintassiXsd] = ("Errore XSD", "Errore validazione XSD della request"),
        [Semantica] = ("Errore Generico", "Errore Generico"),
        [SystemError] = ("Errore sistema", "Errore del sistema"),
    };

    public static CtFaultBean Bean(string code, string idDominio)
    {
        var (fs, desc) = Catalog.TryGetValue(code, out var v) ? v : (code, code);
        return new CtFaultBean { FaultCode = code, FaultString = fs, Id = idDominio, Description = desc };
    }
}

[DataContract(Namespace = PaForNodeNs.Xsd)]
public class CtQrCode
{
    [DataMember(Order = 0)] public string FiscalCode { get; set; } = "";
    [DataMember(Order = 1)] public string NoticeNumber { get; set; } = "";
}

// Identificativo univoco annidato come da spec (debtor.uniqueIdentifier.{type,value}).
[DataContract(Namespace = PaForNodeNs.Xsd)]
public class CtEntityUniqueIdentifier
{
    [DataMember(Order = 0, Name = "entityUniqueIdentifierType")] public string EntityUniqueIdentifierType { get; set; } = "F"; // F/G
    [DataMember(Order = 1, Name = "entityUniqueIdentifierValue")] public string EntityUniqueIdentifierValue { get; set; } = "";
}

[DataContract(Namespace = PaForNodeNs.Xsd)]
public class CtSubject
{
    [DataMember(Order = 0, Name = "uniqueIdentifier")] public CtEntityUniqueIdentifier UniqueIdentifier { get; set; } = new();
    [DataMember(Order = 1, Name = "fullName")] public string FullName { get; set; } = "";
    [DataMember(Order = 2, Name = "streetName", EmitDefaultValue = false)] public string? StreetName { get; set; }
    [DataMember(Order = 3, Name = "civicNumber", EmitDefaultValue = false)] public string? CivicNumber { get; set; }
    [DataMember(Order = 4, Name = "postalCode", EmitDefaultValue = false)] public string? PostalCode { get; set; }
    [DataMember(Order = 5, Name = "city", EmitDefaultValue = false)] public string? City { get; set; }
    [DataMember(Order = 6, Name = "stateProvinceRegion", EmitDefaultValue = false)] public string? StateProvinceRegion { get; set; }
    [DataMember(Order = 7, Name = "country", EmitDefaultValue = false)] public string? Country { get; set; }
    [DataMember(Order = 8, Name = "email", EmitDefaultValue = false)] public string? Email { get; set; }
}

// Metadata di riconciliazione (key/value) come da Dizionario dei metadata pagoPA.
[DataContract(Namespace = PaForNodeNs.Xsd)]
public class CtMapEntry
{
    [DataMember(Order = 0, Name = "key")] public string Key { get; set; } = "";
    [DataMember(Order = 1, Name = "value")] public string Value { get; set; } = "";
}

// <metadata><mapEntry><key/><value/></mapEntry>…</metadata>
[CollectionDataContract(Namespace = PaForNodeNs.Xsd, Name = "metadata", ItemName = "mapEntry")]
public class CtMetadata : List<CtMapEntry>
{
    public CtMetadata() { }
    public CtMetadata(IEnumerable<CtMapEntry> items) : base(items) { }
}

// Richiesta marca da bollo (E-bollo 2.0): alternativa all'IBAN sul transfer.
[DataContract(Namespace = PaForNodeNs.Xsd)]
public class CtRichiestaMarcaDaBollo
{
    [DataMember(Order = 0)] public string TipoBollo { get; set; } = "";
    [DataMember(Order = 1)] public string HashDocumento { get; set; } = "";
    [DataMember(Order = 2)] public string ProvinciaResidenza { get; set; } = "";
}

[DataContract(Namespace = PaForNodeNs.Xsd)]
public class CtTransferPAV2
{
    [DataMember(Order = 0)] public string IdTransfer { get; set; } = "";
    [DataMember(Order = 1)] public decimal TransferAmount { get; set; }
    [DataMember(Order = 2, EmitDefaultValue = false)] public string? FiscalCodePA { get; set; }
    [DataMember(Order = 3, EmitDefaultValue = false)] public string? IBAN { get; set; }
    [DataMember(Order = 4, EmitDefaultValue = false)] public CtRichiestaMarcaDaBollo? RichiestaMarcaDaBollo { get; set; }
    [DataMember(Order = 5)] public string RemittanceInformation { get; set; } = "";
    [DataMember(Order = 6)] public string TransferCategory { get; set; } = "";
    [DataMember(Order = 7, EmitDefaultValue = false)] public CtMetadata? Metadata { get; set; }
}

// <transferList><transfer>…</transfer>…</transferList> (item ripetuto, come WSDL).
[CollectionDataContract(Namespace = PaForNodeNs.Xsd, Name = "transferListPAV2", ItemName = "transfer")]
public class CtTransferListPAV2 : List<CtTransferPAV2>
{
    public CtTransferListPAV2() { }
    public CtTransferListPAV2(IEnumerable<CtTransferPAV2> items) : base(items) { }
}

[DataContract(Namespace = PaForNodeNs.Xsd)]
public class CtPaymentPAV2
{
    [DataMember(Order = 0)] public string CreditorReferenceId { get; set; } = ""; // IUV
    [DataMember(Order = 1)] public decimal PaymentAmount { get; set; }
    [DataMember(Order = 2)] public DateTime DueDate { get; set; }
    [DataMember(Order = 3, EmitDefaultValue = false)] public DateTime? RetentionDate { get; set; }
    [DataMember(Order = 4)] public bool LastPayment { get; set; }
    [DataMember(Order = 5)] public string Description { get; set; } = "";
    [DataMember(Order = 6)] public string CompanyName { get; set; } = "";
    [DataMember(Order = 7, EmitDefaultValue = false)] public string? OfficeName { get; set; }
    [DataMember(Order = 8)] public CtSubject Debtor { get; set; } = new();
    [DataMember(Order = 9)] public CtTransferListPAV2 TransferList { get; set; } = new();
}

[DataContract(Namespace = PaForNodeNs.Xsd)]
public class CtPaymentOptionDescriptionPA
{
    [DataMember(Order = 0)] public decimal Amount { get; set; }
    [DataMember(Order = 1)] public string Options { get; set; } = "EQ"; // EQ / GTE
    [DataMember(Order = 2, EmitDefaultValue = false)] public DateTime? DueDate { get; set; }
    [DataMember(Order = 3, EmitDefaultValue = false)] public string? DetailDescription { get; set; }
    [DataMember(Order = 4)] public bool AllCCP { get; set; }
}

[DataContract(Namespace = PaForNodeNs.Xsd)]
public class CtPaymentOptionsDescriptionListPA
{
    [DataMember(Order = 0)] public List<CtPaymentOptionDescriptionPA> PaymentOptionDescription { get; set; } = new();
}

// ---------------- Receipt (paSendRTV2) ----------------
[DataContract(Namespace = PaForNodeNs.Xsd)]
public class CtTransferPAReceiptV2
{
    [DataMember(Order = 0)] public string IdTransfer { get; set; } = "";
    [DataMember(Order = 1)] public decimal TransferAmount { get; set; }
    [DataMember(Order = 2, EmitDefaultValue = false)] public string? FiscalCodePA { get; set; }
    [DataMember(Order = 3, EmitDefaultValue = false)] public string? IBAN { get; set; }
    [DataMember(Order = 4)] public string RemittanceInformation { get; set; } = "";
    [DataMember(Order = 5)] public string TransferCategory { get; set; } = "";
    [DataMember(Order = 6, EmitDefaultValue = false)] public CtMetadata? Metadata { get; set; }
}

[CollectionDataContract(Namespace = PaForNodeNs.Xsd, Name = "transferListPAReceiptV2", ItemName = "transfer")]
public class CtTransferListPAReceiptV2 : List<CtTransferPAReceiptV2>
{
    public CtTransferListPAReceiptV2() { }
    public CtTransferListPAReceiptV2(IEnumerable<CtTransferPAReceiptV2> items) : base(items) { }
}

[DataContract(Namespace = PaForNodeNs.Xsd)]
public class CtReceiptV2
{
    [DataMember(Order = 0)] public string ReceiptId { get; set; } = "";
    [DataMember(Order = 1)] public string NoticeNumber { get; set; } = "";
    [DataMember(Order = 2)] public string FiscalCode { get; set; } = "";
    [DataMember(Order = 3)] public string Outcome { get; set; } = "OK";
    [DataMember(Order = 4)] public string CreditorReferenceId { get; set; } = ""; // IUV
    [DataMember(Order = 5)] public decimal PaymentAmount { get; set; }
    [DataMember(Order = 6)] public string Description { get; set; } = "";
    [DataMember(Order = 7)] public string CompanyName { get; set; } = "";
    [DataMember(Order = 8, EmitDefaultValue = false)] public string? OfficeName { get; set; }
    [DataMember(Order = 9, EmitDefaultValue = false)] public CtSubject? Debtor { get; set; }
    [DataMember(Order = 10, EmitDefaultValue = false)] public CtTransferListPAReceiptV2? TransferList { get; set; }
    [DataMember(Order = 11, EmitDefaultValue = false)] public string? IdPSP { get; set; }
    [DataMember(Order = 12, EmitDefaultValue = false)] public string? PspFiscalCode { get; set; }
    [DataMember(Order = 13, EmitDefaultValue = false)] public string? PspPartitaIVA { get; set; }
    [DataMember(Order = 14)] public string PSPCompanyName { get; set; } = "";
    [DataMember(Order = 15, EmitDefaultValue = false)] public string? IdChannel { get; set; }
    [DataMember(Order = 16, EmitDefaultValue = false)] public string? ChannelDescription { get; set; }
    [DataMember(Order = 17, EmitDefaultValue = false)] public CtSubject? Payer { get; set; }
    [DataMember(Order = 18, EmitDefaultValue = false)] public string? PaymentMethod { get; set; }
    [DataMember(Order = 19, EmitDefaultValue = false)] public decimal Fee { get; set; }
    [DataMember(Order = 20, EmitDefaultValue = false)] public decimal PrimaryCiIncurredFee { get; set; }
    [DataMember(Order = 21, EmitDefaultValue = false)] public string? IdBundle { get; set; }
    [DataMember(Order = 22, EmitDefaultValue = false)] public string? IdCiBundle { get; set; }
    [DataMember(Order = 23, EmitDefaultValue = false)] public DateTime? PaymentDateTime { get; set; }
    [DataMember(Order = 24, EmitDefaultValue = false)] public DateTime? ApplicationDate { get; set; }
    [DataMember(Order = 25, EmitDefaultValue = false)] public DateTime? TransferDate { get; set; }
    [DataMember(Order = 26, EmitDefaultValue = false)] public bool Standin { get; set; }
}

// ---------------- Request/Response ----------------
[DataContract(Namespace = PaForNodeNs.Xsd)]
public class PaVerifyPaymentNoticeReq
{
    [DataMember(Order = 0)] public string IdPA { get; set; } = "";
    [DataMember(Order = 1)] public string IdBrokerPA { get; set; } = "";
    [DataMember(Order = 2)] public string IdStation { get; set; } = "";
    [DataMember(Order = 3)] public CtQrCode QrCode { get; set; } = new();
}

[DataContract(Namespace = PaForNodeNs.Xsd)]
public class PaVerifyPaymentNoticeRes
{
    [DataMember(Order = 0)] public StOutcome Outcome { get; set; }
    [DataMember(Order = 1, EmitDefaultValue = false)] public CtFaultBean? Fault { get; set; }
    [DataMember(Order = 2, EmitDefaultValue = false)] public CtPaymentOptionsDescriptionListPA? PaymentList { get; set; }
    [DataMember(Order = 3, EmitDefaultValue = false)] public string? PaymentDescription { get; set; }
    [DataMember(Order = 4, EmitDefaultValue = false)] public string? FiscalCodePA { get; set; }
    [DataMember(Order = 5, EmitDefaultValue = false)] public string? CompanyName { get; set; }
    [DataMember(Order = 6, EmitDefaultValue = false)] public string? OfficeName { get; set; }
}

[DataContract(Namespace = PaForNodeNs.Xsd)]
public class PaGetPaymentV2Request
{
    [DataMember(Order = 0)] public string IdPA { get; set; } = "";
    [DataMember(Order = 1)] public string IdBrokerPA { get; set; } = "";
    [DataMember(Order = 2)] public string IdStation { get; set; } = "";
    [DataMember(Order = 3)] public CtQrCode QrCode { get; set; } = new();
    [DataMember(Order = 4, EmitDefaultValue = false)] public decimal Amount { get; set; }
    [DataMember(Order = 5, EmitDefaultValue = false)] public string? TransferType { get; set; } // POSTAL o vuoto
}

[DataContract(Namespace = PaForNodeNs.Xsd)]
public class PaGetPaymentV2Response
{
    [DataMember(Order = 0)] public StOutcome Outcome { get; set; }
    [DataMember(Order = 1, EmitDefaultValue = false)] public CtFaultBean? Fault { get; set; }
    [DataMember(Order = 2, EmitDefaultValue = false)] public CtPaymentPAV2? Data { get; set; }
}

[DataContract(Namespace = PaForNodeNs.Xsd)]
public class PaSendRTV2Request
{
    [DataMember(Order = 0)] public string IdPA { get; set; } = "";
    [DataMember(Order = 1)] public string IdBrokerPA { get; set; } = "";
    [DataMember(Order = 2)] public string IdStation { get; set; } = "";
    [DataMember(Order = 3)] public CtReceiptV2 Receipt { get; set; } = new();
}

[DataContract(Namespace = PaForNodeNs.Xsd)]
public class PaSendRTV2Response
{
    [DataMember(Order = 0)] public StOutcome Outcome { get; set; }
    [DataMember(Order = 1, EmitDefaultValue = false)] public CtFaultBean? Fault { get; set; }
}

// ---------------- paDemandPaymentNotice (pagamento SPONTANEO: avviso on-demand) ----------------
[DataContract(Namespace = PaForNodeNs.Xsd)]
public class CtDatiSpecificiServizioRequest
{
    [DataMember(Order = 0)] public decimal Amount { get; set; }
    [DataMember(Order = 1)] public string Causale { get; set; } = "";
    [DataMember(Order = 2, EmitDefaultValue = false)] public string? DebtorFullName { get; set; }
}

[DataContract(Namespace = PaForNodeNs.Xsd)]
public class PaDemandPaymentNoticeRequest
{
    [DataMember(Order = 0)] public string IdPA { get; set; } = "";
    [DataMember(Order = 1)] public string IdBrokerPA { get; set; } = "";
    [DataMember(Order = 2)] public string IdStation { get; set; } = "";
    [DataMember(Order = 3)] public string IdServizio { get; set; } = "";       // tipo dovuto / tassonomia
    [DataMember(Order = 4)] public string IdSoggettoServizio { get; set; } = ""; // CF debitore
    [DataMember(Order = 5)] public CtDatiSpecificiServizioRequest DatiSpecificiServizioRequest { get; set; } = new();
}

[DataContract(Namespace = PaForNodeNs.Xsd)]
public class PaDemandPaymentNoticeResponse
{
    [DataMember(Order = 0)] public StOutcome Outcome { get; set; }
    [DataMember(Order = 1, EmitDefaultValue = false)] public CtFaultBean? Fault { get; set; }
    [DataMember(Order = 2, EmitDefaultValue = false)] public CtQrCode? QrCode { get; set; }
    [DataMember(Order = 3, EmitDefaultValue = false)] public CtPaymentOptionsDescriptionListPA? PaymentList { get; set; }
    [DataMember(Order = 4, EmitDefaultValue = false)] public string? PaymentDescription { get; set; }
    [DataMember(Order = 5, EmitDefaultValue = false)] public string? FiscalCodePA { get; set; }
    [DataMember(Order = 6, EmitDefaultValue = false)] public string? CompanyName { get; set; }
    [DataMember(Order = 7, EmitDefaultValue = false)] public string? OfficeName { get; set; }
}

// Service contract SOAP.
[ServiceContract(Namespace = PaForNodeNs.Xsd, Name = "paForNode")]
public interface IPaForNode
{
    [OperationContract(Action = "paVerifyPaymentNotice")]
    Task<PaVerifyPaymentNoticeRes> PaVerifyPaymentNoticeAsync(PaVerifyPaymentNoticeReq request);

    [OperationContract(Action = "paGetPaymentV2")]
    Task<PaGetPaymentV2Response> PaGetPaymentV2Async(PaGetPaymentV2Request request);

    [OperationContract(Action = "paSendRTV2")]
    Task<PaSendRTV2Response> PaSendRTV2Async(PaSendRTV2Request request);

    [OperationContract(Action = "paDemandPaymentNotice")]
    Task<PaDemandPaymentNoticeResponse> PaDemandPaymentNoticeAsync(PaDemandPaymentNoticeRequest request);
}
