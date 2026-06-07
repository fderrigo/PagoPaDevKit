using Microsoft.EntityFrameworkCore;

namespace PagoPaDevKit.Mock.Gpd;

// API GPD Debt Positions v3 (external). 6 endpoint REST, fedeli alla spec ufficiale.
public static class GpdEndpoints
{
    public static void MapGpd(this WebApplication app)
    {
        var key = app.Configuration["Gpd:SubscriptionKey"] ?? "dev-gpd-key";
        var grp = app.MapGroup("/organizations/{organizationFiscalCode}");

        // ---- POST createPosition (?toPublish=true) ------------------------------------
        grp.MapPost("/debtpositions", async (string organizationFiscalCode, PaymentPositionRequest req,
            bool? toPublish, HttpRequest http, GpdDbContext db) =>
        {
            var auth = ApiGuards.CheckSubscriptionKey(http, key);
            if (auth is not null) return auth;

            if (await db.DebtPositions.AnyAsync(d => d.OrganizationFiscalCode == organizationFiscalCode && d.Iupd == req.Iupd))
                return ApiGuards.Error(GpdErrors.UniqueViolation, organizationFiscalCode);

            var validation = Validate(req);
            if (validation is not null) return validation;

            var now = DateTimeOffset.UtcNow;
            var entity = new DebtPosition
            {
                Iupd = req.Iupd,
                OrganizationFiscalCode = organizationFiscalCode,
                InsertedDate = now,
                Status = DebtPositionStatus.Draft,
            };
            ApplyHeader(entity, req);
            entity.PaymentOptions = MapPaymentOptions(req, organizationFiscalCode);

            if (toPublish == true) Publish(entity, now);

            db.DebtPositions.Add(entity);
            await db.SaveChangesAsync();
            return Results.Created($"/organizations/{organizationFiscalCode}/debtpositions/{entity.Iupd}",
                PaymentPositionResponse.From(entity));
        });

        // ---- GET getOrganizationDebtPositionByIUPD ------------------------------------
        grp.MapGet("/debtpositions/{iupd}", async (string organizationFiscalCode, string iupd,
            HttpRequest http, GpdDbContext db) =>
        {
            var auth = ApiGuards.CheckSubscriptionKey(http, key);
            if (auth is not null) return auth;
            var d = await Load(db, organizationFiscalCode, iupd);
            if (d is null) return ApiGuards.Error(GpdErrors.NotFound, organizationFiscalCode, iupd);
            RefreshExpiry(d);
            return Results.Ok(PaymentPositionResponse.From(d));
        });

        // ---- GET getOrganizationDebtPositions (lista paginata, filtri v3) -------------
        grp.MapGet("/debtpositions", async (string organizationFiscalCode, HttpRequest http, GpdDbContext db,
            int? page, int? limit, DateTimeOffset? due_date_from, DateTimeOffset? due_date_to,
            DateTimeOffset? payment_date_from, DateTimeOffset? payment_date_to, string? status,
            string? orderby, string? ordering) =>
        {
            var auth = ApiGuards.CheckSubscriptionKey(http, key);
            if (auth is not null) return auth;

            int p = page ?? 0, lim = Math.Clamp(limit ?? 10, 1, 50);
            var q = FullGraph(db).Where(x => x.OrganizationFiscalCode == organizationFiscalCode);
            if (due_date_from is not null) q = q.Where(x => x.PaymentOptions.Any(po => po.Installments.Any(i => i.DueDate >= due_date_from)));
            if (due_date_to is not null) q = q.Where(x => x.PaymentOptions.Any(po => po.Installments.Any(i => i.DueDate <= due_date_to)));
            if (payment_date_from is not null) q = q.Where(x => x.PaymentDate >= payment_date_from);
            if (payment_date_to is not null) q = q.Where(x => x.PaymentDate <= payment_date_to);
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);

            // SQLite non ordina DateTimeOffset in SQL: ordino e pagino lato client (volumi mock).
            var all = await q.ToListAsync();
            bool desc = !string.Equals(ordering, "ASC", StringComparison.OrdinalIgnoreCase);
            Func<DebtPosition, object> keySel = (orderby?.ToUpperInvariant()) switch
            {
                "COMPANY_NAME" => x => x.CompanyName,
                "IUPD" => x => x.Iupd,
                "STATUS" => x => x.Status,
                _ => x => x.InsertedDate,
            };
            var ordered = desc ? all.OrderByDescending(keySel) : all.OrderBy(keySel);
            long total = all.Count;
            var items = ordered.Skip(p * lim).Take(lim).ToList();
            foreach (var d in items) RefreshExpiry(d);

            return Results.Ok(new PaymentPositionListResponse
            {
                PaymentPositionList = items.Select(PaymentPositionResponse.From).ToList(),
                PageInfo = new PageInfo
                {
                    Page = p, Limit = lim, ItemsFound = items.Count,
                    TotalItems = total, TotalPages = (int)Math.Ceiling(total / (double)lim)
                }
            });
        });

