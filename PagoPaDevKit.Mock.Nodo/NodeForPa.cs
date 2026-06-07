using System.Runtime.Serialization;
using System.ServiceModel;

namespace PagoPaDevKit.Mock.Nodo;

// Primitive SOAP nodeForPa: l'EC (PA) chiede al Nodo l'elenco / il singolo Flusso di Rendicontazione.
// Modello classico di recupero FdR (alternativo al pull REST). Il Nodo legge i FdR da GPD.
public static class NodeForPaNs { public const string Xsd = "http://pagopa-api.pagopa.gov.it/node/nodeForPa.xsd"; }

[DataContract(Namespace = NodeForPaNs.Xsd)]
public class CtIdRendicontazione
{
    [DataMember(Order = 0)] public string IdentificativoFlusso { get; set; } = "";
    [DataMember(Order = 1)] public System.DateTime DataOraFlusso { get; set; }
}

[DataContract(Namespace = NodeForPaNs.Xsd)]
public class CtElencoFlussiRendicontazione
{
    [DataMember(Order = 0)] public List<CtIdRendicontazione> IdRendicontazione { get; set; } = new();
}

[DataContract(Namespace = NodeForPaNs.Xsd)]
public class NodoChiediElencoFlussiRendicontazioneReq
{
    [DataMember(Order = 0)] public string IdentificativoPA { get; set; } = "";
    [DataMember(Order = 1, EmitDefaultValue = false)] public string? IdentificativoIntermediarioPA { get; set; }
    [DataMember(Order = 2, EmitDefaultValue = false)] public string? IdentificativoStazioneIntermediarioPA { get; set; }
    [DataMember(Order = 3, EmitDefaultValue = false)] public string? Password { get; set; }
}

[DataContract(Namespace = NodeForPaNs.Xsd)]
public class NodoChiediElencoFlussiRendicontazioneRes
{
    [DataMember(Order = 0)] public StOutcome Outcome { get; set; }
    [DataMember(Order = 1, EmitDefaultValue = false)] public CtFaultBean? Fault { get; set; }
    [DataMember(Order = 2, EmitDefaultValue = false)] public CtElencoFlussiRendicontazione? ElencoFlussiRendicontazione { get; set; }
    [DataMember(Order = 3)] public int TotRestituiti { get; set; }
}

[DataContract(Namespace = NodeForPaNs.Xsd)]
public class NodoChiediFlussoRendicontazioneReq
{
    [DataMember(Order = 0)] public string IdentificativoPA { get; set; } = "";
    [DataMember(Order = 1, EmitDefaultValue = false)] public string? IdentificativoIntermediarioPA { get; set; }
    [DataMember(Order = 2, EmitDefaultValue = false)] public string? IdentificativoStazioneIntermediarioPA { get; set; }
    [DataMember(Order = 3, EmitDefaultValue = false)] public string? Password { get; set; }
    [DataMember(Order = 4)] public string IdentificativoFlusso { get; set; } = "";
}

[DataContract(Namespace = NodeForPaNs.Xsd)]
public class NodoChiediFlussoRendicontazioneRes
{
    [DataMember(Order = 0)] public StOutcome Outcome { get; set; }
    [DataMember(Order = 1, EmitDefaultValue = false)] public CtFaultBean? Fault { get; set; }
    // FlussoRiversamento XML firmato, codificato base64.
    [DataMember(Order = 2, EmitDefaultValue = false)] public byte[]? XmlRendicontazione { get; set; }
}

[ServiceContract(Namespace = NodeForPaNs.Xsd, Name = "nodeForPa")]
public interface INodeForPa
{
    [OperationContract(Action = "nodoChiediElencoFlussiRendicontazione")]
    Task<NodoChiediElencoFlussiRendicontazioneRes> NodoChiediElencoFlussiRendicontazioneAsync(NodoChiediElencoFlussiRendicontazioneReq request);

    [OperationContract(Action = "nodoChiediFlussoRendicontazione")]
    Task<NodoChiediFlussoRendicontazioneRes> NodoChiediFlussoRendicontazioneAsync(NodoChiediFlussoRendicontazioneReq request);
}
