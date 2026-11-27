using System.Text.Json;
using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using St = ERP.Core.Models.Accounting.TailoringOrderStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// أمر التفصيل. القواعد:
/// <list type="bullet">
/// <item>المسودة وحدها تُعدَّل وتُحذف، وتُطبع عرض سعر. كل الأرقام من الخادم: السعر المقترح = الأساسي + فروق التصميم + أمتار قماش المحل ×
/// سعره الصافي؛ المخالف والخصم بصلاحية «خصم يدوي»، والضريبة من الصنف الخدمي للنوع.</item>
/// <item>التصميم من خيارات النوع الفعّالة (الإلزامي حاضر، والقيمة من قيمه الفعّالة)، والقياس لقطة النسخة الحالية لملف العميل نفسه وبالنوع نفسه.</item>
/// <item>التأكيد يتطلب قياساً لكل تشكيلة وصنفاً خدمياً للنوع، وينشئ قطعة لكل وحدة من الكمية برمز مستقل.</item>
/// <item>الدفعة المقدمة (اعتماد) مرة واحدة لأمر قائم، لا تتجاوز الإجمالي ولا تقل عن حد الإعداد: فاتورة ضريبية نظامية مرحّلة مرجعها
/// <c>tailoring_advance</c>، صافيها على حساب «دفعات مقدمة من عملاء الخياطة» لا الإيراد. إلغاؤها بمرتجع نظامي ما لم يُسترد منها شيء.</item>
/// <item>الإلغاء بسبب لأمر لم يبدأ إنتاجه وبلا دفعة قائمة؛ والإرجاع للمسودة كذلك ويحذف القطع.</item>
/// </list>
/// </summary>
public class TailoringOrderService : ITailoringOrderService
{
    public const string AdvanceReferenceType = "tailoring_advance";
    private const int MaxLines = 50, MaxPiecesPerLine = 500;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _db;
    private readonly ISalesInvoiceService _sales;
    private readonly ISalesReturnService _returns;
    private readonly INumberSequenceService _numbers;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;
    private readonly IAuditService _audit;

    public TailoringOrderService(ErpDbContext db, ISalesInvoiceService sales, ISalesReturnService returns, INumberSequenceService numbers,
        ITransactionRunner tx, ICurrentUser user, IPermissionService permissions, IAuditService audit)
    {
        _db = db; _sales = sales; _returns = returns; _numbers = numbers; _tx = tx; _user = user; _permissions = permissions; _audit = audit;
    }

    private string? By => _user.Name ?? _user.Email;

    // ---------- القراءة ----------

    public Task<DevExtreme.AspNet.Data.ResponseModel.LoadResult> LoadAsync(DataSourceLoadOptions options, CancellationToken ct = default)
    {
        var rows = _db.Set<TailoringOrder>().AsNoTracking().Select(o => new TailoringOrderRowDto
        {
            Id = o.Id, OrderNumber = o.OrderNumber, OrderDate = o.OrderDate, ExpectedDeliveryDate = o.ExpectedDeliveryDate, CustomerId = o.CustomerId,
            CustomerName = o.CustomerName, CustomerPhone = o.CustomerPhone, Status = o.Status, Priority = o.Priority, PieceCount = o.PieceCount,
            GrandTotal = o.GrandTotal, AdvanceAmount = o.AdvanceAmount, Remaining = o.GrandTotal - o.AdvanceAmount, AdvanceInvoiceNumber = o.AdvanceInvoiceNumber,
        });
        return EntityLoader.LoadRowsAsync(rows, options, nameof(TailoringOrderRowDto.Id), ct,
            EntityLoader.Desc(nameof(TailoringOrderRowDto.OrderDate)), EntityLoader.Desc(nameof(TailoringOrderRowDto.OrderNumber)));
    }

