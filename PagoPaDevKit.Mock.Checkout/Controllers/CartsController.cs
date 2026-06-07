using Microsoft.AspNetCore.Mvc;
using PagoPaDevKit.Mock.Checkout.Models;

namespace PagoPaDevKit.Mock.Checkout.Controllers;

// Carts API (eCommerce pagoPA): l'EC fa POST /carts (paymentNotices max 5 + returnUrls)
// e riceve un 302 verso la pagina di pagamento del Checkout.
[ApiController]
public class CartsController : ControllerBase
{
    readonly CartStore _store;
    public CartsController(CartStore store) => _store = store;

    [HttpPost("/carts")]
    public IActionResult Carts([FromBody] CartRequest req)
    {
        if (req.PaymentNotices.Count is 0 or > 5)
            return BadRequest(new { title = "Invalid cart", detail = "paymentNotices: da 1 a 5 avvisi." });
        var id = _store.Save(req);
        // 302: il browser viene rediretto alla pagina di pagamento del Checkout.
        return Redirect($"/pagamento?cartId={id}");
    }
}
