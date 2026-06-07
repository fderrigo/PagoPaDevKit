using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using PagoPaDevKit.Mock.Gpd;
using Xunit;

namespace PagoPaDevKit.Tests;

// Test FlussoRiversamento 1.0.4 firmato: PSP -> Nodo (nodoInviaFlussoRendicontazione) ->
// GPD (verifica firma + acquisizione + REPORTED) -> recupero flusso firmato lato Organization.
[Collection("pagopa")]
public class FlussoRiversamentoTests
{
    const string Ofc = "77777777777";
    const string Psp = "BANCADEMO";
    const string PaNs = "http://pagopa-api.pagopa.gov.it/pa/paForNode.xsd";
    const string Nns = "http://pagopa-api.pagopa.gov.it/node/nodeForPsp.xsd";
    readonly PagoPaFixture _fx;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public FlussoRiversamentoTests(PagoPaFixture fx) => _fx = fx;

    static string Iuv(int n) => n.ToString("D17");
    static string Nav(int n) => "3" + Iuv(n);

    async Task SeedAndPay(int seed)
    {
        var body = new
        {
            iupd = $"FLX-{seed}", payStandIn = true, companyName = "Comune Demo",
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
                                                       category = "9/0101108TS/", iban = "IT60X0542811101000000123456" } } }
                    }
                }
            }
        };
        (await _fx.GpdClient().PostAsJsonAsync($"/organizations/{Ofc}/debtpositions?toPublish=true", body, Json)).EnsureSuccessStatusCode();
        string env = $@"<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:t=""{PaNs}""><soap:Body>
<t:PaSendRTV2><t:request><t:IdPA>{Ofc}</t:IdPA><t:IdBrokerPA>{Ofc}</t:IdBrokerPA><t:IdStation>S</t:IdStation>
<t:Receipt><t:ReceiptId>RX-{seed}</t:ReceiptId><t:NoticeNumber>{Nav(seed)}</t:NoticeNumber><t:FiscalCode>{Ofc}</t:FiscalCode>
<t:Outcome>OK</t:Outcome><t:CreditorReferenceId>{Iuv(seed)}</t:CreditorReferenceId><t:PaymentAmount>10.50</t:PaymentAmount>
<t:Description>TARI</t:Description><t:CompanyName>Comune Demo</t:CompanyName><t:PSPCompanyName>Banca Demo</t:PSPCompanyName>
<t:Fee>1.50</t:Fee></t:Receipt></t:request></t:PaSendRTV2></soap:Body></soap:Envelope>";
        var c = new StringContent(env, Encoding.UTF8, "text/xml"); c.Headers.Add("SOAPAction", "\"paSendRTV2\"");
        (await _fx.GpdClient(withKey: false).PostAsync("/gpd/nodo/paForNode.svc", c)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task SignedFlow_SentViaNode_IsAcquired_Reports_AndRetrievableSigned()
    {
        int seed = 6001;
        await SeedAndPay(seed);
        string fdr = "2026-06-04BANCADEMO-99";

        // Il PSP costruisce e FIRMA il FlussoRiversamento 1.0.4.
        var cert = new DevSigningCert().Certificate;
        var header = new FlussoHeader(fdr, DateTimeOffset.UtcNow, Psp, "Banca Demo S.p.A.", Ofc, "Comune Demo",
            "BCITITMM", DateTimeOffset.UtcNow, "REG-6001", 1, 10.50m);
        var righe = new[] { new FlussoRiga(Iuv(seed), "IUR-6001", 1, 10.50m, "0", DateTimeOffset.UtcNow) };
        string signed = FlussoRiversamentoXml.Sign(FlussoRiversamentoXml.Build(header, righe), cert);
        Assert.True(FlussoRiversamentoXml.Verify(signed)); // firma valida lato PSP

        string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(signed));

        // Invio via Nodo (nodoInviaFlussoRendicontazione).
        string env = $@"<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:n=""{Nns}""><soap:Body>
<n:NodoInviaFlussoRendicontazione><n:request>
<n:IdentificativoPSP>{Psp}</n:IdentificativoPSP><n:IdentificativoIntermediarioPSP>{Psp}</n:IdentificativoIntermediarioPSP>
<n:IdentificativoCanale>{Psp}_01</n:IdentificativoCanale><n:IdentificativoFlusso>{fdr}</n:IdentificativoFlusso>
<n:DataOraFlusso>2026-06-04T10:00:00</n:DataOraFlusso><n:IdentificativoDominio>{Ofc}</n:IdentificativoDominio>
<n:XmlRendicontazione>{b64}</n:XmlRendicontazione></n:request></n:NodoInviaFlussoRendicontazione></soap:Body></soap:Envelope>";
        var c = new StringContent(env, Encoding.UTF8, "text/xml"); c.Headers.Add("SOAPAction", "\"nodoInviaFlussoRendicontazione\"");
        var send = await _fx.NodoClient().PostAsync("/nodo/node/nodeForPsp.svc", c);
        var sendXml = await send.Content.ReadAsStringAsync();
        Assert.Contains("<Outcome>OK</Outcome>", sendXml);

        // Rata su GPD -> REPORTED.
        var dp = await _fx.GpdClient().GetFromJsonAsync<JsonElement>($"/organizations/{Ofc}/debtpositions/FLX-{seed}");
        Assert.Equal("REPORTED", dp.GetProperty("paymentOption")[0].GetProperty("installments")[0].GetProperty("status").GetString());

        // Recupero del flusso come FlussoRiversamento XML firmato.
        var get = await _fx.GpdClient().GetAsync($"/organizations/{Ofc}/fdrs/{fdr}/revisions/1/psps/{Psp}/flussoriversamento");
        Assert.Equal(System.Net.HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("application/xml", get.Content.Headers.ContentType!.MediaType);
        string retrieved = await get.Content.ReadAsStringAsync();
        Assert.True(FlussoRiversamentoXml.Verify(retrieved));      // firma del flusso recuperato valida
        Assert.Contains(Iuv(seed), retrieved);
        Assert.Contains("FlussoRiversamento", retrieved);
    }

    [Fact]
    public void TamperedSignature_Rejected()
    {
        var cert = new DevSigningCert().Certificate;
        var header = new FlussoHeader("TAMPER-1", DateTimeOffset.UtcNow, Psp, "Banca Demo", Ofc, "Comune Demo",
            null, DateTimeOffset.UtcNow, "REG-T", 1, 10.50m);
        var righe = new[] { new FlussoRiga(Iuv(6002), "IUR-T", 1, 10.50m, "0", DateTimeOffset.UtcNow) };
        string signed = FlussoRiversamentoXml.Sign(FlussoRiversamentoXml.Build(header, righe), cert);
        string tampered = signed.Replace("10.50", "99.99"); // manomissione importo
        Assert.False(FlussoRiversamentoXml.Verify(tampered));
    }
}
