using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using PagoPaDevKit.Mock.Gpd;
using Xunit;

namespace PagoPaDevKit.Tests;

// Primitive SOAP nodeForPa: l'EC chiede l'elenco e il singolo Flusso di Rendicontazione al Nodo.
[Collection("pagopa")]
public class NodeForPaTests
{
    const string Ofc = "77777777777";
    const string Psp = "BANCADEMO";
    const string PaNs = "http://pagopa-api.pagopa.gov.it/pa/paForNode.xsd";
    const string Nns = "http://pagopa-api.pagopa.gov.it/node/nodeForPsp.xsd";
    const string PaNodeNs = "http://pagopa-api.pagopa.gov.it/node/nodeForPa.xsd";
    readonly PagoPaFixture _fx;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public NodeForPaTests(PagoPaFixture fx) => _fx = fx;

    static string Iuv(int n) => n.ToString("D17");
    static string Nav(int n) => "3" + Iuv(n);
    static string V(XDocument d, string l) => d.Descendants().FirstOrDefault(e => string.Equals(e.Name.LocalName, l, StringComparison.OrdinalIgnoreCase))?.Value ?? "";

    async Task SeedAndPay(int seed)
    {
        var body = new
        {
            iupd = $"NFPA-{seed}", payStandIn = true, companyName = "Comune Demo",
            paymentOption = new[]
            {
                new
                {
                    description = "TARI", switchToExpired = false,
                    debtor = new { type = "F", fiscalCode = "RSSMRA80A01H501U", fullName = "Mario Rossi" },
                    installments = new[]
                    {
                        new { iuv = Iuv(seed), amount = 1050L, description = "TARI", dueDate = DateTimeOffset.UtcNow.AddDays(30),
                              transfer = new[] { new { idTransfer = "1", amount = 1050L, remittanceInformation = "TARI",
                                                       category = "9/0101002IM/", iban = "IT60X0542811101000000123456" } } }
                    }
                }
            }
        };
        (await _fx.GpdClient().PostAsJsonAsync($"/organizations/{Ofc}/debtpositions?toPublish=true", body, Json)).EnsureSuccessStatusCode();
        await _fx.CheckoutClient().PostAsync("/pagamento",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["nav"] = Nav(seed), ["fiscalCode"] = Ofc, ["pan"] = "4242424242424242" }));
    }

    async Task PublishSignedFlow(int seed, string fdr)
    {
        var cert = new DevSigningCert().Certificate;
        var header = new FlussoHeader(fdr, DateTimeOffset.UtcNow, Psp, "Banca Demo", Ofc, "Comune Demo",
            "BCITITMM", DateTimeOffset.UtcNow, "REG-" + seed, 1, 10.50m);
        var righe = new[] { new FlussoRiga(Iuv(seed), "IUR-" + seed, 1, 10.50m, "0", DateTimeOffset.UtcNow) };
        string signed = FlussoRiversamentoXml.Sign(FlussoRiversamentoXml.Build(header, righe), cert);
        string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(signed));
        string env = $@"<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:n=""{Nns}""><soap:Body>
<n:NodoInviaFlussoRendicontazione><n:request><n:IdentificativoPSP>{Psp}</n:IdentificativoPSP>
<n:IdentificativoIntermediarioPSP>{Psp}</n:IdentificativoIntermediarioPSP><n:IdentificativoCanale>{Psp}_01</n:IdentificativoCanale>
<n:IdentificativoFlusso>{fdr}</n:IdentificativoFlusso><n:DataOraFlusso>2026-06-04T10:00:00</n:DataOraFlusso>
<n:IdentificativoDominio>{Ofc}</n:IdentificativoDominio><n:XmlRendicontazione>{b64}</n:XmlRendicontazione>
</n:request></n:NodoInviaFlussoRendicontazione></soap:Body></soap:Envelope>";
        var c = new StringContent(env, Encoding.UTF8, "text/xml"); c.Headers.Add("SOAPAction", "\"nodoInviaFlussoRendicontazione\"");
        (await _fx.NodoClient().PostAsync("/nodo/node/nodeForPsp.svc", c)).EnsureSuccessStatusCode();
    }

    async Task<XDocument> PaSoap(string action, string inner)
    {
        string env = $@"<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:p=""{PaNodeNs}""><soap:Body>{inner}</soap:Body></soap:Envelope>";
        var c = new StringContent(env, Encoding.UTF8, "text/xml"); c.Headers.Add("SOAPAction", $"\"{action}\"");
        var r = await _fx.NodoClient().PostAsync("/nodo/node/nodeForPa.svc", c);
        return XDocument.Parse(await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ElencoAndChiediFlussoRendicontazione()
    {
        int seed = 96001;
        string fdr = "2026-06-04NFPA-1";
        await SeedAndPay(seed);
        await PublishSignedFlow(seed, fdr);

        // nodoChiediElencoFlussiRendicontazione
        var elenco = await PaSoap("nodoChiediElencoFlussiRendicontazione",
            $@"<p:NodoChiediElencoFlussiRendicontazione><p:request><p:IdentificativoPA>{Ofc}</p:IdentificativoPA></p:request></p:NodoChiediElencoFlussiRendicontazione>");
        Assert.Equal("OK", V(elenco, "Outcome"));
        Assert.Contains(elenco.Descendants().Where(e => e.Name.LocalName == "IdentificativoFlusso").Select(e => e.Value), v => v == fdr);

        // nodoChiediFlussoRendicontazione -> FlussoRiversamento firmato (base64)
        var chiedi = await PaSoap("nodoChiediFlussoRendicontazione",
            $@"<p:NodoChiediFlussoRendicontazione><p:request><p:IdentificativoPA>{Ofc}</p:IdentificativoPA><p:IdentificativoFlusso>{fdr}</p:IdentificativoFlusso></p:request></p:NodoChiediFlussoRendicontazione>");
        Assert.Equal("OK", V(chiedi, "Outcome"));
        string b64 = V(chiedi, "XmlRendicontazione");
        Assert.False(string.IsNullOrEmpty(b64));
        string xml = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
        Assert.Contains("FlussoRiversamento", xml);
        Assert.Contains(Iuv(seed), xml);
    }
}
