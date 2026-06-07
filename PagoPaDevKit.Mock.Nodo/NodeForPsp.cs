using System.Runtime.Serialization;
using System.ServiceModel;

namespace PagoPaDevKit.Mock.Nodo;

// =====================================================================
// Contratti SOAP nodeForPsp: primitive del NODO chiamate dal PSP/Checkout.
// Modellati su pagopa-api/wsdl/xsd/nodeForPsp.xsd.
// Flusso PSP->Nodo: verifyPaymentNotice -> activatePaymentNotice(V2) ->
//                   sendPaymentOutcomeV2. Il Nodo a sua volta chiama l'EC
//                   (GPD) via paForNode.
// =====================================================================

public static class NodeNs { public const string Xsd = "http://pagopa-api.pagopa.gov.it/node/nodeForPsp.xsd"; }

[DataContract(Namespace = NodeNs.Xsd)]
public enum StOutcome { [EnumMember] OK, [EnumMember] KO }

// faultBean come da guida "Gestione degli errori" pagoPA: include serial e i campi
// original* per l'errore emesso dalla controparte (EC/PA o PSP).
[DataContract(Namespace = NodeNs.Xsd)]
public class CtFaultBean
{
    [DataMember(Order = 0, Name = "faultCode")] public string FaultCode { get; set; } = "";
    [DataMember(Order = 1, Name = "faultString")] public string FaultString { get; set; } = "";
    [DataMember(Order = 2, Name = "id")] public string Id { get; set; } = "";
    [DataMember(Order = 3, Name = "description", EmitDefaultValue = false)] public string? Description { get; set; }
    [DataMember(Order = 4, Name = "serial", EmitDefaultValue = false)] public int? Serial { get; set; }
    [DataMember(Order = 5, Name = "originalFaultCode", EmitDefaultValue = false)] public string? OriginalFaultCode { get; set; }
    [DataMember(Order = 6, Name = "originalFaultString", EmitDefaultValue = false)] public string? OriginalFaultString { get; set; }
    [DataMember(Order = 7, Name = "originalDescription", EmitDefaultValue = false)] public string? OriginalDescription { get; set; }
}

[DataContract(Namespace = NodeNs.Xsd)]
public class CtQrCode
{
    [DataMember(Order = 0)] public string FiscalCode { get; set; } = "";
    [DataMember(Order = 1)] public string NoticeNumber { get; set; } = "";
}

// ---- verifyPaymentNotice ----
[DataContract(Namespace = NodeNs.Xsd)]
public class VerifyPaymentNoticeReq
{
    [DataMember(Order = 0)] public string IdPSP { get; set; } = "";
    [DataMember(Order = 1)] public string IdBrokerPSP { get; set; } = "";
    [DataMember(Order = 2)] public string IdChannel { get; set; } = "";
    [DataMember(Order = 3, EmitDefaultValue = false)] public string? Password { get; set; }
    [DataMember(Order = 4)] public CtQrCode QrCode { get; set; } = new();
}

[DataContract(Namespace = NodeNs.Xsd)]
public class CtPaymentOptionDescription
{
    [DataMember(Order = 0)] public decimal Amount { get; set; }
    [DataMember(Order = 1)] public string Options { get; set; } = "EQ";
    [DataMember(Order = 2, EmitDefaultValue = false)] public System.DateTime? DueDate { get; set; }
    [DataMember(Order = 3, EmitDefaultValue = false)] public string? PaymentNote { get; set; }
    [DataMember(Order = 4)] public bool AllCCP { get; set; }
}

[DataContract(Namespace = NodeNs.Xsd)]
public class CtPaymentOptionsList
{
    [DataMember(Order = 0)] public List<CtPaymentOptionDescription> PaymentOptionDescription { get; set; } = new();
}

[DataContract(Namespace = NodeNs.Xsd)]
public class VerifyPaymentNoticeRes
{
    [DataMember(Order = 0)] public StOutcome Outcome { get; set; }
    [DataMember(Order = 1, EmitDefaultValue = false)] public CtFaultBean? Fault { get; set; }
    [DataMember(Order = 2, EmitDefaultValue = false)] public CtPaymentOptionsList? PaymentList { get; set; }
    [DataMember(Order = 3, EmitDefaultValue = false)] public string? PaymentDescription { get; set; }
    [DataMember(Order = 4, EmitDefaultValue = false)] public string? FiscalCodePA { get; set; }
    [DataMember(Order = 5, EmitDefaultValue = false)] public string? CompanyName { get; set; }
    [DataMember(Order = 6, EmitDefaultValue = false)] public string? OfficeName { get; set; }
}

