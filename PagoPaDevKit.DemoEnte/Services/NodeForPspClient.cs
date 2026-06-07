using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace PagoPaDevKit.DemoEnte;

// Client SOAP nodeForPsp usato dal flusso "Test Cittadino" in-console: il console
// si comporta da PSP e chiama il NODO (che poi parla con l'EC/GPD). Identico contratto
// del Checkout reale, cosi lo stepper esercita il giro vero verify->activate->notify->outcome.
public class NodeForPspClient
{
    const string Ns = "http://pagopa-api.pagopa.gov.it/node/nodeForPsp.xsd";
    const string Path = "/nodo/node/nodeForPsp.svc";
    readonly IHttpClientFactory _http;
    readonly string _idPsp, _idBroker, _idChannel;

    public NodeForPspClient(IHttpClientFactory http, IConfiguration cfg)
    {
        _http = http;
        _idPsp = cfg["Ente:IdPsp"] ?? "BANCADEMO";
        _idBroker = cfg["Ente:IdBrokerPsp"] ?? _idPsp;
        _idChannel = cfg["Ente:IdChannel"] ?? $"{_idPsp}_01";
    }

    // Fault del Nodo: faultCode e la busta (es. PPT_ERRORE_EMESSO_DA_PAA); la causa vera
    // sta negli original* restituiti dall'EC (es. PAA_PAGAMENTO_SCONOSCIUTO).
    public record FaultInfo(string Code, string OriginalCode, string OriginalString, string OriginalDescription)
    {
        public bool HasOriginal => !string.IsNullOrEmpty(OriginalCode);
        // Descrizione leggibile: mostra la causa EC reale, con la busta tra parentesi.
        public string Display => HasOriginal
            ? $"{OriginalCode} — {OriginalString}" + (string.IsNullOrEmpty(OriginalDescription) ? "" : $": {OriginalDescription}") + $" (busta {Code})"
            : Code;
    }

    public record VerifyResult(bool Ok, FaultInfo? Fault, string CompanyName, string Description, decimal AmountEuro);
    public record ActivateResult(bool Ok, FaultInfo? Fault, string PaymentToken, decimal TotalAmount,
        string CompanyName, string Description, string Iuv);

    public async Task<VerifyResult> VerifyAsync(string ofc, string nav)
    {
        string inner = $@"<n:VerifyPaymentNotice><n:request>
<n:IdPSP>{_idPsp}</n:IdPSP><n:IdBrokerPSP>{_idBroker}</n:IdBrokerPSP><n:IdChannel>{_idChannel}</n:IdChannel>
<n:QrCode><n:FiscalCode>{ofc}</n:FiscalCode><n:NoticeNumber>{nav}</n:NoticeNumber></n:QrCode>
</n:request></n:VerifyPaymentNotice>";
        var d = await Post("verifyPaymentNotice", inner);
        return Val(d, "Outcome") == "OK"
            ? new VerifyResult(true, null, Val(d, "CompanyName"), Val(d, "PaymentDescription"), Dec(d, "Amount"))
            : new VerifyResult(false, ReadFault(d), "", "", 0);
    }

    public async Task<ActivateResult> ActivateAsync(string ofc, string nav, decimal amount)
    {
        string A(decimal x) => x.ToString("0.00", CultureInfo.InvariantCulture);
        string inner = $@"<n:ActivatePaymentNotice><n:request>
<n:IdPSP>{_idPsp}</n:IdPSP><n:IdBrokerPSP>{_idBroker}</n:IdBrokerPSP><n:IdChannel>{_idChannel}</n:IdChannel>
<n:IdempotencyKey>{Guid.NewGuid():N}</n:IdempotencyKey>
<n:QrCode><n:FiscalCode>{ofc}</n:FiscalCode><n:NoticeNumber>{nav}</n:NoticeNumber></n:QrCode>
<n:Amount>{A(amount)}</n:Amount></n:request></n:ActivatePaymentNotice>";
        var d = await Post("activatePaymentNotice", inner);
        return Val(d, "Outcome") == "OK"
            ? new ActivateResult(true, null, Val(d, "PaymentToken"), Dec(d, "TotalAmount"),
                Val(d, "CompanyName"), Val(d, "PaymentDescription"), Val(d, "CreditorReferenceId"))
            : new ActivateResult(false, ReadFault(d), "", 0, "", "", "");
    }

    static string Tokens(IEnumerable<string> tokens) =>
        "<n:PaymentTokens>" + string.Concat(tokens.Select(t => $"<n:paymentToken>{System.Security.SecurityElement.Escape(t)}</n:paymentToken>")) + "</n:PaymentTokens>";

    public record NotifyItem(string Token, decimal Amount, string CompanyName);

    public async Task<(bool ok, FaultInfo? fault)> PspNotifyAsync(string ofc, decimal totalAmount, decimal fee, IEnumerable<NotifyItem> items)
    {
        string A(decimal x) => x.ToString("0.00", CultureInfo.InvariantCulture);
        string Esc(string? s) => System.Security.SecurityElement.Escape(s ?? "");
        string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
        string list = "<n:PaymentList>" + string.Concat(items.Select(i =>
            $"<n:payment><n:PaymentToken>{Esc(i.Token)}</n:PaymentToken><n:FiscalCodePA>{Esc(ofc)}</n:FiscalCodePA>" +
            $"<n:CompanyName>{Esc(i.CompanyName)}</n:CompanyName><n:DebtAmount>{A(i.Amount)}</n:DebtAmount></n:payment>")) + "</n:PaymentList>";
        string inner = $@"<n:PspNotifyPaymentV2><n:request>
<n:IdPSP>{_idPsp}</n:IdPSP><n:IdBrokerPSP>{_idBroker}</n:IdBrokerPSP><n:IdChannel>{_idChannel}</n:IdChannel>
<n:TransactionId>{Guid.NewGuid():N}</n:TransactionId><n:TotalAmount>{A(totalAmount)}</n:TotalAmount><n:Fee>{A(fee)}</n:Fee>
<n:TimestampOperation>{now}</n:TimestampOperation>{list}</n:request></n:PspNotifyPaymentV2>";
        var d = await Post("pspNotifyPaymentV2", inner);
        return Val(d, "Outcome") == "OK" ? (true, null) : (false, ReadFault(d));
    }

