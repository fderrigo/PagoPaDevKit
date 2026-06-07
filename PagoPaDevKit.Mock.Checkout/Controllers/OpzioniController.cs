using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PagoPaDevKit.Mock.Checkout.Models;

namespace PagoPaDevKit.Mock.Checkout.Controllers;

// Scelta dell'opzione di pagamento (unica soluzione / piano rateale / alternative).
// Il Checkout interroga GET /payment-options e fa scegliere al cittadino la rata da pagare.
public class OpzioniController : Controller
{
    readonly IHttpClientFactory _http;
    public OpzioniController(IHttpClientFactory http) => _http = http;

    [HttpGet("/opzioni")]
    public async Task<IActionResult> Index(string nav, string fiscalCode)
    {
        var resp = await _http.CreateClient("gpd").GetAsync($"/payment-options/organizations/{fiscalCode}/notices/{nav}");
        if (!resp.IsSuccessStatusCode)
            return View("~/Views/Pagamento/Esito.cshtml", new EsitoVm { Ok = false, Heading = "Opzioni non disponibili", Message = $"GET payment-options -> {(int)resp.StatusCode}." });

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var vm = new OpzioniVm
        {
            FiscalCode = fiscalCode,
            Iupd = root.GetProperty("iupd").GetString() ?? "",
            Status = root.GetProperty("status").GetString() ?? ""
        };
        foreach (var po in root.GetProperty("paymentOption").EnumerateArray())
        {
            var opt = new OpzioneVm
            {
                Description = po.GetProperty("description").GetString() ?? "",
                OptionType = po.GetProperty("optionType").GetString() ?? "",
                TotalAmount = po.GetProperty("totalAmount").GetInt64()
            };
            foreach (var i in po.GetProperty("installments").EnumerateArray())
                opt.Rate.Add(new RataVm
                {
                    Nav = i.GetProperty("nav").GetString() ?? "",
                    Amount = i.GetProperty("amount").GetInt64(),
                    DueDate = i.GetProperty("dueDate").GetDateTimeOffset(),
                    Status = i.GetProperty("status").GetString() ?? "",
                    Description = i.GetProperty("description").GetString() ?? ""
                });
            vm.Opzioni.Add(opt);
        }
        return View(vm);
    }
}
