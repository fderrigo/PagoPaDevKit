using Microsoft.EntityFrameworkCore;

namespace PagoPaDevKit.Mock.Gpd;

// Implementazione delle primitive paForNode sui dati GPD.
// NB: gli importi SOAP sono in EURO (decimal); GPD li conserva in centesimi.
public class PaForNodeService : IPaForNode
{
    readonly GpdDbContext _db;
    readonly SpontaneousIuv _iuv;
    readonly IConfiguration _cfg;
    readonly ReceiptDeliverySender _delivery;
    public PaForNodeService(GpdDbContext db, SpontaneousIuv iuv, IConfiguration cfg, ReceiptDeliverySender delivery)
    { _db = db; _iuv = iuv; _cfg = cfg; _delivery = delivery; }

    static decimal Euro(long cents) => cents / 100m;
    static long Cents(decimal euro) => (long)Math.Round(euro * 100m);

    record Found(DebtPosition Position, PaymentOption Option, Installment Installment);

    async Task<Found?> Locate(string ofc, string nav)
    {
        var d = await GpdEndpoints.FullGraph(_db)
            .FirstOrDefaultAsync(x => x.OrganizationFiscalCode == ofc
                && x.PaymentOptions.Any(po => po.Installments.Any(i => i.Nav == nav)));
        if (d is null) return null;
        var po = d.PaymentOptions.First(p => p.Installments.Any(i => i.Nav == nav));
        var inst = po.Installments.First(i => i.Nav == nav);
        return new Found(d, po, inst);
    }

    // Fault con faultString/description ufficiali dal catalogo PAA.
    static CtFaultBean Fault(string code, string ofc) => PaaFault.Bean(code, ofc);

    // Verifica i requisiti di pagabilità; ritorna il fault o null se OK.
    static string? CheckPayable(Found f)
    {
        GpdEndpoints.RefreshExpiry(f.Position);
        if (f.Installment.Status == InstallmentStatus.Paid || f.Position.Status == DebtPositionStatus.Paid)
            return PaaFault.Duplicato;
        if (f.Position.Status == DebtPositionStatus.Unpayable || f.Installment.Status == InstallmentStatus.Expired)
            return PaaFault.Scaduto;
        if (f.Position.Status is not (DebtPositionStatus.Valid or DebtPositionStatus.PartiallyPaid))
            return PaaFault.Sconosciuto;
        // Opzioni alternative: se un'altra opzione di pagamento è già stata scelta (ha rate pagate),
        // questa opzione è annullata.
        var activeOption = f.Position.PaymentOptions.FirstOrDefault(po => po.Installments.Any(i => i.Status == InstallmentStatus.Paid));
        if (activeOption is not null && !ReferenceEquals(activeOption, f.Option))
            return PaaFault.Annullato;
        return null;
    }

    public async Task<PaVerifyPaymentNoticeRes> PaVerifyPaymentNoticeAsync(PaVerifyPaymentNoticeReq request)
    {
        string ofc = request.QrCode.FiscalCode, nav = request.QrCode.NoticeNumber;
        var f = await Locate(ofc, nav);
        if (f is null)
            return new() { Outcome = StOutcome.KO, Fault = Fault(PaaFault.Sconosciuto, ofc) };
        var bad = CheckPayable(f);
        if (bad is not null)
            return new() { Outcome = StOutcome.KO, Fault = Fault(bad, ofc) };

        var i = f.Installment;
        return new PaVerifyPaymentNoticeRes
        {
            Outcome = StOutcome.OK,
            PaymentList = new CtPaymentOptionsDescriptionListPA
            {
                PaymentOptionDescription =
                {
                    new CtPaymentOptionDescriptionPA
                    {
                        Amount = Euro(i.Amount), Options = "EQ",
                        DueDate = i.DueDate.UtcDateTime,
                        // allCCP: true se tutti i transfer sono su IBAN bancario (nessun postale/marca da bollo).
                        AllCCP = i.Transfers.All(t => !string.IsNullOrEmpty(t.Iban) && string.IsNullOrEmpty(t.PostalIban) && t.StampHashDocument is null),
                        DetailDescription = i.Description
                    }
                }
            },
            PaymentDescription = i.Description,
            FiscalCodePA = f.Position.OrganizationFiscalCode,
            CompanyName = f.Position.CompanyName,
            OfficeName = f.Position.OfficeName
        };
    }

