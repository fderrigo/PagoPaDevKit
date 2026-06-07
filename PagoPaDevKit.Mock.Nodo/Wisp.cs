using System.Collections.Concurrent;

namespace PagoPaDevKit.Mock.Nodo;

// WISP (shim): nel pagoPA storico il WISP smistava il pagamento verso il PSP scelto
// dal cittadino. Oggi è dismesso e il PSP/Checkout chiama direttamente il Nodo; qui un
// registro PSP minimale che il Nodo consulta per arricchire la ricevuta (denominazione PSP).
public class Wisp
{
    readonly Dictionary<string, string> _catalog;
    readonly string _defaultName;

    public Wisp(IConfiguration cfg)
    {
        _defaultName = cfg["Wisp:DefaultPspName"] ?? "Banca Demo S.p.A.";
        _catalog = new(StringComparer.OrdinalIgnoreCase)
        {
            ["BANCADEMO"] = "Banca Demo S.p.A.",
            ["PSPDEMO"] = "PSP Demo S.p.A."
        };
    }

    // Seleziona/risolve il PSP: nel flusso reale il citizen sceglie il PSP nel wallet.
    public (string idPsp, string pspName) Select(string? requestedIdPsp)
    {
        if (!string.IsNullOrWhiteSpace(requestedIdPsp))
            return (requestedIdPsp!, _catalog.TryGetValue(requestedIdPsp!, out var n) ? n : _defaultName);
        var first = _catalog.First();
        return (first.Key, first.Value);
    }
}

// Token di pagamento emessi all'activate, consumati al sendPaymentOutcome.
public record TokenTransfer(string IdTransfer, decimal Amount, string? FiscalCodePA, string? Iban, string Remittance, string Category, List<(string Key, string Value)> Metadata);
public record TokenCtx(string Ofc, string Nav, string Iuv, decimal AmountEuro, string CompanyName,
    string Description, string? PayerCf, string? PayerName, List<TokenTransfer> Transfers);

public class PaymentTokenStore
{
    readonly ConcurrentDictionary<string, TokenCtx> _tokens = new();
    public string Issue(TokenCtx ctx) { var t = Guid.NewGuid().ToString("N"); _tokens[t] = ctx; return t; }
    public bool TryGet(string token, out TokenCtx ctx) => _tokens.TryGetValue(token, out ctx!);
    public void Remove(string token) => _tokens.TryRemove(token, out _);
}
