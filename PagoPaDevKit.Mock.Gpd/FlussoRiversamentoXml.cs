using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PagoPaDevKit.Mock.Gpd;

// Certificato dev self-signed per firmare i Flussi di Rendicontazione (XML-DSig).
// In produzione la firma è apposta dal PSP con il proprio certificato.
public class DevSigningCert
{
    public X509Certificate2 Certificate { get; }
    public DevSigningCert()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=PagoPaDevKit Dev FdR Signer", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        // Date fisse: nessuna dipendenza da DateTime.Now (riproducibilità).
        Certificate = req.CreateSelfSigned(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
                                           new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }
}

// Modello di testata + righe per costruire/parsare il FlussoRiversamento.
public record FlussoHeader(string IdentificativoFlusso, DateTimeOffset DataOraFlusso, string MittenteId,
    string MittenteName, string RiceventeId, string RiceventeName, string? Bic, DateTimeOffset DataRegolamento,
    string IdRegolamento, long NumeroPagamenti, decimal ImportoTotale);
public record FlussoRiga(string Iuv, string Iur, long Indice, decimal Importo, string CodiceEsito, DateTimeOffset DataEsito);

// Costruzione, firma (enveloped XML-DSig) e parsing del FlussoRiversamento 1.0.4.
public static class FlussoRiversamentoXml
{
    public const string Ns = "http://www.digitpa.gov.it/schemas/2011/Pagamenti/";
    static readonly XNamespace P = Ns;

    public static string Build(FlussoHeader h, IEnumerable<FlussoRiga> righe)
    {
        string D(decimal d) => d.ToString("0.00", CultureInfo.InvariantCulture);
        var doc = new XElement(P + "FlussoRiversamento",
            new XElement(P + "versioneOggetto", "1.0"),
            new XElement(P + "identificativoFlusso", h.IdentificativoFlusso),
            new XElement(P + "dataOraFlusso", h.DataOraFlusso.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss")),
            new XElement(P + "identificativoUnivocoRegolamento", h.IdRegolamento),
            new XElement(P + "dataRegolamento", h.DataRegolamento.UtcDateTime.ToString("yyyy-MM-dd")),
            new XElement(P + "istitutoMittente",
                new XElement(P + "identificativoUnivocoMittente",
                    new XElement(P + "tipoIdentificativoUnivoco", "B"),
                    new XElement(P + "codiceIdentificativoUnivoco", h.MittenteId)),
                new XElement(P + "denominazioneMittente", h.MittenteName)),
            h.Bic is null ? null : new XElement(P + "codiceBicBancaDiRiversamento", h.Bic),
            new XElement(P + "istitutoRicevente",
                new XElement(P + "identificativoUnivocoRicevente",
                    new XElement(P + "tipoIdentificativoUnivoco", "G"),
                    new XElement(P + "codiceIdentificativoUnivoco", h.RiceventeId)),
                new XElement(P + "denominazioneRicevente", h.RiceventeName)),
            new XElement(P + "numeroTotalePagamenti", h.NumeroPagamenti),
            new XElement(P + "importoTotalePagamenti", D(h.ImportoTotale)),
            righe.Select(r => new XElement(P + "datiSingoliPagamenti",
                new XElement(P + "identificativoUnivocoVersamento", r.Iuv),
                new XElement(P + "identificativoUnivocoRiscossione", r.Iur),
                new XElement(P + "indiceDatiSingoloPagamento", r.Indice),
                new XElement(P + "singoloImportoPagato", D(r.Importo)),
                new XElement(P + "codiceEsitoSingoloPagamento", r.CodiceEsito),
                new XElement(P + "dataEsitoSingoloPagamento", r.DataEsito.UtcDateTime.ToString("yyyy-MM-dd")))));
        return new XDeclaration("1.0", "UTF-8", null) + "\n" + doc.ToString(SaveOptions.DisableFormatting);
    }

    // Firma enveloped XML-DSig; il certificato firmatario è incluso in KeyInfo/X509Data.
    public static string Sign(string xml, X509Certificate2 cert)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);
        using var key = cert.GetRSAPrivateKey()!;
        var signed = new SignedXml(doc) { SigningKey = key };
        var reference = new Reference { Uri = "" };
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigC14NTransform());
        signed.AddReference(reference);
        var ki = new KeyInfo();
        ki.AddClause(new KeyInfoX509Data(cert));
        signed.KeyInfo = ki;
        signed.ComputeSignature();
        doc.DocumentElement!.AppendChild(doc.ImportNode(signed.GetXml(), true));
        return doc.OuterXml;
    }

    // Verifica la firma usando il certificato incluso nel documento.
    public static bool Verify(string signedXml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(signedXml);
        var nodes = doc.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl);
        if (nodes.Count == 0) return false;
        var signed = new SignedXml(doc);
        signed.LoadXml((XmlElement)nodes[0]!);
        var certEl = doc.GetElementsByTagName("X509Certificate")[0]?.InnerText;
        if (certEl is null) return false;
        var cert = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(certEl));
        using var pub = cert.GetRSAPublicKey()!;
        return signed.CheckSignature(pub);
    }

    public static (FlussoHeader header, List<FlussoRiga> righe) Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Root!;
        string S(XElement e, string n) => e.Element(P + n)?.Value ?? "";
        decimal Dn(XElement e, string n) => decimal.TryParse(S(e, n), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;
        var mitt = root.Element(P + "istitutoMittente")!;
        var ric = root.Element(P + "istitutoRicevente")!;
        var header = new FlussoHeader(
            S(root, "identificativoFlusso"),
            DateTimeOffset.TryParse(S(root, "dataOraFlusso"), out var dof) ? dof : default,
            mitt.Element(P + "identificativoUnivocoMittente")!.Element(P + "codiceIdentificativoUnivoco")!.Value,
            S(mitt, "denominazioneMittente"),
            ric.Element(P + "identificativoUnivocoRicevente")!.Element(P + "codiceIdentificativoUnivoco")!.Value,
            S(ric, "denominazioneRicevente"),
            root.Element(P + "codiceBicBancaDiRiversamento")?.Value,
            DateTimeOffset.TryParse(S(root, "dataRegolamento"), out var dr) ? dr : default,
            S(root, "identificativoUnivocoRegolamento"),
            long.TryParse(S(root, "numeroTotalePagamenti"), out var np) ? np : 0,
            Dn(root, "importoTotalePagamenti"));
        var righe = root.Elements(P + "datiSingoliPagamenti").Select(e => new FlussoRiga(
            S(e, "identificativoUnivocoVersamento"), S(e, "identificativoUnivocoRiscossione"),
            long.TryParse(S(e, "indiceDatiSingoloPagamento"), out var i) ? i : 0,
            Dn(e, "singoloImportoPagato"), S(e, "codiceEsitoSingoloPagamento"),
            DateTimeOffset.TryParse(S(e, "dataEsitoSingoloPagamento"), out var de) ? de : default)).ToList();
        return (header, righe);
    }
}
