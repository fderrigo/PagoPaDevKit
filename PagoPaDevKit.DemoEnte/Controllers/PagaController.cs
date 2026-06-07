using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PagoPaDevKit.DemoEnte.Controllers;

// "Paga ora": l'EC (backend) crea il carrello su Checkout via Carts API (POST /carts) e
// rimanda il browser del cittadino al 302 restituito. Caso d'uso "pagamento presso frontend EC".
public class PagaController : Controller
{
    readonly EnteDbContext _db;
    readonly IHttpClientFactory _http;
    readonly string _ofc, _companyName, _checkoutBrowserBase, _returnUrl;

    public PagaController(EnteDbContext db, IHttpClientFactory http, IConfiguration cfg)
    {
        _db = db; _http = http;
        _ofc = cfg["Ente:FiscalCode"] ?? "77777777777";
        _companyName = cfg["Ente:CompanyName"] ?? "Comune Demo";
        _checkoutBrowserBase = cfg["Ente:CheckoutBaseUrl"] ?? "http://localhost:5002";
        _returnUrl = cfg["Ente:ReturnUrl"] ?? "http://localhost:5003/esito";
    }

    [HttpGet("/paga")]
    public async Task<IActionResult> Paga([FromQuery] string nav)
    {
        var d = await _db.Dovuti.FirstOrDefaultAsync(x => x.Nav == nav);
        if (d is null) return NotFound();

        var cart = new
        {
            paymentNotices = new[]
            {
                new { noticeNumber = d.Nav, fiscalCode = _ofc, amount = d.Amount, companyName = _companyName, description = d.Description, allCCP = false }
            },
            returnUrls = new { returnOkUrl = _returnUrl, returnCancelUrl = _returnUrl, returnErrorUrl = _returnUrl, returnWaitingUrl = _returnUrl },
            idCart = Guid.NewGuid().ToString("N")
        };

        // POST /carts lato server: il Checkout risponde 302 con la pagina di pagamento.
        var client = _http.CreateClient("checkout");
        var resp = await client.PostAsJsonAsync("/carts", cart);
        if (resp.StatusCode != System.Net.HttpStatusCode.Redirect && !resp.IsSuccessStatusCode)
            return View("Errore", $"Checkout /carts ha risposto {(int)resp.StatusCode}.");
        var location = resp.Headers.Location?.ToString() ?? "/";

        // Rimanda il browser del cittadino alla pagina del Checkout (base browser-facing).
        var sep = location.StartsWith("http") ? "" : _checkoutBrowserBase.TrimEnd('/');
        return Redirect($"{sep}{location}");
    }
}
