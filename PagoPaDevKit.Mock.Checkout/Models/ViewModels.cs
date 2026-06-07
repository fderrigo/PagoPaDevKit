namespace PagoPaDevKit.Mock.Checkout.Models;

// Voce del carrello (un avviso).
public class CartItem
{
    public string Nav { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
}

// Carrello di pagamento (1..5 avvisi, come il caso d'uso 'pagamento presso frontend EC').
public class CartVm
{
    public List<CartItem> Items { get; set; } = new();
    public string FiscalCode { get; set; } = "";
    public string? ReturnUrl { get; set; }   // flusso legacy (un solo returnUrl)
    public string? CartId { get; set; }       // flusso Carts API
    public decimal Total => Items.Sum(i => i.Amount);
}

// Opzioni di pagamento (scelta cittadino).
public class OpzioniVm
{
    public string FiscalCode { get; set; } = "";
    public string Iupd { get; set; } = "";
    public string Status { get; set; } = "";
    public List<OpzioneVm> Opzioni { get; set; } = new();
}
public class OpzioneVm
{
    public string Description { get; set; } = "";
    public string OptionType { get; set; } = "";
    public long TotalAmount { get; set; }
    public List<RataVm> Rate { get; set; } = new();
}
public class RataVm
{
    public string Nav { get; set; } = "";
    public long Amount { get; set; }
    public DateTimeOffset DueDate { get; set; }
    public string Status { get; set; } = "";
    public string Description { get; set; } = "";
}

// Esito generico (successo / KO carta / errore avviso).
public class EsitoVm
{
    public bool Ok { get; set; }
    public string Heading { get; set; } = "";
    public string Message { get; set; } = "";
    public List<string> Navs { get; set; } = new();
    public string? FiscalCode { get; set; }
    public string? CompanyName { get; set; }
    public decimal? Amount { get; set; }
    public decimal? Fee { get; set; }
    public bool ShowRetry { get; set; }
}
