using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace PagoPaDevKit.Mock.Nodo;

// Client SOAP del Nodo verso l'EC (GPD): primitive paForNode.
public class PaForNodeSoapClient
{
    const string Ns = "http://pagopa-api.pagopa.gov.it/pa/paForNode.xsd";
    const string Path = "/gpd/nodo/paForNode.svc";
    readonly IHttpClientFactory _http;
    readonly string _idStation;

    public PaForNodeSoapClient(IHttpClientFactory http, IConfiguration cfg)
    {
        _http = http;
        _idStation = cfg["Nodo:IdStation"] ?? "PAGOPADEVKIT_NODO";
    }

    // Fault emesso dall'EC (faultCode/faultString/description) — verrà incapsulato dal Nodo nei campi original*.
    public record EcFault(string Code, string Str, string Desc);
    static EcFault ReadFault(XDocument d) => new(Val(d, "faultCode"), Val(d, "faultString"), Val(d, "description"));

    public record VerifyResult(bool Ok, EcFault? Fault,
        string CompanyName, string OfficeName, string Description, decimal AmountEuro, string FiscalCodePA, bool AllCCP);

    public record TransferRow(string IdTransfer, decimal Amount, string? FiscalCodePA, string? Iban, string Remittance, string Category, List<(string Key, string Value)> Metadata);
    public record PaymentResult(bool Ok, EcFault? Fault, string Iuv, decimal AmountEuro,
        string CompanyName, string OfficeName, string Description, List<TransferRow> Transfers);

    public async Task<VerifyResult> VerifyAsync(string ofc, string nav)
    {
        string inner = $@"<t:PaVerifyPaymentNotice><t:request><t:IdPA>{ofc}</t:IdPA><t:IdBrokerPA>{ofc}</t:IdBrokerPA>
<t:IdStation>{_idStation}</t:IdStation><t:QrCode><t:FiscalCode>{ofc}</t:FiscalCode><t:NoticeNumber>{nav}</t:NoticeNumber></t:QrCode>
</t:request></t:PaVerifyPaymentNotice>";
        var d = await Post("paVerifyPaymentNotice", inner);
        if (Val(d, "Outcome") == "OK")
            return new VerifyResult(true, null, Val(d, "CompanyName"), Val(d, "OfficeName"),
                Val(d, "PaymentDescription"), Dec(d, "Amount"), Val(d, "FiscalCodePA"),
                Val(d, "AllCCP").Equals("true", StringComparison.OrdinalIgnoreCase));
        return new VerifyResult(false, ReadFault(d), "", "", "", 0, ofc, false);
    }

    public async Task<PaymentResult> GetPaymentV2Async(string ofc, string nav, string? transferType = null)
    {
        string tt = string.IsNullOrEmpty(transferType) ? "" : $"<t:TransferType>{System.Security.SecurityElement.Escape(transferType)}</t:TransferType>";
        string inner = $@"<t:PaGetPaymentV2><t:request><t:IdPA>{ofc}</t:IdPA><t:IdBrokerPA>{ofc}</t:IdBrokerPA>
<t:IdStation>{_idStation}</t:IdStation><t:QrCode><t:FiscalCode>{ofc}</t:FiscalCode><t:NoticeNumber>{nav}</t:NoticeNumber></t:QrCode>{tt}
</t:request></t:PaGetPaymentV2>";
        var d = await Post("paGetPaymentV2", inner);
        if (Val(d, "Outcome") != "OK")
            return new PaymentResult(false, ReadFault(d), "", 0, "", "", "", new());
        var transfers = d.Descendants().Where(e => string.Equals(e.Name.LocalName, "transfer", StringComparison.OrdinalIgnoreCase)).Select(tr => new TransferRow(
            Child(tr, "IdTransfer"), DecS(Child(tr, "TransferAmount")), Child(tr, "FiscalCodePA"),
            Child(tr, "IBAN"), Child(tr, "RemittanceInformation"), Child(tr, "TransferCategory"),
            tr.Descendants().Where(e => string.Equals(e.Name.LocalName, "mapEntry", StringComparison.OrdinalIgnoreCase))
              .Select(me => (Child(me, "key"), Child(me, "value"))).ToList())).ToList();
        return new PaymentResult(true, null, Val(d, "CreditorReferenceId"), Dec(d, "PaymentAmount"),
            Val(d, "CompanyName"), Val(d, "OfficeName"), Val(d, "Description"), transfers);
    }

