using PagoPaDevKit.Client;

namespace PagoPaDevKit.DemoEnte;

public class HomeVm
{
    public List<Dovuto> Dovuti { get; set; } = new();
    public string CompanyName { get; set; } = "";
    public string CheckoutBaseUrl { get; set; } = "";
    public string ReturnUrl { get; set; } = "";
    public string Ofc { get; set; } = "";
    public List<TaxonomyDto> TipiDovuto { get; set; } = new();
    public List<IbanDto> Ibans { get; set; } = new();
}

// Esito ricevuto dal Checkout via returnUrl.
public class EsitoEnteVm
{
    public string Outcome { get; set; } = "";
    public List<string> Navs { get; set; } = new();
}

// Riconciliazione contabile (mock del lato banca/quadratura, non emulabile dalla piattaforma).
public class RiconciliazioneVm
{
    public string CompanyName { get; set; } = "";
    public string Ofc { get; set; } = "";
    public string PspId { get; set; } = "";
    // KPI riepilogo
    public int RtCount { get; set; }
    public long RtTotaleCent { get; set; }        // incassato (ricevute)
    public int FdrCount { get; set; }
    public decimal FdrTotale { get; set; }        // rendicontato (FdR, euro)
    public decimal RiversatoTotale { get; set; }  // riversato banca (MOCK, euro)
    public int RtDaRendicontare { get; set; }     // ricevute non ancora in un FdR
    public bool TuttoQuadrato { get; set; }
    // FdR reali arricchiti
    public List<FdrRow> Fdr { get; set; } = new();
    // Quadratura per giornata (RT ↔ FdR ↔ riversamento)
    public List<QuadraturaGiorno> Giorni { get; set; } = new();
}

public class FdrRow
{
    public string Fdr { get; set; } = "";
    public string PspId { get; set; } = "";
    public int Revision { get; set; }
    public DateTimeOffset FlowDate { get; set; }
    public long NumPagamenti { get; set; }
    public decimal Importo { get; set; }
}

public class QuadraturaGiorno
{
    public DateTime Data { get; set; }
    public int RtCount { get; set; }
    public long RtImportoCent { get; set; }
    public int FdrCount { get; set; }
    public decimal FdrImporto { get; set; }
    public decimal Riversato { get; set; }        // MOCK = rendicontato
    public List<QuadraturaRiga> Pagamenti { get; set; } = new();
    public bool Pending => RtCount > FdrCount;     // RT non ancora rendicontati
    public bool Quadrato => !Pending;              // rendicontato == incassato
}

public class QuadraturaRiga
{
    public string Iuv { get; set; } = "";
    public string Debitore { get; set; } = "";
    public long ImportoCent { get; set; }
    public bool Incassato { get; set; } = true;    // RT presente
    public bool Rendicontato { get; set; }         // presente in un FdR
    public string? Fdr { get; set; }               // flusso che lo contiene
}

public class FdrDettaglioVm
{
    public FdrDetail Detail { get; set; } = new();
    public List<FdrPaymentRow> Payments { get; set; } = new();
    public string Ofc { get; set; } = "";
    public int Revision { get; set; } = 1;
    public string PspId { get; set; } = "";
}

public class FlussoMock
{
    public string IdentificativoFlusso { get; set; } = "";
    public string IdentificativoRegolamento { get; set; } = "";
    public DateTimeOffset DataRegolamento { get; set; }
    public string Bic { get; set; } = "";
    public long ImportoTotale { get; set; }     // centesimi (rendicontato)
    public long ImportoRiversato { get; set; }  // centesimi (accredito banca, mock)
    public int NumeroPagamenti { get; set; }
    public bool Quadrato => ImportoTotale == ImportoRiversato;
    public List<ReceiptSummary> Pagamenti { get; set; } = new();
}

public class RicevutaVm
{
    public Dovuto Dovuto { get; set; } = new();
    public ReceiptDto? Receipt { get; set; }
}

public class IbanAdminVm
{
    public List<IbanDto> Ibans { get; set; } = new();
    public string CompanyName { get; set; } = "";
    public string Ofc { get; set; } = "";
}

// Avviso di pagamento pagoPA (stampa).
public class AvvisoVm
{
    public string CompanyName { get; set; } = "";
    public string Ofc { get; set; } = "";
    public string CodiceSia { get; set; } = "";
    public Dovuto Dovuto { get; set; } = new();
    public string QrPngBase64 { get; set; } = "";
    public string QrPayload { get; set; } = "";
}

