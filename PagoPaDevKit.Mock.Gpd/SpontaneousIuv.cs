namespace PagoPaDevKit.Mock.Gpd;

// Generatore IUV lato EC per i pagamenti SPONTANEI (l'avviso è creato on-demand).
// IUV 17 cifre: segregation code (2) + progressivo (15). NAV = "3" + IUV.
public class SpontaneousIuv
{
    readonly int _segregation;
    long _counter;

    public SpontaneousIuv(IConfiguration cfg)
    {
        _segregation = cfg.GetValue("Gpd:SpontaneousSegregation", 47);
        // Base progressivo "casuale" senza Date.Now: derivata dall'hash del processo.
        _counter = Math.Abs((long)Environment.TickCount64) % 1_000_000_000_000L;
    }

    public string NextIuv()
    {
        long n = System.Threading.Interlocked.Increment(ref _counter);
        return _segregation.ToString("D2") + n.ToString("D15");
    }

    public string NextNav() => "3" + NextIuv();
}