    // Ricevuta completa (paSendRTV2): transferList, payer annidato, channelDescription, date.
    public async Task<(bool ok, EcFault? fault)> SendRTAsync(TokenCtx ctx, string pspCompanyName, decimal feeEuro,
        string receiptId, string idPsp, string? payerCf, string? payerName)
    {
        string A(decimal x) => x.ToString("0.00", CultureInfo.InvariantCulture);
        string Esc(string? s) => System.Security.SecurityElement.Escape(s ?? "");
        string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
        string Meta(TokenTransfer t) => t.Metadata.Count == 0 ? "" :
            "<t:Metadata>" + string.Concat(t.Metadata.Select(m => $"<t:mapEntry><t:key>{Esc(m.Key)}</t:key><t:value>{Esc(m.Value)}</t:value></t:mapEntry>")) + "</t:Metadata>";
        string transferList = "<t:TransferList>" + string.Concat(ctx.Transfers.Select(t =>
            $"<t:transfer><t:IdTransfer>{Esc(t.IdTransfer)}</t:IdTransfer><t:TransferAmount>{A(t.Amount)}</t:TransferAmount>" +
            $"<t:FiscalCodePA>{Esc(t.FiscalCodePA)}</t:FiscalCodePA><t:IBAN>{Esc(t.Iban)}</t:IBAN>" +
            $"<t:RemittanceInformation>{Esc(t.Remittance)}</t:RemittanceInformation><t:TransferCategory>{Esc(t.Category)}</t:TransferCategory>{Meta(t)}</t:transfer>")) + "</t:TransferList>";
        string payer = payerCf is null ? "" :
            $"<t:Payer><t:uniqueIdentifier><t:entityUniqueIdentifierType>F</t:entityUniqueIdentifierType><t:entityUniqueIdentifierValue>{Esc(payerCf)}</t:entityUniqueIdentifierValue></t:uniqueIdentifier><t:fullName>{Esc(payerName)}</t:fullName></t:Payer>";
        string inner = $@"<t:PaSendRTV2><t:request><t:IdPA>{ctx.Ofc}</t:IdPA><t:IdBrokerPA>{ctx.Ofc}</t:IdBrokerPA>
<t:IdStation>{_idStation}</t:IdStation><t:Receipt>
<t:ReceiptId>{receiptId}</t:ReceiptId><t:NoticeNumber>{ctx.Nav}</t:NoticeNumber><t:FiscalCode>{ctx.Ofc}</t:FiscalCode>
<t:Outcome>OK</t:Outcome><t:CreditorReferenceId>{ctx.Iuv}</t:CreditorReferenceId><t:PaymentAmount>{A(ctx.AmountEuro)}</t:PaymentAmount>
<t:Description>{Esc(ctx.Description)}</t:Description><t:CompanyName>{Esc(ctx.CompanyName)}</t:CompanyName>{transferList}
<t:IdPSP>{Esc(idPsp)}</t:IdPSP><t:PspFiscalCode>{Esc(idPsp)}</t:PspFiscalCode><t:PSPCompanyName>{Esc(pspCompanyName)}</t:PSPCompanyName>
<t:IdChannel>{Esc(idPsp)}_01</t:IdChannel><t:ChannelDescription>Checkout pagoPA (mock)</t:ChannelDescription>{payer}
<t:PaymentMethod>CP</t:PaymentMethod><t:Fee>{A(feeEuro)}</t:Fee><t:PaymentDateTime>{now}</t:PaymentDateTime>
<t:ApplicationDate>{now}</t:ApplicationDate><t:TransferDate>{now}</t:TransferDate></t:Receipt></t:request></t:PaSendRTV2>";
        var d = await Post("paSendRTV2", inner);
        return Val(d, "Outcome") == "OK" ? (true, null) : (false, ReadFault(d));
    }

    public record DemandResult(bool Ok, EcFault? Fault, string Nav, decimal AmountEuro, string CompanyName, string Description);

    public async Task<DemandResult> DemandAsync(string ofc, string idServizio, string debtorCf, decimal amount, string causale, string? debtorFullName)
    {
        string A(decimal x) => x.ToString("0.00", CultureInfo.InvariantCulture);
        string Esc(string? s) => System.Security.SecurityElement.Escape(s ?? "");
        string inner = $@"<t:PaDemandPaymentNotice><t:request><t:IdPA>{ofc}</t:IdPA><t:IdBrokerPA>{ofc}</t:IdBrokerPA>
<t:IdStation>{_idStation}</t:IdStation><t:IdServizio>{Esc(idServizio)}</t:IdServizio><t:IdSoggettoServizio>{Esc(debtorCf)}</t:IdSoggettoServizio>
<t:DatiSpecificiServizioRequest><t:Amount>{A(amount)}</t:Amount><t:Causale>{Esc(causale)}</t:Causale><t:DebtorFullName>{Esc(debtorFullName)}</t:DebtorFullName></t:DatiSpecificiServizioRequest>
</t:request></t:PaDemandPaymentNotice>";
        var d = await Post("paDemandPaymentNotice", inner);
        if (Val(d, "Outcome") != "OK")
            return new DemandResult(false, ReadFault(d), "", 0, "", "");
        return new DemandResult(true, null, Val(d, "NoticeNumber"), Dec(d, "Amount"), Val(d, "CompanyName"), Val(d, "PaymentDescription"));
    }

    async Task<XDocument> Post(string action, string innerBody)
    {
        string env = $@"<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:t=""{Ns}"">
<soap:Body>{innerBody}</soap:Body></soap:Envelope>";
        var content = new StringContent(env, Encoding.UTF8, "text/xml");
        content.Headers.Add("SOAPAction", $"\"{action}\"");
        var resp = await _http.CreateClient("gpd").PostAsync(Path, content);
        return XDocument.Parse(await resp.Content.ReadAsStringAsync());
    }

    static string Val(XDocument d, string local) =>
        d.Descendants().FirstOrDefault(e => string.Equals(e.Name.LocalName, local, StringComparison.OrdinalIgnoreCase))?.Value ?? "";
    static string Child(XElement parent, string local) =>
        parent.Descendants().FirstOrDefault(e => string.Equals(e.Name.LocalName, local, StringComparison.OrdinalIgnoreCase))?.Value ?? "";
    static decimal Dec(XDocument d, string local) => DecS(Val(d, local));
    static decimal DecS(string s) => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;
}
