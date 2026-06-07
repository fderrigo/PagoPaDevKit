namespace PagoPaDevKit.Mock.Checkout.Models;

// Esito deterministico delle carte di test (documentate nel README).
public enum CardOutcome { Ok, AuthDenied, Expired, Unknown }

public static class TestCards
{
    public static CardOutcome Classify(string? pan)
    {
        var digits = new string((pan ?? "").Where(char.IsDigit).ToArray());
        return digits switch
        {
            "4242424242424242" => CardOutcome.Ok,
            "4000000000000002" => CardOutcome.AuthDenied,
            "4000000000000069" => CardOutcome.Expired,
            _ => CardOutcome.Unknown
        };
    }
}
