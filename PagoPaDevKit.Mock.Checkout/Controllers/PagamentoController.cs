using Microsoft.AspNetCore.Mvc;
using PagoPaDevKit.Mock.Checkout.Models;

namespace PagoPaDevKit.Mock.Checkout.Controllers;

// Checkout = PSP/WISP. Caso d'uso "pagamento presso frontend EC": carrello (Carts API o legacy),
// activatePaymentNotice per avviso, pspNotifyPaymentV2, sendPaymentOutcomeV2 unico, redirect returnUrls.
[Route("pagamento")]
public class PagamentoController : Controller
{
    const int MaxAvvisi = 5;
    readonly NodeForPspClient _node;
    readonly CartStore _carts;
    readonly decimal _feeEuro;

    public PagamentoController(NodeForPspClient node, CartStore carts, IConfiguration cfg)
    {
        _node = node; _carts = carts;
        _feeEuro = (long.TryParse(cfg["Checkout:FeeCents"], out var f) ? f : 150) / 100m;
    }

    // GET /pagamento?cartId=…  oppure  ?nav=…&fiscalCode=…&returnUrl=…  (legacy)
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] string? cartId, [FromQuery] string[] nav, string? fiscalCode, string? returnUrl)
    {
        List<string> navs; string ofc;
        if (!string.IsNullOrEmpty(cartId))
        {
            var cart = _carts.Get(cartId);
            if (cart is null) return View("Esito", new EsitoVm { Ok = false, Heading = "Carrello scaduto", Message = "Carrello non trovato." });
            navs = cart.PaymentNotices.Select(n => n.NoticeNumber).Take(MaxAvvisi).ToList();
            ofc = cart.PaymentNotices.FirstOrDefault()?.FiscalCode ?? "";
        }
        else
        {
            navs = (nav ?? Array.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().Take(MaxAvvisi).ToList();
            ofc = fiscalCode ?? "";
        }
        if (navs.Count == 0)
            return View("Esito", new EsitoVm { Ok = false, Heading = "Avviso mancante", Message = "Nessun avviso (NAV) indicato." });

        var vm = new CartVm { FiscalCode = ofc, CartId = cartId, ReturnUrl = returnUrl };
        foreach (var n in navs)
        {
            var v = await _node.VerifyAsync(ofc, n);
            if (!v.Ok)
                return View("Esito", new EsitoVm { Ok = false, Heading = "Avviso non pagabile", Message = $"Avviso {n} non pagabile: {v.Fault?.Display}" });
            vm.Items.Add(new CartItem { Nav = n, CompanyName = v.CompanyName, Description = v.Description, Amount = v.AmountEuro });
        }
        return View(vm);
    }

    // POST /pagamento
    [HttpPost]
    public async Task<IActionResult> Index([FromForm] string? cartId, [FromForm] string[] nav,
        [FromForm] string? fiscalCode, [FromForm] string? returnUrl, [FromForm] string pan)
    {
        List<string> navs; string ofc; CartReturnUrls? urls = null;
        if (!string.IsNullOrEmpty(cartId))
        {
            var cart = _carts.Get(cartId);
            if (cart is null) return View("Esito", new EsitoVm { Ok = false, Heading = "Carrello scaduto", Message = "Carrello non trovato." });
            navs = cart.PaymentNotices.Select(n => n.NoticeNumber).Take(MaxAvvisi).ToList();
            ofc = cart.PaymentNotices.FirstOrDefault()?.FiscalCode ?? "";
            urls = cart.ReturnUrls;
        }
        else
        {
            navs = (nav ?? Array.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().Take(MaxAvvisi).ToList();
            ofc = fiscalCode ?? "";
        }

        var outcome = TestCards.Classify(pan);
        if (outcome != CardOutcome.Ok)
        {
            string msg = outcome switch
            {
                CardOutcome.AuthDenied => "Autorizzazione negata dalla banca.",
                CardOutcome.Expired => "Carta scaduta.",
                _ => "Carta non riconosciuta (usa una carta di test)."
            };
            return Done(false, urls, returnUrl, navs, ofc, new EsitoVm { Ok = false, Heading = "Pagamento non riuscito", Message = msg, Navs = navs, FiscalCode = ofc, ShowRetry = true });
        }

        var tokens = new List<string>();
        var items = new List<NodeForPspClient.NotifyItem>();
        decimal total = 0; string company = "";
        foreach (var n in navs)
        {
            var act = await _node.ActivateAsync(ofc, n, 0);
            if (!act.Ok)
                return View("Esito", new EsitoVm { Ok = false, Heading = "Pagamento non riuscito", Message = $"Attivazione avviso {n} fallita: {act.Fault?.Display}" });
            tokens.Add(act.PaymentToken); total += act.TotalAmount; company = act.CompanyName;
            items.Add(new NodeForPspClient.NotifyItem(act.PaymentToken, act.TotalAmount, act.CompanyName));
        }

        var (notified, nfault) = await _node.PspNotifyAsync(ofc, total, _feeEuro, items);
        if (!notified)
            return View("Esito", new EsitoVm { Ok = false, Heading = "Pagamento non riuscito", Message = $"Notifica pagamento fallita: {nfault?.Display}" });

        var (ok, fault) = await _node.SendOutcomeAsync(tokens, ok: true, _feeEuro, null, null);
        if (!ok)
            return View("Esito", new EsitoVm { Ok = false, Heading = "Pagamento non riuscito", Message = $"Invio esito fallito: {fault?.Display}" });

        return Done(true, urls, returnUrl, navs, ofc, new EsitoVm
        {
            Ok = true, Heading = "Pagamento riuscito", CompanyName = company,
            Navs = navs, FiscalCode = ofc, Amount = total, Fee = _feeEuro
        });
    }

    // Redirect agli URL di ritorno: Carts API (Ok/Error) oppure legacy returnUrl, altrimenti pagina Esito.
    IActionResult Done(bool ok, CartReturnUrls? urls, string? legacyReturnUrl, List<string> navs, string ofc, EsitoVm vm)
    {
        string? target = urls is not null
            ? (ok ? urls.ReturnOkUrl : urls.ReturnErrorUrl)
            : legacyReturnUrl;
        if (!string.IsNullOrWhiteSpace(target))
        {
            var q = $"outcome={(ok ? "OK" : "KO")}&fiscalCode={Uri.EscapeDataString(ofc)}" +
                    string.Concat(navs.Select(n => $"&nav={Uri.EscapeDataString(n)}"));
            var sep = target.Contains('?') ? "&" : "?";
            return Redirect($"{target}{sep}{q}");
        }
        return View("Esito", vm);
    }
}
