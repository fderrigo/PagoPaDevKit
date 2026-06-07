using System.Text;
using Microsoft.EntityFrameworkCore;

namespace PagoPaDevKit.Mock.Gpd;

// API FdR (pagopa-fdr): lato PSP (pubblicazione flusso) e lato Organization (recupero).
public static class FdrEndpoints
{
    public static void MapFdr(this WebApplication app)
    {
        var key = app.Configuration["Gpd:SubscriptionKey"] ?? "dev-gpd-key";
        IResult? Auth(HttpRequest r) => ApiGuards.CheckSubscriptionKey(r, key);

        // ============ PSP: pubblicazione flusso ============
        var psp = app.MapGroup("/psps/{pspId}");

        // createEmptyFlow
        psp.MapPost("/fdrs/{fdr}", async (string pspId, string fdr, CreateFlowRequest req, HttpRequest http, GpdDbContext db) =>
        {
            if (Auth(http) is { } a) return a;
            if (await db.Fdrs.AnyAsync(x => x.PspId == pspId && x.Name == fdr && !x.Published))
                return ApiGuards.Problem(409, "Conflict", $"Flow {fdr} already exists in CREATED status.");
            var now = DateTimeOffset.UtcNow;
            db.Fdrs.Add(new Fdr
            {
                Name = fdr, PspId = pspId, OrganizationId = req.Receiver.OrganizationId ?? req.Receiver.Id ?? "",
                Revision = 1, Published = false, Status = FdrStatus.Created,
                FdrDate = req.FdrDate, Regulation = req.Regulation, RegulationDate = req.RegulationDate,
                BicCodePouringBank = req.BicCodePouringBank,
                SenderType = req.Sender.Type, SenderId = req.Sender.Id, PspName = req.Sender.PspName,
                PspBrokerId = req.Sender.PspBrokerId, ChannelId = req.Sender.ChannelId,
                ReceiverId = req.Receiver.Id, OrganizationName = req.Receiver.OrganizationName,
                TotPayments = req.TotPayments, SumPayments = req.SumPayments, Created = now, Updated = now
            });
            await db.SaveChangesAsync();
            return Results.Ok(new { message = $"Flow {fdr} created." });
        });

        // addPayment
        psp.MapPut("/fdrs/{fdr}/payments/add", async (string pspId, string fdr, AddPaymentRequest req, HttpRequest http, GpdDbContext db) =>
        {
            if (Auth(http) is { } a) return a;
            var f = await LoadCreated(db, pspId, fdr);
            if (f is null) return ApiGuards.Problem(404, "Not Found", $"Created flow {fdr} not found.");
            foreach (var p in req.Payments)
                f.Payments.Add(new FdrPayment { Index = p.Index, Iuv = p.Iuv, Iur = p.Iur, IdTransfer = p.IdTransfer, Pay = p.Pay, PayStatus = p.PayStatus, PayDate = p.PayDate });
            f.Updated = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { message = $"{req.Payments.Count} payments added." });
        });

        // deletePayment
        psp.MapPut("/fdrs/{fdr}/payments/del", async (string pspId, string fdr, DeletePaymentRequest req, HttpRequest http, GpdDbContext db) =>
        {
            if (Auth(http) is { } a) return a;
            var f = await LoadCreated(db, pspId, fdr);
            if (f is null) return ApiGuards.Problem(404, "Not Found", $"Created flow {fdr} not found.");
            f.Payments.RemoveAll(x => req.Indexes.Contains(x.Index));
            f.Updated = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(new { message = "payments deleted." });
        });