    public async Task<(bool ok, FaultInfo? fault)> SendOutcomeAsync(IEnumerable<string> paymentTokens, bool ok, decimal feeEuro,
        string? payerCf, string? payerName)
    {
        string A(decimal x) => x.ToString("0.00", CultureInfo.InvariantCulture);
        string Esc(string? s) => System.Security.SecurityElement.Escape(s ?? "");
        string payer = payerCf is null ? "" : $"<n:Payer><n:UniqueIdentifierValue>{Esc(payerCf)}</n:UniqueIdentifierValue><n:FullName>{Esc(payerName)}</n:FullName></n:Payer>";
        string inner = $@"<n:SendPaymentOutcomeV2><n:request>
<n:IdPSP>{_idPsp}</n:IdPSP><n:IdBrokerPSP>{_idBroker}</n:IdBrokerPSP><n:IdChannel>{_idChannel}</n:IdChannel>
<n:IdempotencyKey>{Guid.NewGuid():N}</n:IdempotencyKey>{Tokens(paymentTokens)}
<n:Outcome>{(ok ? "OK" : "KO")}</n:Outcome>
<n:Details><n:PaymentMethod>CP</n:PaymentMethod><n:Fee>{A(feeEuro)}</n:Fee>{payer}</n:Details>
</n:request></n:SendPaymentOutcomeV2>";
        var d = await Post("sendPaymentOutcomeV2", inner);
        return Val(d, "Outcome") == "OK" ? (true, null) : (false, ReadFault(d));
    }

    public record DemandResult(bool Ok, FaultInfo? Fault, string Nav);

    public async Task<DemandResult> DemandAsync(string ofc, string idServizio, string debtorCf, decimal amount, string causale, string? debtorFullName)
    {
        string A(decimal x) => x.ToString("0.00", CultureInfo.InvariantCulture);
        string Esc(string? s) => System.Security.SecurityElement.Escape(s ?? "");
        string inner = $@"<n:DemandPaymentNotice><n:request>
<n:IdPSP>{_idPsp}</n:IdPSP><n:IdBrokerPSP>{_idBroker}</n:IdBrokerPSP><n:IdChannel>{_idChannel}</n:IdChannel>
<n:IdPA>{Esc(ofc)}</n:IdPA><n:IdServizio>{Esc(idServizio)}</n:IdServizio><n:IdSoggettoServizio>{Esc(debtorCf)}</n:IdSoggettoServizio>
<n:DatiSpecificiServizioRequest><n:Amount>{A(amount)}</n:Amount><n:Causale>{Esc(causale)}</n:Causale><n:DebtorFullName>{Esc(debtorFullName)}</n:DebtorFullName></n:DatiSpecificiServizioRequest>
</n:request></n:DemandPaymentNotice>";
        var d = await Post("demandPaymentNotice", inner);
        return Val(d, "Outcome") == "OK"
            ? new DemandResult(true, null, Val(d, "NoticeNumber"))
            : new DemandResult(false, ReadFault(d), "");
    }

    async Task<XDocument> Post(string action, string innerBody)
    {
        string env = $@"<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:n=""{Ns}"">
<soap:Body>{innerBody}</soap:Body></soap:Envelope>";
        var content = new StringContent(env, Encoding.UTF8, "text/xml");
        content.Headers.Add("SOAPAction", $"\"{action}\"");
        var resp = await _http.CreateClient("node").PostAsync(Path, content);
        return XDocument.Parse(await resp.Content.ReadAsStringAsync());
    }

    // Estrae la busta + la causa EC reale dal faultBean del Nodo.
    static FaultInfo ReadFault(XDocument d) =>
        new(Val(d, "faultCode"), Val(d, "originalFaultCode"), Val(d, "originalFaultString"), Val(d, "originalDescription"));

    static string Val(XDocument d, string local) =>
        d.Descendants().FirstOrDefault(e => string.Equals(e.Name.LocalName, local, StringComparison.OrdinalIgnoreCase))?.Value ?? "";
    static decimal Dec(XDocument d, string local) => decimal.TryParse(Val(d, local), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;
}

// Esito deterministico delle carte di test (documentate nel README).
public enum CardOutcome { Ok, AuthDenied, Expired, Unknown }

public static class TestCards
{
    public record TestCard(string Pan, string Label, CardOutcome Outcome, string Behavior);

    public static readonly List<TestCard> All = new()
    {
        new("4242424242424242", "Pagamento riuscito", CardOutcome.Ok, "Autorizzazione OK, esito positivo, RT emessa."),
        new("4000000000000002", "Autorizzazione negata", CardOutcome.AuthDenied, "La banca rifiuta: pagamento non riuscito."),
        new("4000000000000069", "Carta scaduta", CardOutcome.Expired, "Carta scaduta: pagamento non riuscito."),
    };

    public static CardOutcome Classify(string? pan)
    {
        var digits = new string((pan ?? "").Where(char.IsDigit).ToArray());
        return digits switch
        {
            "4242424242424242" => CardOutcome.Ok,
            "4000000000000002" => CardOutcome.AuthDenied,
            "4000000000000069" => CardOutcome.Expired,
            _ => CardOutcome.Unknown
        };
    }
}
