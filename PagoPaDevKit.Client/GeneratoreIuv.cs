namespace PagoPaDevKit.Client;

// =====================================================================
// Helper di generazione IUV/NAV.
//
// Regole pagoPA (modello IUV a 17 cifre, aux digit 3):
//  - L'IUV identifica univocamente il versamento presso l'ente creditore.
//  - Qui usiamo lo schema: [segregation code 2 cifre][progressivo 13 cifre][check 2 cifre].
//    Il segregation code è assegnato all'ente in fase di adesione e instrada il
//    pagamento; il check è un controllo mod 93 sulle 15 cifre precedenti.
//  - Il NAV (Numero Avviso, "codice avviso") = aux digit "3" + IUV (totale 18 cifre).
//    L'aux digit 3 indica che l'IUV è generato lato ente (modello a 17 cifre).
//
// NOTA: il calcolo del check qui è plausibile ma semplificato; l'ente reale usa
// l'algoritmo del proprio gestionale. Il mock GPD valida solo lunghezza/formato,
// quindi qualsiasi IUV a 17 cifre numeriche è accettato sia dal mock sia da UAT.
// =====================================================================
public class GeneratoreIuv
{
    readonly int _segregationCode;
    long _progressivo;

    public GeneratoreIuv(int segregationCode = 47, long progressivoIniziale = 1)
    {
        if (segregationCode is < 0 or > 99)
            throw new ArgumentOutOfRangeException(nameof(segregationCode), "Segregation code: 2 cifre (00-99).");
        _segregationCode = segregationCode;
        _progressivo = progressivoIniziale;
    }

    // Genera un IUV a 17 cifre incrementando il progressivo interno.
    public string NextIuv()
    {
        string seg = _segregationCode.ToString("D2");
        string prog = (_progressivo++).ToString("D13");
        string body = seg + prog;                 // 15 cifre
        string check = (CheckMod93(body)).ToString("D2");
        return body + check;                      // 17 cifre
    }

    // NAV = aux digit "3" + IUV.
    public static string NavFromIuv(string iuv)
    {
        if (iuv.Length != 17 || !iuv.All(char.IsDigit))
            throw new ArgumentException("IUV deve essere di 17 cifre numeriche.", nameof(iuv));
        return "3" + iuv;
    }

    public string NextNav() => NavFromIuv(NextIuv());

    static int CheckMod93(string digits)
    {
        // Somma pesata semplice -> resto mod 93 (00..92). Sufficiente per il mock.
        long acc = 0;
        foreach (var c in digits) acc = (acc * 10 + (c - '0')) % 93;
        return (int)acc;
    }
}