    public async Task<PaGetPaymentV2Response> PaGetPaymentV2Async(PaGetPaymentV2Request request)
    {
        string ofc = request.QrCode.FiscalCode, nav = request.QrCode.NoticeNumber;
        var f = await Locate(ofc, nav);
        if (f is null)
            return new() { Outcome = StOutcome.KO, Fault = Fault(PaaFault.Sconosciuto, ofc) };
        var bad = CheckPayable(f);
        if (bad is not null)
            return new() { Outcome = StOutcome.KO, Fault = Fault(bad, ofc) };

        var i = f.Installment;
        return new PaGetPaymentV2Response
        {
            Outcome = StOutcome.OK,
            Data = new CtPaymentPAV2
            {
                CreditorReferenceId = i.Iuv,
                PaymentAmount = Euro(i.Amount),
                DueDate = i.DueDate.UtcDateTime,
                LastPayment = true,
                Description = i.Description,
                CompanyName = f.Position.CompanyName,
                OfficeName = f.Position.OfficeName,
                Debtor = new CtSubject
                {
                    UniqueIdentifier = new() { EntityUniqueIdentifierType = f.Option.DebtorType, EntityUniqueIdentifierValue = f.Option.DebtorFiscalCode },
                    FullName = f.Option.DebtorFullName,
                    StreetName = f.Option.StreetName, City = f.Option.City
                },
                TransferList = new CtTransferListPAV2(i.Transfers.Select(t => new CtTransferPAV2
                    {
                        IdTransfer = t.IdTransfer, TransferAmount = Euro(t.Amount),
                        FiscalCodePA = t.OrganizationFiscalCode ?? f.Position.OrganizationFiscalCode,
                        IBAN = string.IsNullOrEmpty(t.StampHashDocument) ? t.Iban : null,
                        // E-bollo 2.0: transfer marca da bollo -> richiesta MBD invece dell'IBAN.
                        RichiestaMarcaDaBollo = string.IsNullOrEmpty(t.StampHashDocument) ? null : new CtRichiestaMarcaDaBollo
                        {
                            TipoBollo = t.StampType ?? "01", HashDocumento = t.StampHashDocument!, ProvinciaResidenza = t.StampProvincialResidence ?? ""
                        },
                        RemittanceInformation = t.RemittanceInformation,
                        TransferCategory = t.Category,
                        // Metadata di riconciliazione inseriti dall'EC alla creazione, propagati in attivazione.
                        Metadata = t.TransferMetadata.Count == 0 ? null
                            : new CtMetadata(t.TransferMetadata.Select(m => new CtMapEntry { Key = m.Key, Value = m.Value ?? "" }))
                    }).ToList())
            }
        };
    }

    public async Task<PaSendRTV2Response> PaSendRTV2Async(PaSendRTV2Request request)
    {
        var r = request.Receipt;
        string ofc = string.IsNullOrWhiteSpace(r.FiscalCode) ? request.IdPA : r.FiscalCode;
        var f = await Locate(ofc, r.NoticeNumber);
        if (f is null)
            return new() { Outcome = StOutcome.KO, Fault = Fault(PaaFault.Sconosciuto, ofc) };
        if (f.Installment.Status == InstallmentStatus.Paid)
            return new() { Outcome = StOutcome.KO, Fault = Fault(PaaFault.Duplicato, ofc) };

        // Marca la rata pagata e ricalcola lo stato posizione:
        // PAID se UN'opzione è interamente pagata (unica soluzione o tutte le rate del piano),
        // altrimenti PARTIALLY_PAID (piano rateale in corso).
        f.Installment.Status = InstallmentStatus.Paid;
        bool anyOptionFullyPaid = f.Position.PaymentOptions.Any(po => po.Installments.All(i => i.Status == InstallmentStatus.Paid));
        f.Position.Status = anyOptionFullyPaid ? DebtPositionStatus.Paid : DebtPositionStatus.PartiallyPaid;

        // E-bollo 2.0: per ogni transfer "marca da bollo" emetti la marca da bollo digitale (MBD).
        foreach (var st in f.Installment.Transfers.Where(t => !string.IsNullOrEmpty(t.StampHashDocument)))
        {
            string idMarca = "01" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            string marcaXml = $"<marcaDaBollo><identificativo>{idMarca}</identificativo><tipoBollo>{st.StampType}</tipoBollo>" +
                              $"<hashDocumento>{st.StampHashDocument}</hashDocumento><provinciaResidenza>{st.StampProvincialResidence}</provinciaResidenza></marcaDaBollo>";
            _db.DigitalStamps.Add(new DigitalStamp
            {
                OrganizationFiscalCode = ofc, Iuv = f.Installment.Iuv,
                TipoBollo = st.StampType ?? "01", HashDocumento = st.StampHashDocument ?? "",
                ProvinciaResidenza = st.StampProvincialResidence ?? "", IdentificativoMarca = idMarca,
                MarcaBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(marcaXml)),
                IssuedAt = DateTimeOffset.UtcNow
            });
        }
        f.Position.PaymentDate = r.PaymentDateTime?.ToUniversalTime() ?? DateTimeOffset.UtcNow;

