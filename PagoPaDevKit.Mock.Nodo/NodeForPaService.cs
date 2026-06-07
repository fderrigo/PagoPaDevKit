using System.Net.Http.Json;
using System.Text.Json;

namespace PagoPaDevKit.Mock.Nodo;

// Implementa nodeForPa leggendo i Flussi di Rendicontazione da GPD (REST org pull + FlussoRiversamento XML).
public class NodeForPaService : INodeForPa
{
    readonly IHttpClientFactory _http;
    readonly string _subKey;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public NodeForPaService(IHttpClientFactory http, IConfiguration cfg)
    { _http = http; _subKey = cfg["Nodo:SubscriptionKey"] ?? "dev-gpd-key"; }

    HttpClient Gpd()
    {
        var c = _http.CreateClient("gpd");
        if (!c.DefaultRequestHeaders.Contains("Ocp-Apim-Subscription-Key"))
            c.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", _subKey);
        return c;
    }

    record FlowItem(string Fdr, string PspId, int Revision, bool Published, DateTimeOffset FlowDate);
    record FlowsResp(List<FlowItem> Data);

    static CtFaultBean Fault(string code, string id) => new() { FaultCode = code, FaultString = code, Id = id };

    public async Task<NodoChiediElencoFlussiRendicontazioneRes> NodoChiediElencoFlussiRendicontazioneAsync(NodoChiediElencoFlussiRendicontazioneReq r)
    {
        var resp = await Gpd().GetAsync($"/organizations/{r.IdentificativoPA}/fdrs?size=1000");
        if (!resp.IsSuccessStatusCode)
            return new() { Outcome = StOutcome.KO, Fault = Fault("PPT_SINTASSI_XSD", r.IdentificativoPA) };
        var flows = await resp.Content.ReadFromJsonAsync<FlowsResp>(Json);
        var list = (flows?.Data ?? new()).Select(f => new CtIdRendicontazione
        {
            IdentificativoFlusso = f.Fdr, DataOraFlusso = f.FlowDate.UtcDateTime
        }).ToList();
        return new NodoChiediElencoFlussiRendicontazioneRes
        {
            Outcome = StOutcome.OK,
            ElencoFlussiRendicontazione = new CtElencoFlussiRendicontazione { IdRendicontazione = list },
            TotRestituiti = list.Count
        };
    }

    public async Task<NodoChiediFlussoRendicontazioneRes> NodoChiediFlussoRendicontazioneAsync(NodoChiediFlussoRendicontazioneReq r)
    {
        var lresp = await Gpd().GetAsync($"/organizations/{r.IdentificativoPA}/fdrs?size=1000");
        if (!lresp.IsSuccessStatusCode)
            return new() { Outcome = StOutcome.KO, Fault = Fault("PPT_SINTASSI_XSD", r.IdentificativoPA) };
        var flows = await lresp.Content.ReadFromJsonAsync<FlowsResp>(Json);
        var f = (flows?.Data ?? new()).FirstOrDefault(x => x.Fdr == r.IdentificativoFlusso);
        if (f is null)
            return new() { Outcome = StOutcome.KO, Fault = Fault("PPT_FLUSSO_SCONOSCIUTO", r.IdentificativoPA) };

        var xresp = await Gpd().GetAsync($"/organizations/{r.IdentificativoPA}/fdrs/{f.Fdr}/revisions/{f.Revision}/psps/{f.PspId}/flussoriversamento");
        if (!xresp.IsSuccessStatusCode)
            return new() { Outcome = StOutcome.KO, Fault = Fault("PPT_FLUSSO_SCONOSCIUTO", r.IdentificativoPA) };
        var xml = await xresp.Content.ReadAsByteArrayAsync();
        return new NodoChiediFlussoRendicontazioneRes { Outcome = StOutcome.OK, XmlRendicontazione = xml };
    }
}
