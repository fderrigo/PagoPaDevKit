using Microsoft.AspNetCore.Mvc;
using PagoPaDevKit.Client;

namespace PagoPaDevKit.DemoEnte.Controllers;

// Registro IBAN dell'ente (su ApiConfig). Gestito dalla pagina Infrastruttura > Strumenti.
public class IbanController : Controller
{
    readonly ApiConfigClient _apiConfig;
    readonly string _ofc, _companyName;

    public IbanController(ApiConfigClient apiConfig, IConfiguration cfg)
    {
        _apiConfig = apiConfig;
        _ofc = cfg["Ente:FiscalCode"] ?? "77777777777";
        _companyName = cfg["Ente:CompanyName"] ?? "Comune Demo";
    }

    [HttpGet("/ibans")]
    public IActionResult Index() => Redirect("/infrastruttura/strumenti");

    [HttpPost("/ibans")]
    public async Task<IActionResult> Crea([FromForm] string iban, [FromForm] string? description, [FromForm] string? label)
    {
        var dto = new IbanDto
        {
            Iban = iban?.Replace(" ", "").ToUpperInvariant() ?? "",
            CompanyName = _companyName, Description = description, IsActive = true,
            ValidityDate = DateTimeOffset.UtcNow,
            Labels = string.IsNullOrWhiteSpace(label) ? new() : new() { new IbanLabelDto { Name = label } }
        };
        try { await _apiConfig.CreateIbanAsync(_ofc, dto); TempData["Toast"] = "IBAN aggiunto al registro."; }
        catch (PagoPaApiException ex) { TempData["ToastKind"] = "danger"; TempData["Toast"] = $"ApiConfig (HTTP {ex.StatusCode}): {ex.ResponseBody}"; }
        return Redirect("/infrastruttura/strumenti");
    }

    [HttpPost("/ibans/{ibanValue}/delete")]
    public async Task<IActionResult> Elimina(string ibanValue)
    {
        try { await _apiConfig.DeleteIbanAsync(_ofc, ibanValue); TempData["Toast"] = "IBAN eliminato."; }
        catch (PagoPaApiException ex) { TempData["ToastKind"] = "danger"; TempData["Toast"] = $"Eliminazione fallita: HTTP {ex.StatusCode}."; }
        return Redirect("/infrastruttura/strumenti");
    }
}