        // Metadata di riconciliazione propagati nella RT (transfer.metadata).
        var allMeta = r.TransferList?.Where(t => t.Metadata is not null).SelectMany(t => t.Metadata!).ToList();
        string? metaJson = allMeta is { Count: > 0 }
            ? System.Text.Json.JsonSerializer.Serialize(allMeta.Select(m => new { m.Key, m.Value }))
            : null;

        var receipt = new Receipt
        {
            ReceiptId = string.IsNullOrWhiteSpace(r.ReceiptId) ? Guid.NewGuid().ToString("N") : r.ReceiptId,
            MetadataJson = metaJson,
            Iuv = string.IsNullOrWhiteSpace(r.CreditorReferenceId) ? f.Installment.Iuv : r.CreditorReferenceId,
            Nav = r.NoticeNumber,
            OrganizationFiscalCode = ofc,
            DebtorFiscalCode = r.Debtor?.UniqueIdentifier?.EntityUniqueIdentifierValue ?? f.Option.DebtorFiscalCode,
            PaymentAmount = Cents(r.PaymentAmount),
            Fee = Cents(r.Fee),
            Outcome = r.Outcome,
            PspCompanyName = r.PSPCompanyName,
            IdPsp = r.IdPSP,
            IdChannel = r.IdChannel,
            PaymentMethod = r.PaymentMethod,
            PaymentDateTime = r.PaymentDateTime?.ToUniversalTime() ?? DateTimeOffset.UtcNow
        };
        _db.Receipts.Add(receipt);
        await _db.SaveChangesAsync();

        // Consegna alla stazione broadcast dell'EC (paSendRT push). Se fallisce -> NOTICE_PENDING (retry).
        if (_delivery.StationConfigured)
        {
            receipt.DeliveryAttempts = 1;
            receipt.DeliveryStatus = await _delivery.TryDeliverAsync(receipt)
                ? ReceiptDeliveryStatus.Delivered : ReceiptDeliveryStatus.NoticePending;
            await _db.SaveChangesAsync();
        }
        return new PaSendRTV2Response { Outcome = StOutcome.OK };
    }

    // Pagamento SPONTANEO: il cittadino non ha un avviso. L'EC genera l'avviso on-demand
    // (nuova posizione Origin=SPONTANEO, IUV lato EC) e restituisce qrCode + importo.
    public async Task<PaDemandPaymentNoticeResponse> PaDemandPaymentNoticeAsync(PaDemandPaymentNoticeRequest request)
    {
        string ofc = request.IdPA;
        var dati = request.DatiSpecificiServizioRequest;
        long amount = Cents(dati.Amount);
        if (amount <= 0)
            return new() { Outcome = StOutcome.KO, Fault = Fault(PaaFault.Semantica, ofc) };

        string iuv = _iuv.NextIuv(), nav = "3" + iuv;
        string company = _cfg["Gpd:SpontaneousCompanyName"] ?? "Ente Creditore";
        string iban = _cfg["Gpd:SpontaneousIban"] ?? "IT60X0542811101000000123456";
        var now = DateTimeOffset.UtcNow;

        var entity = new DebtPosition
        {
            Iupd = $"SPONT-{iuv}",
            OrganizationFiscalCode = ofc,
            CompanyName = company,
            Origin = PaymentOrigin.Spontaneo,
            InsertedDate = now,
            Status = DebtPositionStatus.Draft,
            PaymentOptions =
            {
                new PaymentOption
                {
                    Description = dati.Causale,
                    DebtorType = "F",
                    DebtorFiscalCode = request.IdSoggettoServizio,
                    DebtorFullName = dati.DebtorFullName ?? request.IdSoggettoServizio,
                    Installments =
                    {
                        new Installment
                        {
                            Iuv = iuv, Nav = nav, Amount = amount, Description = dati.Causale,
                            DueDate = now.AddDays(30), Status = InstallmentStatus.Unpaid,
                            Transfers =
                            {
                                new Transfer
                                {
                                    IdTransfer = "1", Amount = amount, OrganizationFiscalCode = ofc,
                                    RemittanceInformation = dati.Causale, Category = request.IdServizio, Iban = iban
                                }
                            }
                        }
                    }
                }
            }
        };
        GpdEndpoints.Publish(entity, now);
        _db.DebtPositions.Add(entity);
        await _db.SaveChangesAsync();

        return new PaDemandPaymentNoticeResponse
        {
            Outcome = StOutcome.OK,
            QrCode = new CtQrCode { FiscalCode = ofc, NoticeNumber = nav },
            PaymentList = new CtPaymentOptionsDescriptionListPA
            {
                PaymentOptionDescription = { new CtPaymentOptionDescriptionPA { Amount = dati.Amount, Options = "EQ", DueDate = now.AddDays(30).UtcDateTime } }
            },
            PaymentDescription = dati.Causale,
            FiscalCodePA = ofc,
            CompanyName = company
        };
    }
}
