using Microsoft.AspNetCore.Mvc;

namespace PagoPaDevKit.DemoEnte.ViewComponents;

// Voce di menu della sidebar.
public record NavItem(string Label, string Href, string Icon);
public record NavGroup(string? Label, List<NavItem> Items);

public class SidebarViewComponent : ViewComponent
{
    public static readonly List<NavGroup> Groups = new()
    {
        new(null, new() { new("Dashboard", "/", "bi-speedometer2") }),
        new("Test Ente", new()
        {
            new("Crea posizione", "/ente/crea", "bi-file-earmark-plus"),
            new("Posizioni debitorie", "/ente/posizioni", "bi-list-task"),
            new("Ricevute", "/ente/ricevute", "bi-receipt"),
            new("Log webhook", "/ente/webhook", "bi-broadcast-pin"),
            new("Stampa avvisi", "/ente/avvisi", "bi-printer"),
            new("Rendicontazione (FdR)", "/riconciliazione", "bi-folder2-open"),
        }),
        new("Test Cittadino", new()
        {
            new("Paga un avviso", "/cittadino/paga", "bi-credit-card"),
            new("Opzioni di pagamento", "/cittadino/opzioni", "bi-list-check"),
            new("Pagamento spontaneo", "/cittadino/spontaneo", "bi-hand-index"),
            new("Carte di test", "/cittadino/carte", "bi-credit-card-2-front"),
        }),
        new("Infrastruttura", new()
        {
            new("Stato servizi", "/infrastruttura", "bi-hdd-network"),
            new("Scenari di test", "/infrastruttura/scenari", "bi-play-circle"),
            new("Strumenti", "/infrastruttura/strumenti", "bi-tools"),
        }),
        new(null, new() { new("Documentazione", "/documentazione", "bi-journal-text") }),
    };

    public IViewComponentResult Invoke()
    {
        string path = "/" + (Request.Path.Value ?? "/").Trim('/');
        ViewData["CurrentPath"] = path.ToLowerInvariant();
        return View(Groups);
    }
}

public record EnvBadgeVm(bool IsMock, string Label, List<KeyValuePair<string, string>> BaseUrls);

public class EnvBadgeViewComponent : ViewComponent
{
    readonly IConfiguration _cfg;
    public EnvBadgeViewComponent(IConfiguration cfg) => _cfg = cfg;

    public IViewComponentResult Invoke()
    {
        string gpd = _cfg["PagoPaGpd:BaseUrl"] ?? "http://localhost:5001";
        bool isMock = !gpd.Contains("pagopa.it", StringComparison.OrdinalIgnoreCase);
        string label = (_cfg["Ambiente"] ?? (isMock ? "MOCK" : "UAT")).ToUpperInvariant();
        var urls = new List<KeyValuePair<string, string>>
        {
            new("GPD", gpd),
            new("Checkout", _cfg["Ente:CheckoutBaseUrl"] ?? ""),
            new("Nodo", _cfg["Ente:NodoBaseUrl"] ?? ""),
            new("ApiConfig", _cfg["ApiConfig:BaseUrl"] ?? ""),
        };
        return View(new EnvBadgeVm(isMock, label, urls));
    }
}

// Link verso le altre componenti frontend/API della solution (URL browser-facing da config).
public record ComponenteLink(string Label, string Descr, string Url, string Icon);

public class ComponentiViewComponent : ViewComponent
{
    readonly IConfiguration _cfg;
    public ComponentiViewComponent(IConfiguration cfg) => _cfg = cfg;

    public IViewComponentResult Invoke(string style = "dropdown")
    {
        string C(string key, string def) => _cfg[$"Componenti:{key}"] ?? def;
        var links = new List<ComponenteLink>
        {
            new("Checkout", "Gateway pagamento cittadino", _cfg["Ente:CheckoutBaseUrl"] ?? "http://localhost:5002", "bi-credit-card"),
            new("GPD · Swagger", "API posizioni debitorie v3", C("GpdSwagger", "http://localhost:5001/swagger"), "bi-braces"),
            new("ApiConfig", "Registro IBAN + tassonomia", C("ApiConfig", "http://localhost:5005"), "bi-hdd-network"),
            new("Nodo · WSDL", "SOAP nodeForPsp / nodeForPa", C("NodoWsdl", "http://localhost:5004/nodo/node/nodeForPsp.svc?wsdl"), "bi-diagram-3"),
        };
        return View(style == "footer" ? "Footer" : "Default", links);
    }
}
