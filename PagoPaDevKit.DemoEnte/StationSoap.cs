using System.Runtime.Serialization;
using System.ServiceModel;
using Microsoft.EntityFrameworkCore;

namespace PagoPaDevKit.DemoEnte;

// Stazione broadcast dell'EC: riceve la RT in push (paSendRTV2) dal Nodo/EC (modello async,
// opzione consigliata). Aggiorna il dovuto a PAGATO senza polling.
public static class StationNs { public const string Xsd = "http://pagopa-api.pagopa.gov.it/pa/paForNode.xsd"; }

[DataContract(Namespace = StationNs.Xsd)]
public enum StStationOutcome { [EnumMember] OK, [EnumMember] KO }

[DataContract(Namespace = StationNs.Xsd)]
public class StationReceipt
{
    [DataMember(Order = 0)] public string ReceiptId { get; set; } = "";
    [DataMember(Order = 1)] public string NoticeNumber { get; set; } = "";
    [DataMember(Order = 2)] public string CreditorReferenceId { get; set; } = "";
    [DataMember(Order = 3)] public decimal PaymentAmount { get; set; }
    [DataMember(Order = 4)] public decimal Fee { get; set; }
    [DataMember(Order = 5)] public string PSPCompanyName { get; set; } = "";
    [DataMember(Order = 6)] public string Outcome { get; set; } = "OK";
}

[DataContract(Namespace = StationNs.Xsd)]
public class StationReq { [DataMember(Order = 0)] public StationReceipt Receipt { get; set; } = new(); }

[DataContract(Namespace = StationNs.Xsd)]
public class StationRes { [DataMember(Order = 0)] public StStationOutcome Outcome { get; set; } }

[ServiceContract(Namespace = StationNs.Xsd, Name = "paForNode")]
public interface IStation
{
    [OperationContract(Action = "paSendRTV2")]
    Task<StationRes> PaSendRTV2Async(StationReq request);
}

public class StationService : IStation
{
    readonly EnteDbContext _db;
    public StationService(EnteDbContext db) => _db = db;

    public async Task<StationRes> PaSendRTV2Async(StationReq request)
    {
        var r = request.Receipt;
        var d = await _db.Dovuti.FirstOrDefaultAsync(x => x.Nav == r.NoticeNumber);
        var log = new WebhookLog
        {
            ReceivedAt = DateTimeOffset.UtcNow,
            Nav = r.NoticeNumber, ReceiptId = r.ReceiptId, Outcome = r.Outcome,
            Fee = (long)Math.Round(r.Fee * 100), PaymentAmount = r.PaymentAmount,
            PspCompanyName = r.PSPCompanyName, Matched = d is not null,
            Payload = $"ReceiptId={r.ReceiptId}; NAV={r.NoticeNumber}; CRId={r.CreditorReferenceId}; " +
                      $"importo={r.PaymentAmount}; fee={r.Fee}; psp={r.PSPCompanyName}; esito={r.Outcome}"
        };

        if (d is null)
        {
            log.ProcessResult = "KO — nessun dovuto con questo NAV";
            _db.WebhookLogs.Add(log);
            await _db.SaveChangesAsync();
            return new StationRes { Outcome = StStationOutcome.KO };
        }

        d.Status = "PAGATO";
        d.ReceiptId = r.ReceiptId;
        d.PspCompanyName = r.PSPCompanyName;
        d.Fee = (long)Math.Round(r.Fee * 100);
        d.PaidAt = DateTimeOffset.UtcNow;
        log.ProcessResult = "OK — dovuto aggiornato a PAGATO";
        _db.WebhookLogs.Add(log);
        await _db.SaveChangesAsync();
        return new StationRes { Outcome = StStationOutcome.OK };
    }
}
