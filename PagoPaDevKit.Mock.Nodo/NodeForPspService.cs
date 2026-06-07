using System.Text;

namespace PagoPaDevKit.Mock.Nodo;

// Implementazione del Nodo: orchestra il PSP (nodeForPsp) verso l'EC (paForNode su GPD).
public class NodeForPspService : INodeForPsp
{
    readonly PaForNodeSoapClient _ec;
    readonly PaymentTokenStore _tokens;
    readonly Wisp _wisp;
    readonly IHttpClientFactory _http;
    readonly string _subKey;
    public NodeForPspService(PaForNodeSoapClient ec, PaymentTokenStore tokens, Wisp wisp, IHttpClientFactory http, IConfiguration cfg)
    { _ec = ec; _tokens = tokens; _wisp = wisp; _http = http; _subKey = cfg["Nodo:SubscriptionKey"] ?? "dev-gpd-key"; }

    // Fault generato dal Nodo (PPT_) per errori interni.
    static readonly Dictionary<string, string> PptCatalog = new()
    {
        ["PPT_ERRORE_EMESSO_DA_PAA"] = "Errore restituito dall'ente creditore",
        ["PPT_PAGAMENTO_SCONOSCIUTO"] = "pagamento sconosciuto",
        ["PPT_SEMANTICA"] = "errore semantico",
        ["PPT_RENDICONTAZIONE_KO"] = "rendicontazione non acquisita",
    };
    static CtFaultBean Fault(string code, string id) =>
        new() { FaultCode = code, FaultString = PptCatalog.TryGetValue(code, out var s) ? s : code, Id = id };

    // Errore emesso dalla controparte EC: il Nodo lo incapsula (PPT_ERRORE_EMESSO_DA_PAA + original*).
    static CtFaultBean EcWrap(string idDominio, PaForNodeSoapClient.EcFault ec) => new()
    {
        FaultCode = "PPT_ERRORE_EMESSO_DA_PAA",
        FaultString = "Errore restituito dall'ente creditore",
        Id = idDominio,
        OriginalFaultCode = ec.Code,
        OriginalFaultString = ec.Str,
        OriginalDescription = ec.Desc
    };

    public async Task<VerifyPaymentNoticeRes> VerifyPaymentNoticeAsync(VerifyPaymentNoticeReq r)
    {
        string ofc = r.QrCode.FiscalCode, nav = r.QrCode.NoticeNumber;
        var v = await _ec.VerifyAsync(ofc, nav);
        if (!v.Ok)
            return new VerifyPaymentNoticeRes { Outcome = StOutcome.KO, Fault = EcWrap(ofc, v.Fault!) };
        return new VerifyPaymentNoticeRes
        {
            Outcome = StOutcome.OK,
            PaymentList = new CtPaymentOptionsList { PaymentOptionDescription = { new CtPaymentOptionDescription { Amount = v.AmountEuro, Options = "EQ", AllCCP = v.AllCCP } } },
            PaymentDescription = v.Description, FiscalCodePA = v.FiscalCodePA, CompanyName = v.CompanyName,
            OfficeName = string.IsNullOrEmpty(v.OfficeName) ? null : v.OfficeName
        };
    }

    // Pagamento SPONTANEO: il Nodo inoltra all'EC la richiesta di generazione avviso on-demand.
    public async Task<DemandPaymentNoticeResponse> DemandPaymentNoticeAsync(DemandPaymentNoticeRequest r)
    {
        var dati = r.DatiSpecificiServizioRequest;
        var d = await _ec.DemandAsync(r.IdPA, r.IdServizio, r.IdSoggettoServizio, dati.Amount, dati.Causale, dati.DebtorFullName);
        if (!d.Ok)
            return new DemandPaymentNoticeResponse { Outcome = StOutcome.KO, Fault = EcWrap(r.IdPA, d.Fault!) };
        return new DemandPaymentNoticeResponse
        {
            Outcome = StOutcome.OK,
            QrCode = new CtQrCode { FiscalCode = r.IdPA, NoticeNumber = d.Nav },
            PaymentAmount = d.AmountEuro, PaymentDescription = d.Description,
            FiscalCodePA = r.IdPA, CompanyName = d.CompanyName
        };
    }