// ---- activatePaymentNotice (V2) ----
[DataContract(Namespace = NodeNs.Xsd)]
public class ActivatePaymentNoticeReq
{
    [DataMember(Order = 0)] public string IdPSP { get; set; } = "";
    [DataMember(Order = 1)] public string IdBrokerPSP { get; set; } = "";
    [DataMember(Order = 2)] public string IdChannel { get; set; } = "";
    [DataMember(Order = 3, EmitDefaultValue = false)] public string? Password { get; set; }
    [DataMember(Order = 4, EmitDefaultValue = false)] public string? IdempotencyKey { get; set; }
    [DataMember(Order = 5)] public CtQrCode QrCode { get; set; } = new();
    [DataMember(Order = 6, EmitDefaultValue = false)] public System.DateTime? ExpirationTime { get; set; }
    [DataMember(Order = 7)] public decimal Amount { get; set; }
    [DataMember(Order = 8, EmitDefaultValue = false)] public string? TransferType { get; set; } // POSTAL o vuoto
}

[DataContract(Namespace = NodeNs.Xsd)]
public class CtTransfer
{
    [DataMember(Order = 0)] public string IdTransfer { get; set; } = "";
    [DataMember(Order = 1)] public decimal TransferAmount { get; set; }
    [DataMember(Order = 2, EmitDefaultValue = false)] public string? FiscalCodePA { get; set; }
    [DataMember(Order = 3, EmitDefaultValue = false)] public string? IBAN { get; set; }
    [DataMember(Order = 4)] public string RemittanceInformation { get; set; } = "";
}

[DataContract(Namespace = NodeNs.Xsd)]
public class CtTransferList { [DataMember(Order = 0)] public List<CtTransfer> Transfer { get; set; } = new(); }

[DataContract(Namespace = NodeNs.Xsd)]
public class ActivatePaymentNoticeRes
{
    [DataMember(Order = 0)] public StOutcome Outcome { get; set; }
    [DataMember(Order = 1, EmitDefaultValue = false)] public CtFaultBean? Fault { get; set; }
    [DataMember(Order = 2)] public decimal TotalAmount { get; set; }
    [DataMember(Order = 3, EmitDefaultValue = false)] public string? PaymentDescription { get; set; }
    [DataMember(Order = 4, EmitDefaultValue = false)] public string? FiscalCodePA { get; set; }
    [DataMember(Order = 5, EmitDefaultValue = false)] public string? CompanyName { get; set; }
    [DataMember(Order = 6, EmitDefaultValue = false)] public string? OfficeName { get; set; }
    [DataMember(Order = 7, EmitDefaultValue = false)] public string? PaymentToken { get; set; }
    [DataMember(Order = 8, EmitDefaultValue = false)] public CtTransferList? TransferList { get; set; }
    [DataMember(Order = 9, EmitDefaultValue = false)] public string? CreditorReferenceId { get; set; }
}

// ---- sendPaymentOutcomeV2 ----
[DataContract(Namespace = NodeNs.Xsd)]
public class CtPayer
{
    [DataMember(Order = 0, EmitDefaultValue = false)] public string? UniqueIdentifierValue { get; set; }
    [DataMember(Order = 1, EmitDefaultValue = false)] public string? FullName { get; set; }
}

[DataContract(Namespace = NodeNs.Xsd)]
public class CtSendDetails
{
    [DataMember(Order = 0, EmitDefaultValue = false)] public string? PaymentMethod { get; set; }
    [DataMember(Order = 1, EmitDefaultValue = false)] public string? PaymentChannel { get; set; }
    [DataMember(Order = 2)] public decimal Fee { get; set; }
    [DataMember(Order = 3, EmitDefaultValue = false)] public decimal PrimaryCiIncurredFee { get; set; }
    [DataMember(Order = 4, EmitDefaultValue = false)] public string? IdBundle { get; set; }
    [DataMember(Order = 5, EmitDefaultValue = false)] public string? IdCiBundle { get; set; }
    [DataMember(Order = 6, EmitDefaultValue = false)] public CtPayer? Payer { get; set; }
    [DataMember(Order = 7, EmitDefaultValue = false)] public System.DateTime? ApplicationDate { get; set; }
    [DataMember(Order = 8, EmitDefaultValue = false)] public System.DateTime? TransferDate { get; set; }
    [DataMember(Order = 9, EmitDefaultValue = false)] public string? MarcaDaBollo { get; set; }
}