        // ---- PUT updatePosition -------------------------------------------------------
        grp.MapPut("/debtpositions/{iupd}", async (string organizationFiscalCode, string iupd,
            PaymentPositionRequest req, HttpRequest http, GpdDbContext db) =>
        {
            var auth = ApiGuards.CheckSubscriptionKey(http, key);
            if (auth is not null) return auth;
            var d = await Load(db, organizationFiscalCode, iupd);
            if (d is null) return ApiGuards.Error(GpdErrors.NotFound, organizationFiscalCode, iupd);
            if (d.Status is DebtPositionStatus.Paid or DebtPositionStatus.PartiallyPaid)
                return ApiGuards.Error(GpdErrors.NotUpdatable, organizationFiscalCode, iupd);

            var validation = Validate(req);
            if (validation is not null) return validation;

            db.PaymentOptions.RemoveRange(d.PaymentOptions);
            ApplyHeader(d, req);
            d.PaymentOptions = MapPaymentOptions(req, organizationFiscalCode);
            d.Status = DebtPositionStatus.Draft;
            await db.SaveChangesAsync();
            return Results.Ok(PaymentPositionResponse.From(d));
        });

        // ---- DELETE deletePosition ----------------------------------------------------
        grp.MapDelete("/debtpositions/{iupd}", async (string organizationFiscalCode, string iupd,
            HttpRequest http, GpdDbContext db) =>
        {
            var auth = ApiGuards.CheckSubscriptionKey(http, key);
            if (auth is not null) return auth;
            var d = await Load(db, organizationFiscalCode, iupd);
            if (d is null) return ApiGuards.Error(GpdErrors.NotFound, organizationFiscalCode, iupd);
            if (d.Status is DebtPositionStatus.Paid or DebtPositionStatus.PartiallyPaid)
                return ApiGuards.Error(GpdErrors.NotUpdatable, organizationFiscalCode, iupd);
            db.DebtPositions.Remove(d);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        // ---- POST publishPosition -----------------------------------------------------
        grp.MapPost("/debtpositions/{iupd}/publish", async (string organizationFiscalCode, string iupd,
            HttpRequest http, GpdDbContext db) =>
        {
            var auth = ApiGuards.CheckSubscriptionKey(http, key);
            if (auth is not null) return auth;
            var d = await Load(db, organizationFiscalCode, iupd);
            if (d is null) return ApiGuards.Error(GpdErrors.NotFound, organizationFiscalCode, iupd);
            if (d.Status is DebtPositionStatus.Paid or DebtPositionStatus.PartiallyPaid)
                return ApiGuards.Error(GpdErrors.NotPublishable, organizationFiscalCode, iupd);
            Publish(d, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
            return Results.Ok(PaymentPositionResponse.From(d));
        });
    }

    // ---------------------- helper riusati anche dal SOAP ----------------------

    public static IQueryable<DebtPosition> FullGraph(GpdDbContext db) =>
        db.DebtPositions
          .Include(x => x.PaymentOptions).ThenInclude(po => po.Installments).ThenInclude(i => i.Transfers).ThenInclude(t => t.TransferMetadata)
          .Include(x => x.PaymentOptions).ThenInclude(po => po.Installments).ThenInclude(i => i.InstallmentMetadata);

    public static Task<DebtPosition?> Load(GpdDbContext db, string ofc, string iupd) =>
        FullGraph(db).FirstOrDefaultAsync(x => x.OrganizationFiscalCode == ofc && x.Iupd == iupd);

    static void ApplyHeader(DebtPosition d, PaymentPositionRequest req)
    {
        d.PayStandIn = req.PayStandIn;
        d.CompanyName = req.CompanyName;
        d.OfficeName = req.OfficeName;
    }

    static List<PaymentOption> MapPaymentOptions(PaymentPositionRequest req, string ofc) =>
        req.PaymentOption.Select(po => new PaymentOption
        {
            Description = po.Description,
            ValidityDate = po.ValidityDate,
            RetentionDate = po.RetentionDate,
            SwitchToExpired = po.SwitchToExpired,
            DebtorType = po.Debtor.Type ?? "F",
            DebtorFiscalCode = po.Debtor.FiscalCode,
            DebtorFullName = po.Debtor.FullName,
            StreetName = po.Debtor.StreetName, CivicNumber = po.Debtor.CivicNumber, PostalCode = po.Debtor.PostalCode,
            City = po.Debtor.City, Province = po.Debtor.Province, Region = po.Debtor.Region, Country = po.Debtor.Country,
            Email = po.Debtor.Email, Phone = po.Debtor.Phone,
            Installments = po.Installments.Select(i => new Installment
            {
                Iuv = i.Iuv,
                Nav = string.IsNullOrWhiteSpace(i.Nav) ? GpdRules.NavFromIuv(i.Iuv) : i.Nav!,
                Amount = i.Amount,
                Description = i.Description,
                DueDate = i.DueDate,
                Fee = i.Fee,
                NotificationFee = i.NotificationFee,
                Status = InstallmentStatus.Unpaid,
                Transfers = i.Transfer.Select(t => new Transfer
                {
                    // organizationFiscalCode e required in v3: identifica l'ente beneficiario di
                    // questa quota (split). Se omesso, default all'EC primario (la PA del path).
                    IdTransfer = t.IdTransfer, Amount = t.Amount,
                    OrganizationFiscalCode = string.IsNullOrWhiteSpace(t.OrganizationFiscalCode) ? ofc : t.OrganizationFiscalCode,
                    RemittanceInformation = t.RemittanceInformation, Category = t.Category,
                    Iban = t.Iban, PostalIban = t.PostalIban, CompanyName = t.CompanyName,
                    StampHashDocument = t.Stamp?.HashDocument, StampType = t.Stamp?.StampType,
                    StampProvincialResidence = t.Stamp?.ProvincialResidence,
                    TransferMetadata = t.TransferMetadata.Select(m => new TransferMetadata { Key = m.Key, Value = m.Value }).ToList()
                }).ToList(),
                InstallmentMetadata = i.InstallmentMetadata.Select(m => new InstallmentMetadata { Key = m.Key, Value = m.Value }).ToList()
            }).ToList()
        }).ToList();

    // Pubblicazione: VALID se nessuna validityDate futura, altrimenti PUBLISHED.
    public static void Publish(DebtPosition d, DateTimeOffset now)
    {
        d.PublishDate = now;
        bool futureValidity = d.PaymentOptions.Any(po => po.ValidityDate is { } v && v > now);
        d.Status = futureValidity ? DebtPositionStatus.Published : DebtPositionStatus.Valid;
    }

    // EXPIRED/UNPAYABLE a read-time, fedele alla spec v3:
    //  - un'opzione scade SOLO se switchToExpired=true (default false -> resta pagabile dopo scadenza);
    //  - la scadenza e INCLUSIVA: il giorno stesso della dueDate l'avviso e ancora pagabile;
    //  - con opzioni alternative la posizione e UNPAYABLE solo se NESSUNA opzione resta pagabile.
    public static void RefreshExpiry(DebtPosition d)
    {
        if (d.Status is not (DebtPositionStatus.Valid or DebtPositionStatus.Published)) return;
        var today = DateTimeOffset.UtcNow.Date;

        bool OptionExpired(PaymentOption po) =>
            po.SwitchToExpired &&
            po.Installments.Count > 0 &&
            po.Installments.All(i => i.DueDate.Date < today);

        foreach (var po in d.PaymentOptions.Where(OptionExpired))
            foreach (var i in po.Installments)
                if (i.Status == InstallmentStatus.Unpaid) i.Status = InstallmentStatus.Expired;

        if (d.PaymentOptions.Count > 0 && d.PaymentOptions.All(OptionExpired))
            d.Status = DebtPositionStatus.Unpayable;
    }

    // Errori dati request -> AppError DEBT_POSITION_REQUEST_DATA_ERROR (400). Il 'detail' è il motivo.
    static IResult? Validate(PaymentPositionRequest req)
    {
        IResult Bad(string msg) => ApiGuards.Error(GpdErrors.RequestDataError, msg);
        if (req.PaymentOption.Count == 0) return Bad("At least one payment option is required");
        foreach (var po in req.PaymentOption)
        {
            if (!GpdRules.IsValidType(po.Debtor.Type)) return Bad("debtor.type must be 'F' or 'G'");
            if (string.IsNullOrWhiteSpace(po.Debtor.FiscalCode)) return Bad("debtor.fiscalCode is required");
            if (string.IsNullOrWhiteSpace(po.Debtor.FullName)) return Bad("debtor.fullName is required");
            if (po.Installments.Count == 0) return Bad("At least one installment is required");
            foreach (var i in po.Installments)
            {
                if (!GpdRules.IsValidIuv(i.Iuv)) return Bad($"Invalid IUV '{i.Iuv}': 17 digits expected");
                // dueDate obbligatoria e in formato valido (ISO 8601). Best practice EC.
                if (i.DueDate == default || i.DueDate.Year < 2000) return Bad("dueDate is required and must be a valid ISO 8601 date");
                if (i.Amount <= 0) return Bad("Installment amount must be greater than 0");
                if (i.Transfer.Count == 0) return Bad("At least one transfer is required");
                if (!GpdRules.AmountMatchesTransfers(i))
                    return Bad($"The sum of transfers ({i.Transfer.Sum(t => t.Amount)}) does not match the installment amount ({i.Amount})");
                foreach (var t in i.Transfer)
                {
                    if (!GpdRules.IsValidIdTransfer(t.IdTransfer)) return Bad($"Invalid idTransfer '{t.IdTransfer}': allowed '1'..'5'");
                    if (!GpdRules.HasValidAccredito(t)) return Bad("Each transfer requires either 'iban' or 'stamp' (mutually exclusive)");
                }
            }
        }
        return null;
    }
}