    public async Task<ActivatePaymentNoticeRes> ActivatePaymentNoticeAsync(ActivatePaymentNoticeReq r)
    {
        string ofc = r.QrCode.FiscalCode, nav = r.QrCode.NoticeNumber;
        var p = await _ec.GetPaymentV2Async(ofc, nav, r.TransferType);
        if (!p.Ok)
            return new ActivatePaymentNoticeRes { Outcome = StOutcome.KO, Fault = EcWrap(ofc, p.Fault!) };

        // Emissione paymentToken: blocca il pagamento e collega il contesto (transfer inclusi) per il sendOutcome.
        var transfers = p.Transfers.Select(t => new TokenTransfer(t.IdTransfer, t.Amount, t.FiscalCodePA, t.Iban, t.Remittance, t.Category, t.Metadata)).ToList();
        string token = _tokens.Issue(new TokenCtx(ofc, nav, p.Iuv, p.AmountEuro, p.CompanyName, p.Description, null, null, transfers));
        return new ActivatePaymentNoticeRes
        {
            Outcome = StOutcome.OK,
            TotalAmount = p.AmountEuro,
            PaymentDescription = p.Description,
            FiscalCodePA = ofc,
            CompanyName = p.CompanyName,
            OfficeName = string.IsNullOrEmpty(p.OfficeName) ? null : p.OfficeName,
            PaymentToken = token,
            CreditorReferenceId = p.Iuv,
            TransferList = new CtTransferList
            {
                Transfer = p.Transfers.Select(t => new CtTransfer
                {
                    IdTransfer = t.IdTransfer, TransferAmount = t.Amount, FiscalCodePA = t.FiscalCodePA,
                    IBAN = t.Iban, RemittanceInformation = t.Remittance
                }).ToList()
            }
        };
    }

    // Notifica PSP -> Nodo (step 7-8): valida i token del carrello prima dell'esito.
    public Task<PspNotifyPaymentV2Response> PspNotifyPaymentV2Async(PspNotifyPaymentV2Request r)
    {
        var tokens = r.PaymentList.Select(p => p.PaymentToken).ToList();
        if (tokens.Count == 0 || tokens.Any(t => !_tokens.TryGet(t, out _)))
            return Task.FromResult(new PspNotifyPaymentV2Response { Outcome = StOutcome.KO, Fault = Fault("PPT_PAGAMENTO_SCONOSCIUTO", r.IdPSP) });
        return Task.FromResult(new PspNotifyPaymentV2Response { Outcome = StOutcome.OK });
    }

    // Esito unico per TUTTI i token del carrello (max 5). Una RT per ciascun avviso.
    public async Task<SendPaymentOutcomeV2Response> SendPaymentOutcomeV2Async(SendPaymentOutcomeV2Request r)
    {
        var tokens = r.PaymentTokens;
        if (tokens.Count == 0 || tokens.Any(t => !_tokens.TryGet(t, out _)))
            return new SendPaymentOutcomeV2Response { Outcome = StOutcome.KO, Fault = Fault("PPT_PAGAMENTO_SCONOSCIUTO", r.IdPSP) };

        if (!string.Equals(r.Outcome, "OK", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var t in tokens) _tokens.Remove(t);
            return new SendPaymentOutcomeV2Response { Outcome = StOutcome.OK };
        }

        var (idPsp, pspName) = _wisp.Select(r.IdPSP);
        decimal fee = r.Details?.Fee ?? 0m;
        foreach (var t in tokens)
        {
            _tokens.TryGet(t, out var ctx);
            string receiptId = Guid.NewGuid().ToString("N");
            var (ok, fault) = await _ec.SendRTAsync(ctx, pspName, fee, receiptId, idPsp,
                r.Details?.Payer?.UniqueIdentifierValue, r.Details?.Payer?.FullName);
            _tokens.Remove(t);
            if (!ok)
                return new SendPaymentOutcomeV2Response { Outcome = StOutcome.KO, Fault = EcWrap(ctx.Ofc, fault!) };
        }
        return new SendPaymentOutcomeV2Response { Outcome = StOutcome.OK };
    }

    // Il PSP invia il FlussoRiversamento firmato: il Nodo lo inoltra all'EC (GPD) per l'acquisizione.
    public async Task<NodoInviaFlussoRendicontazioneResponse> NodoInviaFlussoRendicontazioneAsync(NodoInviaFlussoRendicontazioneRequest r)
    {
        var xml = Encoding.UTF8.GetString(r.XmlRendicontazione);
        var client = _http.CreateClient("gpd");
        var content = new StringContent(xml, Encoding.UTF8, "application/xml");
        content.Headers.Add("Ocp-Apim-Subscription-Key", _subKey);
        var resp = await client.PostAsync("/fdr/nodo/flussoriversamento", content);
        return resp.IsSuccessStatusCode
            ? new NodoInviaFlussoRendicontazioneResponse { Outcome = StOutcome.OK }
            : new NodoInviaFlussoRendicontazioneResponse { Outcome = StOutcome.KO, Fault = Fault("PPT_RENDICONTAZIONE_KO", r.IdentificativoDominio) };
    }
}
