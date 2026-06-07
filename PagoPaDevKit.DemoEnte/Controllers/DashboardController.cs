using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PagoPaDevKit.Client;

namespace PagoPaDevKit.DemoEnte.Controllers;

// Home del console: riepilogo dovuti, ultimi movimenti, stato servizi.
public class DashboardController : Controller
{
    readonly EnteDbContext _db;
    readonly PagoPaGpdClient _gpd;
    readonly ServiceHealthService _health;
    readonly string _ofc, _companyName;

    public DashboardController(EnteDbContext db, PagoPaGpdClient gpd, ServiceHealthService health, IConfiguration cfg)
    {
        _db = db; _gpd = gpd; _health = health;
        _ofc = cfg["Ente:FiscalCode"] ?? "77777777777";
        _companyName = cfg["Ente:CompanyName"] ?? "Comune Demo";
    }

    [HttpGet("/")]
    public async Task<IActionResult> Index()
    {
        // Stato letto in PULL da GPD (allinea il DB locale).
        var list = (await _db.Dovuti.ToListAsync()).OrderByDescending(x => x.CreatedAt).ToList();
        foreach (var d in list)
        {
            try
            {
                var pos = await _gpd.GetDebtPositionAsync(_ofc, d.Iupd);
                var inst = pos?.PaymentOption.FirstOrDefault()?.Installments.FirstOrDefault();
                if (inst is not null) d.Status = MapStato.From(pos!.Status, inst.Status);
                if (pos is not null) PosShape.Apply(d, pos);
            }
            catch { /* GPD offline: mostra l'ultimo stato noto */ }
        }
        await _db.SaveChangesAsync();

        var receipts = new List<ReceiptDto>();
        foreach (var d in list.Where(x => x.Status == "PAGATO").Take(5))
        {
            try { var r = await _gpd.GetReceiptByIuvAsync(_ofc, d.Iuv); if (r is not null) receipts.Add(r); }
            catch { }
        }

        var vm = new DashboardVm
        {
            Totali = list.Count,
            DaPagare = list.Count(x => x.Status == "DA PAGARE"),
            Pagati = list.Count(x => x.Status == "PAGATO"),
            Scaduti = list.Count(x => x.Status == "SCADUTO"),
            UltimiDovuti = list.Take(5).ToList(),
            UltimeRicevute = receipts,
            Servizi = await _health.CheckAllAsync(),
            Ofc = _ofc, CompanyName = _companyName
        };
        return View(vm);
    }
}

// Riempie la forma (n. opzioni / n. rate) leggendo la posizione GPD.
public static class PosShape
{
    public static void Apply(Dovuto d, PaymentPositionResponse pos)
    {
        d.OptionCount = pos.PaymentOption.Count == 0 ? 1 : pos.PaymentOption.Count;
        d.InstallmentCount = pos.PaymentOption.Count == 0 ? 1 : pos.PaymentOption.Max(o => o.Installments.Count);
    }
}

// Mappa stato posizione/rata GPD -> etichetta italiana del gestionale.
public static class MapStato
{
    public static string From(string posStatus, string? instStatus) => instStatus switch
    {
        "PAID" or "REPORTED" or "PARTIALLY_REPORTED" => "PAGATO",
        "EXPIRED" => "SCADUTO",
        _ => posStatus is "PAID" or "PARTIALLY_PAID" ? "PAGATO"
            : posStatus == "UNPAYABLE" ? "SCADUTO" : "DA PAGARE"
    };
}