        // publishFlow -> pubblica e porta le rate GPD a REPORTED
        psp.MapPost("/fdrs/{fdr}/publish", async (string pspId, string fdr, HttpRequest http, GpdDbContext db) =>
        {
            if (Auth(http) is { } a) return a;
            var f = await LoadCreated(db, pspId, fdr);
            if (f is null) return ApiGuards.Problem(404, "Not Found", $"Created flow {fdr} not found.");

            f.Published = true; f.Status = FdrStatus.Published; f.Updated = DateTimeOffset.UtcNow;

            // Rendicontazione: le rate pagate citate nel flusso passano a REPORTED.
            var positions = await GpdEndpoints.FullGraph(db).Where(x => x.OrganizationFiscalCode == f.OrganizationId).ToListAsync();
            var installments = positions.SelectMany(p => p.PaymentOptions).SelectMany(po => po.Installments).ToList();
            foreach (var pay in f.Payments)
            {
                var inst = installments.FirstOrDefault(i => i.Iuv == pay.Iuv);
                if (inst is { Status: InstallmentStatus.Paid }) inst.Status = InstallmentStatus.Reported;
            }
            await db.SaveChangesAsync();
            return Results.Ok(new { message = $"Flow {fdr} published.", revision = f.Revision });
        });

        // deleteExistingFlow (solo CREATED)
        psp.MapDelete("/fdrs/{fdr}", async (string pspId, string fdr, HttpRequest http, GpdDbContext db) =>
        {
            if (Auth(http) is { } a) return a;
            var f = await LoadCreated(db, pspId, fdr);
            if (f is null) return ApiGuards.Problem(404, "Not Found", $"Created flow {fdr} not found.");
            db.Fdrs.Remove(f);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        // GET flussi non pubblicati / pubblicati del PSP
        psp.MapGet("/created", async (string pspId, HttpRequest http, GpdDbContext db) =>
            Auth(http) ?? Results.Ok(await FlowsByStatus(db, pspId, published: false)));
        psp.MapGet("/published", async (string pspId, HttpRequest http, GpdDbContext db) =>
            Auth(http) ?? Results.Ok(await FlowsByStatus(db, pspId, published: true)));

        // ============ Organization: recupero flussi pubblicati ============
        var org = app.MapGroup("/organizations/{organizationId}");

        psp.MapGet("/published/fdrs/{fdr}/revisions/{revision}/organizations/{organizationId}/payments",
            async (string pspId, string fdr, int revision, string organizationId, HttpRequest http, GpdDbContext db,
                   int? page, int? size) =>
        {
            if (Auth(http) is { } a) return a;
            var f = await LoadPublished(db, pspId, fdr, organizationId);
            return f is null ? ApiGuards.Problem(404, "Not Found", "Flow not found.") : Results.Ok(Payments(f, page, size));
        });

        // getAllPublishedFlows
        org.MapGet("/fdrs", async (string organizationId, HttpRequest http, GpdDbContext db,
            int? page, int? size, string? pspId, DateTimeOffset? publishedGt) =>
        {
            if (Auth(http) is { } a) return a;
            int pg = page ?? 1, sz = Math.Clamp(size ?? 1000, 1, 1000);
            var all = await db.Fdrs.Where(x => x.OrganizationId == organizationId && x.Published).ToListAsync();
            if (!string.IsNullOrWhiteSpace(pspId)) all = all.Where(x => x.PspId == pspId).ToList();
            if (publishedGt is not null) all = all.Where(x => x.Updated > publishedGt).ToList();
            var ordered = all.OrderByDescending(x => x.Updated).ToList();
            var pageItems = ordered.Skip((pg - 1) * sz).Take(sz).Select(SummaryOf).ToList();
            return Results.Ok(new PaginatedFlowsResponse
            {
                Metadata = new MetadataDtoFdr { PageNumber = pg, PageSize = sz, TotPage = (int)Math.Ceiling(ordered.Count / (double)sz) },
                Count = ordered.Count, Data = pageItems
            });
        });

        // getSinglePublishedFlow
        org.MapGet("/fdrs/{fdr}/revisions/{revision}/psps/{pspId}",
            async (string organizationId, string fdr, int revision, string pspId, HttpRequest http, GpdDbContext db) =>
        {
            if (Auth(http) is { } a) return a;
            var f = await LoadPublished(db, pspId, fdr, organizationId);
            return f is null ? ApiGuards.Problem(404, "Not Found", "Flow not found.") : Results.Ok(SingleOf(f));
        });

        // getPaymentsFromPublishedFlow
        org.MapGet("/fdrs/{fdr}/revisions/{revision}/psps/{pspId}/payments",
            async (string organizationId, string fdr, int revision, string pspId, HttpRequest http, GpdDbContext db,
                   int? page, int? size) =>
        {
            if (Auth(http) is { } a) return a;
            var f = await LoadPublished(db, pspId, fdr, organizationId);
            return f is null ? ApiGuards.Problem(404, "Not Found", "Flow not found.") : Results.Ok(Payments(f, page, size));
        });

        // ---- FlussoRiversamento 1.0.4 XML firmato (recupero lato Organization) --------
        org.MapGet("/fdrs/{fdr}/revisions/{revision}/psps/{pspId}/flussoriversamento",
            async (string organizationId, string fdr, int revision, string pspId, HttpRequest http, GpdDbContext db, DevSigningCert signer) =>
        {
            if (Auth(http) is { } a) return a;
            var f = await LoadPublished(db, pspId, fdr, organizationId);
            if (f is null) return ApiGuards.Problem(404, "Not Found", "Flow not found.");
            var header = new FlussoHeader(f.Name, f.FdrDate, f.PspId, f.PspName ?? f.PspId, f.OrganizationId,
                f.OrganizationName ?? f.OrganizationId, f.BicCodePouringBank, f.RegulationDate ?? f.FdrDate,
                f.Regulation ?? f.Name, f.Payments.Count, f.Payments.Sum(p => p.Pay));
            var righe = f.Payments.OrderBy(p => p.Index).Select(p => new FlussoRiga(
                p.Iuv, p.Iur, p.Index, p.Pay, p.PayStatus == FdrPayStatus.Revoked ? "9" : "0", p.PayDate));
            var signed = FlussoRiversamentoXml.Sign(FlussoRiversamentoXml.Build(header, righe), signer.Certificate);
            return Results.Content(signed, "application/xml", Encoding.UTF8);
        });

        // ---- Ingest FlussoRiversamento firmato (Node-facing: nodoInviaFlussoRendicontazione) ----
        // Il PSP, tramite il Nodo, invia il flusso firmato; GPD verifica la firma, lo registra
        // pubblicato e porta a REPORTED le rate citate.
        app.MapPost("/fdr/nodo/flussoriversamento", async (HttpRequest http, GpdDbContext db) =>
        {
            if (Auth(http) is { } a) return a;
            string xml = await new StreamReader(http.Body).ReadToEndAsync();
            if (!FlussoRiversamentoXml.Verify(xml))
                return ApiGuards.Problem(400, "Bad Request", "Firma XML del FlussoRiversamento non valida.");
            var (h, righe) = FlussoRiversamentoXml.Parse(xml);
            string pspId = h.MittenteId, ofc = h.RiceventeId, fdr = h.IdentificativoFlusso;

            var existing = await db.Fdrs.Include(x => x.Payments).FirstOrDefaultAsync(x => x.PspId == pspId && x.Name == fdr);
            if (existing is not null) db.Fdrs.Remove(existing);
            var now = DateTimeOffset.UtcNow;
            var flow = new Fdr
            {
                Name = fdr, PspId = pspId, OrganizationId = ofc, Revision = 1, Published = true, Status = FdrStatus.Published,
                FdrDate = h.DataOraFlusso, RegulationDate = h.DataRegolamento, Regulation = h.IdRegolamento, BicCodePouringBank = h.Bic,
                SenderType = "B", SenderId = pspId, PspName = h.MittenteName, ReceiverId = ofc, OrganizationName = h.RiceventeName,
                TotPayments = h.NumeroPagamenti, SumPayments = h.ImportoTotale, Created = now, Updated = now
            };
            foreach (var r in righe)
                flow.Payments.Add(new FdrPayment { Index = r.Indice, Iuv = r.Iuv, Iur = r.Iur, IdTransfer = 1, Pay = r.Importo,
                    PayStatus = r.CodiceEsito == "9" ? FdrPayStatus.Revoked : FdrPayStatus.Executed, PayDate = r.DataEsito });
            db.Fdrs.Add(flow);

            var positions = await GpdEndpoints.FullGraph(db).Where(x => x.OrganizationFiscalCode == ofc).ToListAsync();
            var insts = positions.SelectMany(p => p.PaymentOptions).SelectMany(po => po.Installments).ToList();
            foreach (var r in righe)
            {
                var inst = insts.FirstOrDefault(i => i.Iuv == r.Iuv);
                if (inst is { Status: InstallmentStatus.Paid }) inst.Status = InstallmentStatus.Reported;
            }
            await db.SaveChangesAsync();
            return Results.Ok(new { message = $"Flusso {fdr} acquisito e pubblicato.", numeroPagamenti = righe.Count });
        });
    }

    // ---------------------- helper ----------------------
    static Task<Fdr?> LoadCreated(GpdDbContext db, string pspId, string fdr) =>
        db.Fdrs.Include(x => x.Payments).FirstOrDefaultAsync(x => x.PspId == pspId && x.Name == fdr && !x.Published);
    static Task<Fdr?> LoadPublished(GpdDbContext db, string pspId, string fdr, string orgId) =>
        db.Fdrs.Include(x => x.Payments).FirstOrDefaultAsync(x => x.PspId == pspId && x.Name == fdr && x.OrganizationId == orgId && x.Published);

    static async Task<object> FlowsByStatus(GpdDbContext db, string pspId, bool published)
    {
        var list = await db.Fdrs.Where(x => x.PspId == pspId && x.Published == published).ToListAsync();
        return new PaginatedFlowsResponse
        {
            Metadata = new MetadataDtoFdr { PageNumber = 1, PageSize = list.Count, TotPage = 1 },
            Count = list.Count, Data = list.Select(SummaryOf).ToList()
        };
    }

    static FlowSummary SummaryOf(Fdr f) => new()
    { Fdr = f.Name, PspId = f.PspId, Revision = f.Revision, Published = f.Published, FlowDate = f.FdrDate };

    static SingleFlowResponse SingleOf(Fdr f) => new()
    {
        Status = f.Status, Revision = f.Revision, Created = f.Created, Updated = f.Updated,
        Fdr = f.Name, FdrDate = f.FdrDate, Regulation = f.Regulation, RegulationDate = f.RegulationDate,
        BicCodePouringBank = f.BicCodePouringBank, Published = f.Published,
        Sender = new SenderDto { Type = f.SenderType, Id = f.SenderId, PspId = f.PspId, PspName = f.PspName, PspBrokerId = f.PspBrokerId, ChannelId = f.ChannelId },
        Receiver = new ReceiverDto { Id = f.ReceiverId, OrganizationId = f.OrganizationId, OrganizationName = f.OrganizationName },
        ComputedTotPayments = f.Payments.Count, ComputedSumPayments = f.Payments.Sum(p => p.Pay),
        TotPayments = f.TotPayments, SumPayments = f.SumPayments
    };

    static PaginatedPaymentsResponse Payments(Fdr f, int? page, int? size)
    {
        int pg = page ?? 1, sz = Math.Clamp(size ?? 1000, 1, 1000);
        var ordered = f.Payments.OrderBy(p => p.Index).ToList();
        var items = ordered.Skip((pg - 1) * sz).Take(sz).Select(p => new PaymentDto
        { Index = p.Index, Iuv = p.Iuv, Iur = p.Iur, IdTransfer = p.IdTransfer, Pay = p.Pay, PayStatus = p.PayStatus, PayDate = p.PayDate }).ToList();
        return new PaginatedPaymentsResponse
        {
            Metadata = new MetadataDtoFdr { PageNumber = pg, PageSize = sz, TotPage = (int)Math.Ceiling(ordered.Count / (double)sz) },
            Count = ordered.Count, Data = items
        };
    }
}
