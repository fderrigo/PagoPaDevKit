using System.Collections.Concurrent;

namespace PagoPaDevKit.Mock.Checkout.Models;

// Carts API del Checkout (eCommerce pagoPA): l'EC fa POST /carts e riceve un 302 verso la pagina di pagamento.
public class CartRequest
{
    public List<CartNotice> PaymentNotices { get; set; } = new(); // max 5
    public CartReturnUrls ReturnUrls { get; set; } = new();
    public string? EmailNotice { get; set; }
    public string? IdCart { get; set; }   // inserito nel paymentNote
}

public class CartNotice
{
    public string NoticeNumber { get; set; } = "";
    public string FiscalCode { get; set; } = "";
    public long Amount { get; set; }          // centesimi
    public string? CompanyName { get; set; }
    public string? Description { get; set; }
    public bool AllCCP { get; set; }
}

public class CartReturnUrls
{
    public string? ReturnOkUrl { get; set; }
    public string? ReturnCancelUrl { get; set; }
    public string? ReturnErrorUrl { get; set; }
    public string? ReturnWaitingUrl { get; set; }
}

// Store in-memory dei carrelli creati via POST /carts.
public class CartStore
{
    readonly ConcurrentDictionary<string, CartRequest> _carts = new();
    public string Save(CartRequest c) { var id = Guid.NewGuid().ToString("N"); _carts[id] = c; return id; }
    public CartRequest? Get(string id) => _carts.TryGetValue(id, out var c) ? c : null;
}
