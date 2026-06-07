using Microsoft.AspNetCore.Mvc;
using PagoPaDevKit.Mock.Checkout.Models;

namespace PagoPaDevKit.Mock.Checkout.Controllers;

// Landing del Checkout: scelta tra pagamento DA AVVISO e pagamento SPONTANEO.
public class HomeController : Controller
{
    readonly NodeForPspClient _node;
    public HomeController(NodeForPspClient node) => _node = node;

    [HttpGet("/")]
    public IActionResult Index() => View();

    // ---- Pagamento spontaneo: form servizio + dati ----
    [HttpGet("/spontaneo")]
    public IActionResult Spontaneo() => View();

    [HttpPost("/spontaneo")]
    public async Task<IActionResult> Spontaneo([FromForm] string fiscalCode, [FromForm] string idServizio,
        [FromForm] string debtorFiscalCode, [FromForm] string debtorName, [FromForm] string causale, [FromForm] string amountEuro)
    {
        // Client stupido: parsing tollerante, è il backend a validare.
        decimal.TryParse(amountEuro, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var amount);
        var d = await _node.DemandAsync(fiscalCode, idServizio, debtorFiscalCode, amount, causale, debtorName);
        if (!d.Ok)
            return View("~/Views/Pagamento/Esito.cshtml", new EsitoVm { Ok = false, Heading = "Avviso non generato", Message = $"Generazione spontanea fallita: {d.Fault?.Display}" });
        // Avviso generato on-demand -> prosegui sul flusso di pagamento normale.
        return Redirect($"/pagamento?nav={d.Nav}&fiscalCode={fiscalCode}");
    }
}