// ===== Partials =====
public record StatoBadge(string Stato);
public class DovutiTableVm
{
    public List<Dovuto> Dovuti { get; set; } = new();
    public string Ofc { get; set; } = "";
    public bool Compact { get; set; }   // nascondi alcune colonne (dashboard)
}
public record JsonAccordionVm(string Id, string Title, string Json);

// ===== Dashboard =====
public class DashboardVm
{
    public int Totali { get; set; }
    public int DaPagare { get; set; }
    public int Pagati { get; set; }
    public int Scaduti { get; set; }
    public List<Dovuto> UltimiDovuti { get; set; } = new();
    public List<ReceiptDto> UltimeRicevute { get; set; } = new();
    public List<ServiceHealth> Servizi { get; set; } = new();
    public string Ofc { get; set; } = "";
    public string CompanyName { get; set; } = "";
}

// ===== Test Ente =====
public class CreaDovutoVm
{
    public List<TaxonomyDto> TipiDovuto { get; set; } = new();
    public List<IbanDto> Ibans { get; set; } = new();                          // IBAN dell'ente di piattaforma
    public Dictionary<string, List<IbanDto>> IbansByEnte { get; set; } = new(); // CF ente -> suoi IBAN (split)
    public List<EnteBeneficiarioVm> Enti { get; set; } = new();
    public string CompanyName { get; set; } = "";
    public string Ofc { get; set; } = "";
    // esito creazione
    public string? CreatedNav { get; set; }
    public string? CreatedIuv { get; set; }
    public string? ErrorMessage { get; set; }
}

// Ente beneficiario di un transfer (organizationFiscalCode). Piattaforma = l'EC stesso.
public record EnteBeneficiarioVm(string Cf, string Nome, bool IsPlatform);

public class PosizioniVm
{
    public List<Dovuto> Dovuti { get; set; } = new();
    public string Ofc { get; set; } = "";
    public string? Stato { get; set; }   // filtro
    public string? Q { get; set; }       // ricerca NAV/CF
}

public class DettaglioVm
{
    public Dovuto Dovuto { get; set; } = new();
    public PaymentPositionResponse? Posizione { get; set; }
    public string RawJson { get; set; } = "";
    public string Ofc { get; set; } = "";
}

public class RicevuteVm
{
    public List<ReceiptSummary> Ricevute { get; set; } = new();
    public string Ofc { get; set; } = "";
}

public class WebhookVm
{
    public List<WebhookLog> Logs { get; set; } = new();
}

public class AvvisiListVm
{
    public List<Dovuto> Dovuti { get; set; } = new();
    public string Ofc { get; set; } = "";
}

// ===== Test Cittadino =====
public class CarteVm
{
    public List<TestCards.TestCard> Carte { get; set; } = new();
    public string? UltimoNav { get; set; }
    public string Ofc { get; set; } = "";
}

public class PagaCittadinoVm
{
    public int Step { get; set; } = 1;            // 1 dati, 2 riepilogo, 3 carta, 4 esito
    public string Ofc { get; set; } = "";
    public string Nav { get; set; } = "";
    public string FiscalCode { get; set; } = "";
    public string? UltimoNav { get; set; }
    // step 2 riepilogo (da verify)
    public string CompanyName { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal AmountEuro { get; set; }
    public decimal FeeEuro { get; set; }
    // step 4 esito
    public bool? Ok { get; set; }
    public string? Message { get; set; }
    public string? ReceiptId { get; set; }
    public string? FaultCode { get; set; }
    public List<TestCards.TestCard> Carte { get; set; } = new();
}

public class OpzioniCittadinoVm
{
    public string Ofc { get; set; } = "";
    public string Nav { get; set; } = "";
    public string? Status { get; set; }
    public string? RawJson { get; set; }
    public List<OpzioneRata> Rate { get; set; } = new();
    public string? Error { get; set; }
}
public class OpzioneRata
{
    public string Nav { get; set; } = "";
    public string Description { get; set; } = "";
    public long Amount { get; set; }
    public DateTimeOffset? DueDate { get; set; }
    public string Status { get; set; } = "";
}

public class SpontaneoVm
{
    public string Ofc { get; set; } = "";
    public List<TaxonomyDto> Servizi { get; set; } = new();
    public string? ResultNav { get; set; }
    public string? Error { get; set; }
}

// ===== Infrastruttura =====
public class StatoServiziVm
{
    public List<ServiceHealth> Servizi { get; set; } = new();
}
public class ScenariVm
{
    public List<ScenarioInfo> Scenari { get; set; } = new();
}
public class StrumentiVm
{
    public List<IbanDto> Ibans { get; set; } = new();
    public string Ofc { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public int SegregationCode { get; set; }
}