    public async Task<TailoringOrderDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var o = await _db.Set<TailoringOrder>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(Messages.TailoringOrderNotFound);
        var lines = await _db.Set<TailoringOrderLine>().AsNoTracking().Where(l => l.OrderId == id).OrderBy(l => l.SortOrder).ToListAsync(ct);
        var garments = await _db.Set<TailoringGarment>().AsNoTracking().Where(g => g.OrderId == id).OrderBy(g => g.Code).ToListAsync(ct);
        var history = await _db.Set<TailoringOrderStatusHistory>().AsNoTracking().Where(h => h.OrderId == id).OrderBy(h => h.ChangedAt).ToListAsync(ct);
        var dto = await ToDtoAsync(o, lines, ct);
        dto.Garments = garments.Select(g => new TailoringGarmentDto
        {
            Id = g.Id, LineId = g.LineId, PieceNo = g.PieceNo, Code = g.Code, Status = g.Status, ReadyAt = g.ReadyAt, DeliveredAt = g.DeliveredAt, DeliveryId = g.DeliveryId,
        }).ToList();
        dto.History = history.Select(h => new TailoringOrderHistoryDto
        {
            FromStatus = h.FromStatus, ToStatus = h.ToStatus, Note = h.Note, ChangedBy = h.ChangedBy, ChangedAt = h.ChangedAt,
        }).ToList();
        return dto;
    }

    public async Task<TailoringOrderDto> PreviewAsync(SaveTailoringOrderDto r, Guid? id, CancellationToken ct = default)
    {
        var o = new TailoringOrder { OrderNumber = string.Empty };
        if (id is Guid existing)
            o = await _db.Set<TailoringOrder>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == existing, ct) ?? throw new NotFoundException(Messages.TailoringOrderNotFound);
        var lines = await BuildAsync(o, r, ct);
        return await ToDtoAsync(o, lines, ct);
    }

    // ---------- المسودة ----------

    public Task<TailoringOrderDto> CreateAsync(SaveTailoringOrderDto r, CancellationToken ct = default)
        => RunAndGetAsync(async token =>
        {
            var o = new TailoringOrder { Status = St.Draft, CreatedBy = By, CreatedByUserId = _user.UserId };
            var lines = await BuildAsync(o, r, token);
            o.OrderNumber = await _numbers.AssignAsync(new DocumentNumberRequest
            {
                Key = "tailoring_order", DefaultPrefix = "TO-", WarehouseId = o.WarehouseId, Date = o.OrderDate,
                Exists = (n, tk) => _db.Set<TailoringOrder>().AnyAsync(x => x.OrderNumber == n, tk),
            }, token);
            _db.Add(o);
            await _db.SaveChangesAsync(token);
            foreach (var l in lines) { l.OrderId = o.Id; _db.Add(l); }
            AddHistory(o, null, St.Draft, null);
            await _db.SaveChangesAsync(token);
            await LogAsync("TAILORING_ORDER_CREATED", o, $"{o.CustomerName} • {o.GrandTotal:0.##}", token);
            return o.Id;
        }, ct);

    public Task<TailoringOrderDto> UpdateAsync(Guid id, SaveTailoringOrderDto r, CancellationToken ct = default)
        => RunAndGetAsync(async token =>
        {
            var o = await LoadAsync(id, token);
            if (o.Status != St.Draft) throw new ConflictException(Messages.TailoringOrderNotDraft);
            var lines = await BuildAsync(o, r, token);
            _db.RemoveRange(await _db.Set<TailoringOrderLine>().Where(l => l.OrderId == id).ToListAsync(token));
            await _db.SaveChangesAsync(token);
            foreach (var l in lines) { l.OrderId = o.Id; _db.Add(l); }
            await _db.SaveChangesAsync(token);
            await LogAsync("TAILORING_ORDER_UPDATED", o, $"{o.GrandTotal:0.##}", token);
            return o.Id;
        }, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var o = await LoadAsync(id, token);
            if (o.Status != St.Draft) throw new ConflictException(Messages.TailoringOrderNotDraft);
            _db.RemoveRange(await _db.Set<TailoringOrderLine>().Where(l => l.OrderId == id).ToListAsync(token));
            _db.RemoveRange(await _db.Set<TailoringOrderStatusHistory>().Where(h => h.OrderId == id).ToListAsync(token));
            await DocumentAttachmentService.RemoveForOwnersAsync(_db, TailoringAttachmentOwnerTypes.Order, new[] { id }, token);
            _db.Remove(o);
            await _db.SaveChangesAsync(token);
            await LogAsync("TAILORING_ORDER_DELETED", o, o.CustomerName, token);
        }, ct);

    /// <summary>يبني أسطر الأمر ويسعّرها ويحدّث رأسه (بلا حفظ).</summary>
    private async Task<List<TailoringOrderLine>> BuildAsync(TailoringOrder o, SaveTailoringOrderDto r, CancellationToken ct)
    {
        if (r.Lines.Count == 0) throw new ValidationFailedException(Messages.TailoringOrderNoLines);
        if (r.Lines.Count > MaxLines) throw new ValidationFailedException(string.Format(Messages.TailoringOrderTooManyLines, MaxLines));
        if (!TailoringPriorities.IsValid(r.Priority)) throw new ValidationFailedException(Messages.TailoringOrderInvalid);
        var customer = await _db.Set<Customer>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.CustomerId, ct)
            ?? throw new ValidationFailedException(Messages.TailoringCustomerNotFound);
        var settings = await _db.Set<TailoringSettings>().AsNoTracking().FirstOrDefaultAsync(ct) ?? new TailoringSettings();
        var day = (r.OrderDate ?? (o.OrderDate == default ? SaudiTime.Today : o.OrderDate)).Date;
        if (r.ExpectedDeliveryDate is DateTime due && due.Date < day) throw new ValidationFailedException(Messages.TailoringOrderDueBeforeDate);
        var warehouseId = r.WarehouseId ?? settings.DefaultWarehouseId;
        if (warehouseId is Guid wh && !await _db.Set<Warehouse>().AnyAsync(w => w.Id == wh, ct)) throw new ValidationFailedException(Messages.TailoringWarehouseNotFound);

        var canDiscount = await _permissions.HasPermissionAsync(PermissionCatalog.TailoringOrdersScreenId, ScreenAction.Discount, ct);
        var pricesIncludeVat = await _db.Set<AccountingSettings>().AsNoTracking().Select(s => s.ItemPricesIncludeVat).FirstOrDefaultAsync(ct);
        var lines = new List<TailoringOrderLine>();
        var order = 0;
        foreach (var x in r.Lines)
            lines.Add(await BuildLineAsync(customer.Id, x, ++order, canDiscount, pricesIncludeVat, ct));

        o.CustomerId = customer.Id; o.CustomerName = customer.NameAr; o.CustomerPhone = customer.Phone; o.WarehouseId = warehouseId;
        o.OrderDate = day; o.ExpectedDeliveryDate = r.ExpectedDeliveryDate?.Date ?? day.AddDays(settings.DefaultLeadDays);
        o.Priority = r.Priority; o.Notes = Trim(r.Notes, 1000);
        o.PieceCount = lines.Sum(l => l.Quantity);
        o.GrossAmount = lines.Sum(l => DocumentPricing.Round(l.UnitPrice * l.Quantity));
        o.DiscountTotal = lines.Sum(l => l.DiscountAmount);
        o.NetTotal = lines.Sum(l => l.NetAmount);
        o.VatTotal = lines.Sum(l => l.VatAmount);
        o.GrandTotal = lines.Sum(l => l.TotalAmount);
        return lines;
    }

    private async Task<TailoringOrderLine> BuildLineAsync(Guid customerId, SaveTailoringOrderLineDto x, int sort, bool canDiscount, bool pricesIncludeVat, CancellationToken ct)
    {
        var type = await _db.Set<TailoringGarmentType>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == x.GarmentTypeId, ct)
            ?? throw new ValidationFailedException(Messages.TailoringGarmentTypeNotFound);
        if (!type.IsActive) throw new ValidationFailedException(Messages.TailoringGarmentTypeInactive);
        if (x.Quantity < 1 || x.Quantity > MaxPiecesPerLine || x.DiscountAmount < 0 || x.UnitPrice < 0 || x.FabricMeters < 0 || x.CustomerFabricMeters < 0)
            throw new ValidationFailedException(Messages.TailoringOrderInvalid);
        if (!TailoringFabricSources.IsValid(x.FabricSource)) throw new ValidationFailedException(Messages.TailoringOrderInvalid);

        var l = new TailoringOrderLine
        {
            SortOrder = sort, GarmentTypeId = type.Id, GarmentTypeName = type.NameAr, ProductId = type.ProductId, Quantity = x.Quantity,
            FabricSource = x.FabricSource, BasePrice = type.BasePrice, Instructions = Trim(x.Instructions, 1000),
        };

        // القياس: لقطة النسخة الحالية لملف العميل نفسه وبالنوع نفسه
        l.WearerName = Trim(x.WearerName, 150);
        if (x.MeasurementProfileId is Guid pid)
        {
            var p = await _db.Set<TailoringMeasurementProfile>().AsNoTracking().FirstOrDefaultAsync(m => m.Id == pid, ct)
                ?? throw new ValidationFailedException(Messages.TailoringProfileNotFound);
            if (p.CustomerId != customerId || p.GarmentTypeId != type.Id) throw new ValidationFailedException(Messages.TailoringOrderProfileMismatch);
            l.MeasurementProfileId = p.Id;
            l.WearerName ??= p.WearerName;
            if (p.CurrentVersionId is Guid vid)
            {
                var v = await _db.Set<TailoringMeasurementVersion>().AsNoTracking().FirstAsync(m => m.Id == vid, ct);
                l.MeasurementVersionId = v.Id; l.MeasurementUnit = v.Unit; l.MeasurementsJson = v.ValuesJson; l.MeasurementChoicesJson = v.ChoicesJson;
            }
        }

        // التصميم: من خيارات النوع الفعّالة؛ لقطة بالأسماء وفروق الأسعار
        var options = await _db.Set<TailoringDesignOption>().AsNoTracking().Where(op => op.GarmentTypeId == type.Id && op.IsActive)
            .OrderBy(op => op.SortOrder).ToListAsync(ct);
        var optionIds = options.Select(op => op.Id).ToList();
        var values = await _db.Set<TailoringDesignOptionValue>().AsNoTracking().Where(v => optionIds.Contains(v.OptionId) && v.IsActive).ToListAsync(ct);
        var unknown = x.Design.Keys.FirstOrDefault(k => !options.Any(op => op.Key == k));
        if (unknown != null) throw new ValidationFailedException(string.Format(Messages.TailoringOrderUnknownOption, unknown));
        var design = new List<TailoringDesignChoiceDto>();
        foreach (var op in options)
        {
            var raw = x.Design.GetValueOrDefault(op.Key)?.Trim();
            if (string.IsNullOrEmpty(raw))
            {
                if (op.IsRequired) throw new ValidationFailedException(string.Format(Messages.TailoringOrderOptionRequired, op.NameAr));
                continue;
            }
            var choice = new TailoringDesignChoiceDto { Key = op.Key, NameAr = op.NameAr, NameEn = op.NameEn, InputType = op.InputType, Value = raw.Length > 300 ? raw[..300] : raw };
            if (op.InputType == TailoringOptionInputTypes.Select)
            {
                var v = values.FirstOrDefault(v => v.OptionId == op.Id && v.Code == raw)
                    ?? throw new ValidationFailedException(string.Format(Messages.TailoringOrderOptionValueInvalid, op.NameAr));
                choice.ValueNameAr = v.NameAr; choice.ValueNameEn = v.NameEn; choice.PriceDelta = v.PriceDelta;
            }
            else if (op.InputType == TailoringOptionInputTypes.Number && !decimal.TryParse(raw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out _))
                throw new ValidationFailedException(string.Format(Messages.TailoringOrderOptionValueInvalid, op.NameAr));
            else if (op.InputType == TailoringOptionInputTypes.Boolean && raw is not ("true" or "false"))
                throw new ValidationFailedException(string.Format(Messages.TailoringOrderOptionValueInvalid, op.NameAr));
            design.Add(choice);
        }
        l.DesignJson = JsonSerializer.Serialize(design, Json);
        l.OptionsPrice = design.Sum(d => d.PriceDelta);

        // القماش: قماش المحل صنف مخزني بسعره الصافي، وقماش العميل عهدة بلا سعر
        if (x.FabricSource == TailoringFabricSources.Shop)
        {
            l.FabricMeters = decimal.Round(x.FabricMeters ?? type.DefaultFabricMeters, 3);
            if (x.FabricProductId is Guid fid)
            {
                var fabric = await _db.Set<Product>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == fid, ct)
                    ?? throw new ValidationFailedException(Messages.TailoringFabricNotFound);
                if (fabric.ItemType is not (ProductItemTypes.Stock or ProductItemTypes.Manufactured))
                    throw new ValidationFailedException(Messages.TailoringFabricNotStock);
                l.FabricProductId = fabric.Id; l.FabricName = fabric.NameAr;
                l.FabricUnitPrice = DocumentPricing.Round(pricesIncludeVat && fabric.VatRate > 0 ? fabric.SellingPrice / (1 + fabric.VatRate / 100m) : fabric.SellingPrice);
                l.FabricPrice = DocumentPricing.Round(l.FabricMeters * l.FabricUnitPrice);
            }
        }
        else
        {
            l.CustomerFabricDescription = Trim(x.CustomerFabricDescription, 300);
            l.CustomerFabricMeters = x.CustomerFabricMeters is decimal m ? decimal.Round(m, 3) : null;
            l.CustomerFabricColor = Trim(x.CustomerFabricColor, 100);
        }

        // السعر والخصم والضريبة
        l.SuggestedUnitPrice = DocumentPricing.Round(l.BasePrice + l.OptionsPrice + l.FabricPrice);
        l.UnitPrice = x.UnitPrice is decimal price ? DocumentPricing.Round(price) : l.SuggestedUnitPrice;
        l.PriceOverridden = l.UnitPrice != l.SuggestedUnitPrice;
        l.DiscountAmount = DocumentPricing.Round(x.DiscountAmount);
        if ((l.PriceOverridden || l.DiscountAmount > 0) && !canDiscount) throw new ForbiddenException(Messages.TailoringOrderDiscountForbidden);
        var gross = DocumentPricing.Round(l.UnitPrice * l.Quantity);
        if (l.DiscountAmount > gross) throw new ValidationFailedException(Messages.TailoringOrderDiscountTooHigh);
        l.VatRate = type.ProductId is Guid sid
            ? await _db.Set<Product>().AsNoTracking().Where(p => p.Id == sid).Select(p => p.VatRate).FirstOrDefaultAsync(ct) : 15m;
        l.NetAmount = gross - l.DiscountAmount;
        l.VatAmount = DocumentPricing.Round(l.NetAmount * l.VatRate / 100m);
        l.TotalAmount = l.NetAmount + l.VatAmount;
        return l;
    }

    // ---------- الدورة ----------

    public Task<TailoringOrderDto> ConfirmAsync(Guid id, CancellationToken ct = default)
        => RunAndGetAsync(async token =>
        {
            var o = await LoadAsync(id, token);
            if (o.Status != St.Draft) throw new ConflictException(Messages.TailoringOrderNotDraft);
            var lines = await _db.Set<TailoringOrderLine>().AsNoTracking().Where(l => l.OrderId == id).OrderBy(l => l.SortOrder).ToListAsync(token);
            if (lines.Count == 0) throw new ValidationFailedException(Messages.TailoringOrderNoLines);
            var noMeasure = lines.FirstOrDefault(l => l.MeasurementVersionId == null);
            if (noMeasure != null) throw new ValidationFailedException(string.Format(Messages.TailoringOrderMeasurementRequired, noMeasure.SortOrder, noMeasure.GarmentTypeName));
            var noProduct = lines.FirstOrDefault(l => l.ProductId == null);
            if (noProduct != null) throw new ValidationFailedException(string.Format(Messages.TailoringOrderServiceItemRequired, noProduct.GarmentTypeName));

            var piece = 0;
            foreach (var l in lines)
                for (var i = 0; i < l.Quantity; i++)
                {
                    piece++;
                    _db.Add(new TailoringGarment { OrderId = o.Id, LineId = l.Id, PieceNo = i + 1, Code = $"{o.OrderNumber}-{piece:00}" });
                }
            Move(o, St.Confirmed, null);
            o.ConfirmedAt = SaudiTime.Now; o.ConfirmedBy = By;
            await _db.SaveChangesAsync(token);
            await LogAsync("TAILORING_ORDER_CONFIRMED", o, $"{o.PieceCount} pcs • {o.GrandTotal:0.##}", token);
            return o.Id;
        }, ct);

    public Task<TailoringOrderDto> ReopenAsync(Guid id, TailoringReasonDto r, CancellationToken ct = default)
        => RunAndGetAsync(async token =>
        {
            var o = await LoadAsync(id, token);
            var reason = Trim(r.Reason, 500) ?? throw new ValidationFailedException(Messages.TailoringReasonRequired);
            if (o.Status != St.Confirmed) throw new ConflictException(Messages.TailoringOrderNotConfirmed);
            if (o.AdvanceInvoiceId != null) throw new ConflictException(Messages.TailoringOrderHasAdvance);
            var garments = await _db.Set<TailoringGarment>().Where(g => g.OrderId == id).ToListAsync(token);
            if (garments.Any(g => g.Status != TailoringGarmentStatuses.Pending || g.CurrentStageId != null))
                throw new ConflictException(Messages.TailoringOrderProductionStarted);
            _db.RemoveRange(garments);
            Move(o, St.Draft, reason);
            o.ConfirmedAt = null; o.ConfirmedBy = null;
            await _db.SaveChangesAsync(token);
            await LogAsync("TAILORING_ORDER_REOPENED", o, reason, token);
            return o.Id;
        }, ct);

    public Task<TailoringOrderDto> CancelAsync(Guid id, TailoringReasonDto r, CancellationToken ct = default)
        => RunAndGetAsync(async token =>
        {
            var o = await LoadAsync(id, token);
            var reason = Trim(r.Reason, 500) ?? throw new ValidationFailedException(Messages.TailoringReasonRequired);
            if (o.Status is not (St.Draft or St.Confirmed)) throw new ConflictException(Messages.TailoringOrderCannotCancel);
            if (o.AdvanceInvoiceId != null) throw new ConflictException(Messages.TailoringOrderHasAdvance);
            var garments = await _db.Set<TailoringGarment>().Where(g => g.OrderId == id).ToListAsync(token);
            if (garments.Any(g => g.Status != TailoringGarmentStatuses.Pending || g.CurrentStageId != null))
                throw new ConflictException(Messages.TailoringOrderProductionStarted);
            foreach (var g in garments) g.Status = TailoringGarmentStatuses.Cancelled;
            Move(o, St.Cancelled, reason);
            o.CancelledAt = SaudiTime.Now; o.CancelReason = reason;
            await _db.SaveChangesAsync(token);
            await LogAsync("TAILORING_ORDER_CANCELLED", o, reason, token);
            return o.Id;
        }, ct);

    // ---------- الدفعة المقدمة ----------

    public async Task<TailoringOrderDto> AdvanceAsync(Guid id, TailoringAdvanceRequestDto r, CancellationToken ct = default)
    {
        var invoiceId = await _tx.RunAsync(async token =>
        {
            var o = await LoadAsync(id, token);
            if (!St.Open.Contains(o.Status)) throw new ConflictException(Messages.TailoringOrderNotConfirmed);
            if (o.AdvanceInvoiceId != null) throw new ConflictException(Messages.TailoringOrderHasAdvance);
            if (r.PaymentMethod is not (PaymentMethod.Cash or PaymentMethod.BankCard or PaymentMethod.BankTransfer))
                throw new ValidationFailedException(Messages.InvalidPaymentMethod);
            var amount = DocumentPricing.Round(r.Amount);
            if (amount <= 0 || amount > o.GrandTotal) throw new ValidationFailedException(Messages.TailoringAdvanceAmountInvalid);
            var min = await MinAdvanceAsync(o, token);
            if (amount < min) throw new ValidationFailedException(string.Format(Messages.TailoringAdvanceBelowMinimum, min));
            var day = (r.Date ?? SaudiTime.Today).Date;
            if (day > SaudiTime.Today || day < o.OrderDate) throw new ValidationFailedException(Messages.TailoringAdvanceDateInvalid);

            // ضريبة الدفعة بنسبة أسطر الأمر (نسبة واحدة؛ الأمر بنسب مختلفة لا يُقدَّم عليه عربون موحد)
            var rates = await _db.Set<TailoringOrderLine>().AsNoTracking().Where(l => l.OrderId == id).Select(l => l.VatRate).Distinct().ToListAsync(token);
            if (rates.Count != 1) throw new ValidationFailedException(Messages.TailoringAdvanceMixedVat);
            var rate = rates[0];
            var net = DocumentPricing.Round(amount / (1 + rate / 100m));

            await ContractAccountsSetup.EnsureModuleMissingAsync(_db, ContractAccountsSetup.ModuleAccountSet.Tailoring, token);
            var advanceAccount = await _db.Set<AccountingSettings>().AsNoTracking().Select(s => s.TailoringAdvancesReceivedAccountCode).FirstOrDefaultAsync(token)
                ?? throw new ValidationFailedException(Messages.TailoringAccountsMissing);
            var customer = await _db.Set<Customer>().AsNoTracking().FirstAsync(c => c.Id == o.CustomerId, token);
            var title = $"دفعة مقدمة - أمر تفصيل {o.OrderNumber}";
            var invoice = await _sales.CreateAsync(new CreateInvoiceDto
            {
                SystemGenerated = true, Kind = InvoiceKind.Sales, InvoiceType = TradeHelper.InvoiceTypeFor(customer.VatNumber), IssueDate = day,
                PartyId = customer.Id, PartyName = customer.NameAr, PartyVatNumber = customer.VatNumber, PartyPhone = customer.Phone, PartyEmail = customer.Email,
                PaymentMethod = r.PaymentMethod, PaymentMethodCode = Trim(r.PaymentMethodCode, 50), Status = "posted", WarehouseId = o.WarehouseId,
                Notes = string.IsNullOrWhiteSpace(r.Notes) ? title : $"{title} — {r.Notes.Trim()}",
                ReferenceType = AdvanceReferenceType, ReferenceId = o.Id, ReferenceNumber = o.OrderNumber,
                // العربون التزام حتى يُسترد عند التسليم، لا إيراد مكتسب
                RevenueAccountCode = advanceAccount, SkipCreditLimit = true,
                SendToZatca = await ZatcaLinkedAsync(token) ? true : null,
                Items = new List<InvoiceItemDto> { new() { ItemId = Guid.Empty, ItemName = title, Unit = "خدمة", Quantity = 1, UnitPrice = net, VatRate = rate } },
            }, token);

            o.AdvanceInvoiceId = invoice.Id; o.AdvanceInvoiceNumber = invoice.InvoiceNumber; o.AdvanceAmount = invoice.GrandTotal;
            o.AdvanceVat = invoice.VatTotal; o.AdvanceNet = invoice.GrandTotal - invoice.VatTotal; o.AdvanceAt = SaudiTime.Now;
            AddHistory(o, o.Status, o.Status, $"{Messages.TailoringHistoryAdvance} {invoice.InvoiceNumber} • {invoice.GrandTotal:0.00}");
            await _db.SaveChangesAsync(token);
            await LogAsync("TAILORING_ADVANCE_INVOICED", o, $"{invoice.InvoiceNumber} • {invoice.GrandTotal:0.##}", token);
            return invoice.Id;
        }, ct);
        // الفاتورة أُنشئت داخل المعاملة فتخطّاها الإرسال التلقائي: تُرسل بعدها، والرفض يُحفظ على الفاتورة ولا يُفشل العملية
        try { await _sales.SubmitToZatcaIfRequestedAsync(invoiceId, ct); } catch (Exception) when (!ct.IsCancellationRequested) { }
        return await GetAsync(id, ct);
    }

    public Task<TailoringOrderDto> CancelAdvanceAsync(Guid id, TailoringReasonDto r, CancellationToken ct = default)
        => RunAndGetAsync(async token =>
        {
            var o = await LoadAsync(id, token);
            var reason = Trim(r.Reason, 500) ?? throw new ValidationFailedException(Messages.TailoringReasonRequired);
            if (o.AdvanceInvoiceId is not Guid invoiceId) throw new ConflictException(Messages.TailoringOrderNoAdvance);
            if (o.AdvanceRecovered > 0) throw new ConflictException(Messages.TailoringAdvanceRecovered);
            var payment = await _db.Set<InvoiceBase>().AsNoTracking().Where(i => i.Id == invoiceId).Select(i => new { i.PaymentMethod, i.PaymentMethodCode }).FirstAsync(token);
            var text = $"إلغاء الدفعة المقدمة لأمر التفصيل {o.OrderNumber} ({o.AdvanceInvoiceNumber}): {reason}";
            await _returns.CreateFromInvoiceAsync(new CreateReturnInvoiceRequestDto
            {
                OriginalInvoiceId = invoiceId, ReturnReason = text, SystemGenerated = true, ReturnDate = SaudiTime.Today,
                RefundPaymentMethod = payment.PaymentMethod, RefundPaymentMethodCode = payment.PaymentMethodCode,
            }, token);
            AddHistory(o, o.Status, o.Status, $"{Messages.TailoringHistoryAdvanceCancelled} {o.AdvanceInvoiceNumber}: {reason}");
            o.AdvanceInvoiceId = null; o.AdvanceInvoiceNumber = null; o.AdvanceAmount = 0; o.AdvanceNet = 0; o.AdvanceVat = 0; o.AdvanceAt = null;
            await _db.SaveChangesAsync(token);
            await LogAsync("TAILORING_ADVANCE_CANCELLED", o, text, token);
            return o.Id;
        }, ct);

    // ---------- مساعدات ----------

    private async Task<decimal> MinAdvanceAsync(TailoringOrder o, CancellationToken ct)
    {
        var percent = await _db.Set<TailoringSettings>().AsNoTracking().Select(s => s.MinAdvancePercent).FirstOrDefaultAsync(ct);
        return DocumentPricing.Round(o.GrandTotal * percent / 100m);
    }

    private async Task<bool> ZatcaLinkedAsync(CancellationToken ct)
        => await _db.Set<Tenant>().AsNoTracking().Select(t => t.ZatcaConfig.IsEnabled).FirstOrDefaultAsync(ct)
            && await _db.Set<ZatcaUnit>().AsNoTracking().AnyAsync(u => u.IsActive, ct);

    private async Task<TailoringOrder> LoadAsync(Guid id, CancellationToken ct)
        => await _db.Set<TailoringOrder>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(Messages.TailoringOrderNotFound);

    private async Task<TailoringOrderDto> RunAndGetAsync(Func<CancellationToken, Task<Guid>> work, CancellationToken ct)
        => await GetAsync(await _tx.RunAsync(work, ct), ct);

    private void Move(TailoringOrder o, string to, string? note)
    {
        AddHistory(o, o.Status, to, note);
        o.Status = to;
    }

    private void AddHistory(TailoringOrder o, string? from, string to, string? note)
        => _db.Add(new TailoringOrderStatusHistory
        {
            OrderId = o.Id, FromStatus = from, ToStatus = to, Note = note, ChangedBy = By, ChangedByUserId = _user.UserId, ChangedAt = SaudiTime.Now,
        });

    private async Task<TailoringOrderDto> ToDtoAsync(TailoringOrder o, List<TailoringOrderLine> lines, CancellationToken ct)
    {
        var versionIds = lines.Where(l => l.MeasurementVersionId != null).Select(l => l.MeasurementVersionId!.Value).ToList();
        var versionNos = await _db.Set<TailoringMeasurementVersion>().AsNoTracking().Where(v => versionIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, v => v.VersionNo, ct);
        return new TailoringOrderDto
        {
            Id = o.Id, OrderNumber = o.OrderNumber, CustomerId = o.CustomerId, CustomerName = o.CustomerName, CustomerPhone = o.CustomerPhone,
            WarehouseId = o.WarehouseId, OrderDate = o.OrderDate, ExpectedDeliveryDate = o.ExpectedDeliveryDate, Priority = o.Priority, Status = o.Status,
            Notes = o.Notes, PieceCount = o.PieceCount, GrossAmount = o.GrossAmount, DiscountTotal = o.DiscountTotal, NetTotal = o.NetTotal,
            VatTotal = o.VatTotal, GrandTotal = o.GrandTotal, AdvanceInvoiceId = o.AdvanceInvoiceId, AdvanceInvoiceNumber = o.AdvanceInvoiceNumber,
            AdvanceAmount = o.AdvanceAmount, AdvanceNet = o.AdvanceNet, AdvanceVat = o.AdvanceVat, AdvanceRecovered = o.AdvanceRecovered, AdvanceAt = o.AdvanceAt,
            Remaining = o.GrandTotal - o.AdvanceAmount, MinAdvanceAmount = await MinAdvanceAsync(o, ct),
            ConfirmedAt = o.ConfirmedAt, ConfirmedBy = o.ConfirmedBy, CancelReason = o.CancelReason, CreatedBy = o.CreatedBy, CreatedAt = o.CreatedAt,
            IsLate = o.ExpectedDeliveryDate is DateTime due && due.Date < SaudiTime.Today && St.Open.Contains(o.Status),
            Lines = lines.Select(l => new TailoringOrderLineDto
            {
                Id = l.Id, SortOrder = l.SortOrder, GarmentTypeId = l.GarmentTypeId, GarmentTypeName = l.GarmentTypeName,
                MeasurementProfileId = l.MeasurementProfileId, MeasurementVersionId = l.MeasurementVersionId,
                MeasurementVersionNo = l.MeasurementVersionId is Guid vid ? versionNos.GetValueOrDefault(vid) : null,
                WearerName = l.WearerName, MeasurementUnit = l.MeasurementUnit,
                Measurements = string.IsNullOrEmpty(l.MeasurementsJson) ? new() : JsonSerializer.Deserialize<Dictionary<string, decimal?>>(l.MeasurementsJson) ?? new(),
                MeasurementChoices = TailoringMeasurementJson.ReadSelected(l.MeasurementChoicesJson),
                Design = JsonSerializer.Deserialize<List<TailoringDesignChoiceDto>>(l.DesignJson, Json) ?? new(),
                FabricSource = l.FabricSource, FabricProductId = l.FabricProductId, FabricName = l.FabricName, FabricMeters = l.FabricMeters,
                FabricUnitPrice = l.FabricUnitPrice, CustomerFabricDescription = l.CustomerFabricDescription, CustomerFabricMeters = l.CustomerFabricMeters,
                CustomerFabricColor = l.CustomerFabricColor, Quantity = l.Quantity, BasePrice = l.BasePrice, OptionsPrice = l.OptionsPrice,
                FabricPrice = l.FabricPrice, SuggestedUnitPrice = l.SuggestedUnitPrice, UnitPrice = l.UnitPrice, PriceOverridden = l.PriceOverridden,
                DiscountAmount = l.DiscountAmount, NetAmount = l.NetAmount, VatRate = l.VatRate, VatAmount = l.VatAmount, TotalAmount = l.TotalAmount,
                Instructions = l.Instructions,
            }).ToList(),
        };
    }

    private Task LogAsync(string action, TailoringOrder o, string details, CancellationToken ct)
        => _audit.LogAsync(action, nameof(TailoringOrder), o.Id.ToString(), $"{o.OrderNumber} • {details}", ct);

    private static string? Trim(string? v, int max)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        v = v.Trim();
        return v.Length > max ? v[..max] : v;
    }
}