// Lista paymentToken (carrello, max 5): <PaymentTokens><paymentToken>..</paymentToken>..</PaymentTokens>
[CollectionDataContract(Namespace = NodeNs.Xsd, Name = "PaymentTokens", ItemName = "paymentToken")]
public class PaymentTokenList : List<string> { }

[DataContract(Namespace = NodeNs.Xsd)]
public class SendPaymentOutcomeV2Request
{
    [DataMember(Order = 0)] public string IdPSP { get; set; } = "";
    [DataMember(Order = 1)] public string IdBrokerPSP { get; set; } = "";
    [DataMember(Order = 2)] public string IdChannel { get; set; } = "";
    [DataMember(Order = 3, EmitDefaultValue = false)] public string? Password { get; set; }
    [DataMember(Order = 4, EmitDefaultValue = false)] public string? IdempotencyKey { get; set; }
    [DataMember(Order = 5)] public PaymentTokenList PaymentTokens { get; set; } = new();
    [DataMember(Order = 6)] public string Outcome { get; set; } = "OK";
    [DataMember(Order = 7, EmitDefaultValue = false)] public CtSendDetails? Details { get; set; }
}

// pspNotifyPayment v2: notifica PSP -> Nodo prima dell'esito (step 7-8 del caso d'uso).
[DataContract(Namespace = NodeNs.Xsd)]
public class CtNotifyPaymentItem
{
    [DataMember(Order = 0)] public string PaymentToken { get; set; } = "";
    [DataMember(Order = 1, EmitDefaultValue = false)] public string? FiscalCodePA { get; set; }
    [DataMember(Order = 2, EmitDefaultValue = false)] public string? CompanyName { get; set; }
    [DataMember(Order = 3, EmitDefaultValue = false)] public decimal DebtAmount { get; set; }
}

[CollectionDataContract(Namespace = NodeNs.Xsd, Name = "PaymentList", ItemName = "payment")]
public class NotifyPaymentList : List<CtNotifyPaymentItem> { }

[DataContract(Namespace = NodeNs.Xsd)]
public class PspNotifyPaymentV2Request
{
    [DataMember(Order = 0)] public string IdPSP { get; set; } = "";
    [DataMember(Order = 1)] public string IdBrokerPSP { get; set; } = "";
    [DataMember(Order = 2)] public string IdChannel { get; set; } = "";
    [DataMember(Order = 3, EmitDefaultValue = false)] public string? Password { get; set; }
    [DataMember(Order = 4, EmitDefaultValue = false)] public string? TransactionId { get; set; }
    [DataMember(Order = 5, EmitDefaultValue = false)] public decimal TotalAmount { get; set; }
    [DataMember(Order = 6, EmitDefaultValue = false)] public decimal Fee { get; set; }
    [DataMember(Order = 7, EmitDefaultValue = false)] public System.DateTime? TimestampOperation { get; set; }
    [DataMember(Order = 8)] public NotifyPaymentList PaymentList { get; set; } = new();
}

[DataContract(Namespace = NodeNs.Xsd)]
public class PspNotifyPaymentV2Response
{
    [DataMember(Order = 0)] public StOutcome Outcome { get; set; }
    [DataMember(Order = 1, EmitDefaultValue = false)] public CtFaultBean? Fault { get; set; }
}

[DataContract(Namespace = NodeNs.Xsd)]
public class SendPaymentOutcomeV2Response
{
    [DataMember(Order = 0)] public StOutcome Outcome { get; set; }
    [DataMember(Order = 1, EmitDefaultValue = false)] public CtFaultBean? Fault { get; set; }
}

