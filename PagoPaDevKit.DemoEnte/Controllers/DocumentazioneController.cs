using Microsoft.AspNetCore.Mvc;

namespace PagoPaDevKit.DemoEnte.Controllers;

public class DocumentazioneController : Controller
{
    [HttpGet("/documentazione")]
    public IActionResult Index() => View();
}