// ---- nodoInviaFlussoRendicontazione (PSP invia il FdR firmato) ----
[DataContract(Namespace = NodeNs.Xsd)]
public class NodoInviaFlussoRendicontazioneRequest
{
    [DataMember(Order = 0)] public string IdentificativoPSP { get; set; } = "";
    [DataMember(Order = 1)] public string IdentificativoIntermediarioPSP { get; set; } = "";
    [DataMember(Order = 2)] public string IdentificativoCanale { get; set; } = "";
    [DataMember(Order = 3, EmitDefaultValue = false)] public string? Password { get; set; }
    [DataMember(Order = 4)] public string IdentificativoFlusso { get; set; } = "";
    [DataMember(Order = 5)] public System.DateTime DataOraFlusso { get; set; }
    [DataMember(Order = 6)] public string IdentificativoDominio { get; set; } = "";
    // FlussoRiversamento XML firmato, codificato base64 (come da WSDL reale).
    [DataMember(Order = 7)] public byte[] XmlRendicontazione { get; set; } = Array.Empty<byte>();
}

[DataContract(Namespace = NodeNs.Xsd)]
public class NodoInviaFlussoRendicontazioneResponse
{
    [DataMember(Order = 0)] public StOutcome Outcome { get; set; }
    [DataMember(Order = 1, EmitDefaultValue = false)] public CtFaultBean? Fault { get; set; }
}

// ---- demandPaymentNotice (pagamento SPONTANEO: avviso on-demand) ----
[DataContract(Namespace = NodeNs.Xsd)]
public class CtDatiSpecificiServizio
{
    [DataMember(Order = 0)] public decimal Amount { get; set; }
    [DataMember(Order = 1)] public string Causale { get; set; } = "";
    [DataMember(Order = 2, EmitDefaultValue = false)] public string? DebtorFullName { get; set; }
}

[DataContract(Namespace = NodeNs.Xsd)]
public class DemandPaymentNoticeRequest
{
    [DataMember(Order = 0)] public string IdPSP { get; set; } = "";
    [DataMember(Order = 1)] public string IdBrokerPSP { get; set; } = "";
    [DataMember(Order = 2)] public string IdChannel { get; set; } = "";
    [DataMember(Order = 3, EmitDefaultValue = false)] public string? Password { get; set; }
    [DataMember(Order = 4)] public string IdPA { get; set; } = "";
    [DataMember(Order = 5)] public string IdServizio { get; set; } = "";
    [DataMember(Order = 6)] public string IdSoggettoServizio { get; set; } = "";
    [DataMember(Order = 7)] public CtDatiSpecificiServizio DatiSpecificiServizioRequest { get; set; } = new();
}

[DataContract(Namespace = NodeNs.Xsd)]
public class DemandPaymentNoticeResponse
{
    [DataMember(Order = 0)] public StOutcome Outcome { get; set; }
    [DataMember(Order = 1, EmitDefaultValue = false)] public CtFaultBean? Fault { get; set; }
    [DataMember(Order = 2, EmitDefaultValue = false)] public CtQrCode? QrCode { get; set; }
    [DataMember(Order = 3, EmitDefaultValue = false)] public decimal PaymentAmount { get; set; }
    [DataMember(Order = 4, EmitDefaultValue = false)] public string? PaymentDescription { get; set; }
    [DataMember(Order = 5, EmitDefaultValue = false)] public string? FiscalCodePA { get; set; }
    [DataMember(Order = 6, EmitDefaultValue = false)] public string? CompanyName { get; set; }
}

[ServiceContract(Namespace = NodeNs.Xsd, Name = "nodeForPsp")]
public interface INodeForPsp
{
    [OperationContract(Action = "verifyPaymentNotice")]
    Task<VerifyPaymentNoticeRes> VerifyPaymentNoticeAsync(VerifyPaymentNoticeReq request);

    [OperationContract(Action = "demandPaymentNotice")]
    Task<DemandPaymentNoticeResponse> DemandPaymentNoticeAsync(DemandPaymentNoticeRequest request);

    [OperationContract(Action = "activatePaymentNotice")]
    Task<ActivatePaymentNoticeRes> ActivatePaymentNoticeAsync(ActivatePaymentNoticeReq request);

    [OperationContract(Action = "pspNotifyPaymentV2")]
    Task<PspNotifyPaymentV2Response> PspNotifyPaymentV2Async(PspNotifyPaymentV2Request request);

    [OperationContract(Action = "sendPaymentOutcomeV2")]
    Task<SendPaymentOutcomeV2Response> SendPaymentOutcomeV2Async(SendPaymentOutcomeV2Request request);

    [OperationContract(Action = "nodoInviaFlussoRendicontazione")]
    Task<NodoInviaFlussoRendicontazioneResponse> NodoInviaFlussoRendicontazioneAsync(NodoInviaFlussoRendicontazioneRequest request);
}
