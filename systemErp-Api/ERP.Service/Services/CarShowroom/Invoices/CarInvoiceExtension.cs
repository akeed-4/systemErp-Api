using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.CarShowroom;
using ERP.Service.Data;
using ERP.Service.Services.Accounting;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

/// <summary>
/// امتداد المعارض على دورة الفاتورة: أسطر السيارات ودفعاتها، بطاقات المسودة، إنشاء المركبات وبيعها عند الترحيل،
/// إعادتها عند إلغاء الترحيل، مرتجعات السيارات، وحجب تكلفتها عمّن لا يملك صلاحية رؤيتها.
/// InvoiceService لا يعرف شيئاً من هذا: يستدعي نقاط IInvoiceExtension فقط.
/// </summary>
public class CarInvoiceExtension : IInvoiceExtension
{
    private readonly ErpDbContext _db;
    private readonly IVehicleService _vehicles;
    private readonly IAccountingPostingService _posting;
    private readonly ICostVisibility _carCost;

    public CarInvoiceExtension(ErpDbContext db, IVehicleService vehicles, IAccountingPostingService posting, ICostVisibility carCost)
    {
        _db = db; _vehicles = vehicles; _posting = posting; _carCost = carCost;
    }

    public string Module => PlatformModules.CarShowroom;

    // سيارات الفاتورة: دفعات (شراء: سطر لكل دفعة بشواصيها) أو أسطر مستقلة (بيع، وشراء سيارة سيارة). كلاهما يشتق r.Items ويتحقق
    public async Task<Func<Invoice, CancellationToken, Task>?> ResolveAsync(CreateInvoiceDto r, CancellationToken ct)
    {
        if (r.VehicleBatches.Count > 0)
        {
            var (batches, lines) = await VehicleBatchLines.ResolveAsync(_db, _vehicles, r, ct);
            return (invoice, token) => AttachAsync(invoice, lines, batches, token);
        }
        if (r.VehicleLines.Count == 0) return null;
        var vehicleLines = await VehicleInvoiceLines.ResolveAsync(_db, r, ct);
        return (invoice, token) => AttachAsync(invoice, vehicleLines, null, token);
    }

    private async Task AttachAsync(Invoice invoice, List<InvoiceVehicleLine> lines, List<InvoiceVehicleBatch>? batches, CancellationToken ct)
    {
        invoice.VehicleLines.Clear();
        foreach (var vl in lines) invoice.VehicleLines.Add(vl);
        invoice.VehicleBatches.Clear();
        if (batches != null) foreach (var batch in batches) invoice.VehicleBatches.Add(batch);
        // حسابات السيارات من إعدادات المعرض عند عدم تحديدها من العميل
        var isSales = invoice.Kind is InvoiceKind.Sales or InvoiceKind.SalesReturn;
        var car = await SalesCharges.SettingsAsync(_db, ct);
        if (string.IsNullOrWhiteSpace(invoice.InventoryAccountCode)) invoice.InventoryAccountCode = car.VehicleInventoryAccount;
        if (isSales && string.IsNullOrWhiteSpace(invoice.RevenueAccountCode)) invoice.RevenueAccountCode = car.CarSalesRevenueAccount;
        if (isSales && string.IsNullOrWhiteSpace(invoice.CogsAccountCode)) invoice.CogsAccountCode = car.CarCogsAccount;
    }

    public async Task<bool> OwnsReferenceAsync(string? referenceType, Guid referenceId, CancellationToken ct) => referenceType switch
    {
        "car_sales_contract" => await _db.Set<CarSalesContract>().AnyAsync(c => c.Id == referenceId, ct),
        "car_procurement_order" => await _db.Set<CarProcurementOrder>().AnyAsync(o => o.Id == referenceId, ct),
        _ => false,
    };

    // بطاقات شواصي الدفعات تُنشأ مع فاتورة الشراء بحالة «مسودة» في حفظ واحد، وتُطابَق مع الشواصي عند التعديل، وتُحذف معها
    public Task OnSavingAsync(Invoice invoice, bool deleting, CancellationToken ct)
        => invoice.Kind == InvoiceKind.Purchase ? VehicleBatchLines.SyncDraftVehiclesAsync(_db, invoice, removeAll: deleting, ct) : Task.CompletedTask;

    public async Task OnPostingAsync(Invoice invoice, CancellationToken ct)
    {
        await VehicleInvoiceLines.OnPostingAsync(_db, _vehicles, invoice, ct); // شراء: إنشاء المركبات، بيع: تحويلها Sold
        if (invoice.Kind == InvoiceKind.Purchase) await VehicleBatchLines.OnPostingAsync(_db, _posting, invoice, ct); // دفعات: البطاقات من مسودة إلى المخزون
    }

    public Task OnUnpostingAsync(Invoice invoice, CancellationToken ct)
        => invoice.VehicleLines.Count > 0 ? UnpostVehiclesAsync(invoice, ct) : Task.CompletedTask;

    public async Task<InvoiceDto?> CreateReturnAsync(CreateReturnInvoiceRequestDto r, Invoice original,
        Func<CreateInvoiceDto, CancellationToken, Task<InvoiceDto>> create, CancellationToken ct)
    {
        var originalLines = await _db.Set<InvoiceVehicleLine>().AsNoTracking().Where(l => l.InvoiceId == original.Id).OrderBy(l => l.LineNo).ToListAsync(ct);
        return originalLines.Count > 0 ? await CreateVehicleReturnAsync(r, original, originalLines, create, ct) : null;
    }

    public Task FillAsync(Invoice invoice, InvoiceDto dto, CancellationToken ct) => VehicleBatchLines.FillAsync(_db, invoice, dto, ct);

    // تكلفة السيارات وربحها لمن يملك صلاحية car-cost-view فقط
    public async Task MaskAsync(IReadOnlyList<(Invoice Invoice, InvoiceDto Dto)> rows, CancellationToken ct)
    {
        if (!rows.Any(x => IsCarInvoice(x.Invoice)) || await _carCost.CanViewAsync(ct)) return;
        foreach (var (_, dto) in rows.Where(x => IsCarInvoice(x.Invoice)))
        {
            dto.TotalCost = 0; dto.GrossProfit = 0;
            foreach (var item in dto.Items) item.UnitCost = 0;
            foreach (var line in dto.VehicleLines) line.UnitCost = 0;
        }
    }

    /// <summary>قيد بيع السيارة يُظهر تكلفتها (تكلفة المبيعات)، فيتبع الصلاحية نفسها.</summary>
    public async Task EnsureJournalVisibleAsync(Invoice invoice, CancellationToken ct)
    {
        if (IsCarInvoice(invoice) && invoice.Kind is InvoiceKind.Sales or InvoiceKind.SalesReturn && !await _carCost.CanViewAsync(ct))
            throw new ForbiddenException();
    }

    private static bool IsCarInvoice(Invoice invoice)
        => invoice.VehicleLines.Count > 0 || invoice.ReferenceType?.StartsWith("car_", StringComparison.Ordinal) == true;

    /// <summary>
    /// مرتجع فاتورة سيارات: يختار المستخدم المركبات المرتجعة (أو كلها). شراء ← المركبة يجب أن تكون متاحة وتخرج من المخزون (مشطوبة)،
    /// بيع ← المركبة يجب أن تكون مباعة وتعود متاحة. القيد من بنود السيارات نفسها (بتكلفتها في البيع)، ولا تُرجَع مركبة مرتين.
    /// </summary>
    private async Task<InvoiceDto> CreateVehicleReturnAsync(CreateReturnInvoiceRequestDto r, Invoice original, List<InvoiceVehicleLine> originalLines,
        Func<CreateInvoiceDto, CancellationToken, Task<InvoiceDto>> create, CancellationToken ct)
    {
        var isSales = original.Kind == InvoiceKind.Sales;
        var alreadyReturned = (await _db.Set<InvoiceVehicleLine>().AsNoTracking()
                .Where(l => l.VehicleId != null && _db.Set<Invoice>().Any(inv => inv.Id == l.InvoiceId && inv.OriginalInvoiceId == original.Id && inv.Status == "posted"))
                .Select(l => l.VehicleId!.Value).ToListAsync(ct)).ToHashSet();

        var chosen = r.VehicleIds.Count == 0
            ? originalLines.Where(l => l.VehicleId.HasValue && !alreadyReturned.Contains(l.VehicleId.Value)).ToList()
            : r.VehicleIds.Distinct().Select(id => originalLines.FirstOrDefault(l => l.VehicleId == id) ?? throw new ValidationFailedException(Messages.ReturnVehicleNotInOriginal)).ToList();
        if (chosen.Count == 0) throw new ConflictException(Messages.NoRemainingQtyToReturn);

        var vehicleIds = chosen.Select(l => l.VehicleId!.Value).ToList();
        var vehicles = await _db.Set<Vehicle>().Where(v => vehicleIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        foreach (var l in chosen)
        {
            if (alreadyReturned.Contains(l.VehicleId!.Value)) throw new ConflictException(string.Format(Messages.ReturnVehicleAlreadyReturned, l.Vin));
            if (!vehicles.TryGetValue(l.VehicleId.Value, out var v)) throw new ValidationFailedException(Messages.ReturnVehicleNotInOriginal);
            var expected = isSales ? VehicleStatus.Sold : VehicleStatus.Available;
            if (v.Status != expected) throw new ConflictException(string.Format(isSales ? Messages.ReturnVehicleNotSold : Messages.ReturnVehicleNotAvailable, v.ChassisNumber));
        }

        var dto = new CreateInvoiceDto
        {
            Kind = isSales ? InvoiceKind.SalesReturn : InvoiceKind.PurchaseReturn,
            InvoiceType = isSales ? InvoiceType.CreditNote : InvoiceType.DebitNote,
            IsReturn = true, OriginalInvoiceId = original.Id, OriginalInvoiceNumber = original.InvoiceNumber, ReturnReason = r.ReturnReason,
            PartyId = original.PartyId, PartyName = original.PartyName, PartyVatNumber = original.PartyVatNumber, PartyCrNumber = original.PartyCrNumber,
            PartyIdType = original.PartyIdType, PartyIdNumber = original.PartyIdNumber, PartyAddress = original.PartyAddress, PartyPhone = original.PartyPhone, PartyEmail = original.PartyEmail,
            WarehouseId = original.WarehouseId,
            PartyStreet = original.PartyStreet, PartyBuildingNo = original.PartyBuildingNo, PartyDistrict = original.PartyDistrict,
            PartyCity = original.PartyCity, PartyPostalCode = original.PartyPostalCode, PartyAdditionalNo = original.PartyAdditionalNo, PartyCountry = original.PartyCountry,
            PaymentMethod = r.RefundPaymentMethod ?? original.PaymentMethod, PaymentMethodCode = r.RefundPaymentMethod == null ? original.PaymentMethodCode : r.RefundPaymentMethodCode, CurrencyCode = original.CurrencyCode, ExchangeRate = original.ExchangeRate,
            RevenueAccountCode = original.RevenueAccountCode, InventoryAccountCode = original.InventoryAccountCode, CogsAccountCode = original.CogsAccountCode,
            Notes = TradeInvoiceEngine.ReturnNotes(original, r), Status = "posted",
            SendToZatca = TradeInvoiceEngine.ReturnSendsToZatca(original),
        };
        if (r.ReturnDate.HasValue) dto.IssueDate = r.ReturnDate.Value;
        var pool = original.Items.Where(i => i.ItemId == Guid.Empty).ToList();
        // شواصي الدفعات: بند مرتجع لكل (مواصفات، سعر) بكمية الشواصي المختارة، من بند الدفعة في الفاتورة الأصلية
        foreach (var group in chosen.Where(l => l.BatchId != null).GroupBy(l => (Name: VehicleBatchLines.Spec(l), l.UnitPrice)))
        {
            var src = pool.FirstOrDefault(i => i.ItemName == group.Key.Name && i.UnitPrice == group.Key.UnitPrice)
                ?? throw new ValidationFailedException(Messages.ItemNotInOriginalInvoice);
            var count = group.Count();
            dto.Items.Add(new InvoiceItemDto
            {
                ItemId = Guid.Empty, ItemName = src.ItemName, Sku = src.Sku, Unit = src.Unit, Quantity = count,
                UnitPrice = src.UnitPrice, UnitCost = src.UnitCost, Discount = count * group.First().Discount, VatRate = src.VatRate,
                VatCategory = src.VatCategory, VatExemptionReasonCode = src.VatExemptionReasonCode,
                RevenueAccountCode = src.RevenueAccountCode, CostCenterId = src.CostCenterId,
            });
        }
        foreach (var l in chosen.Where(l => l.BatchId == null))
        {
            var name = VehicleInvoiceLines.Describe(l);
            var src = pool.FirstOrDefault(i => i.ItemName == name) ?? throw new ValidationFailedException(Messages.ItemNotInOriginalInvoice);
            pool.Remove(src);
            dto.Items.Add(new InvoiceItemDto
            {
                ItemId = Guid.Empty, ItemName = src.ItemName, Sku = src.Sku, Unit = src.Unit, Quantity = 1,
                UnitPrice = src.UnitPrice, UnitCost = src.UnitCost, Discount = src.Discount, VatRate = src.VatRate,
                VatCategory = src.VatCategory, VatExemptionReasonCode = src.VatExemptionReasonCode, VatAmountOverride = src.VatAmountOverride,
                RevenueAccountCode = src.RevenueAccountCode, CostCenterId = src.CostCenterId,
            });
        }
        if (r.RefundSplits.Count > 0) await TradeInvoiceEngine.ApplyRefundSplitsAsync(_db, dto, r.RefundSplits, ct);
        else if (r.RefundPaymentMethod == null && chosen.Count == originalLines.Count) TradeInvoiceEngine.MirrorOriginalSplits(dto, original);

        var created = await create(dto, ct);

        // أسطر المرتجع تتبّع المركبات (لمنع إرجاعها مرة ثانية) وتتغير حالة المركبة
        var ret = await _db.Set<Invoice>().Include(i => i.VehicleLines).Include(i => i.VehicleBatches).FirstAsync(i => i.Id == created.Id, ct);
        var no = 1;
        foreach (var l in chosen)
        {
            ret.VehicleLines.Add(new InvoiceVehicleLine
            {
                LineNo = no++, VehicleId = l.VehicleId, Vin = l.Vin, TempRef = l.TempRef, BrandId = l.BrandId, ModelId = l.ModelId, TrimId = l.TrimId,
                BrandNameAr = l.BrandNameAr, ModelNameAr = l.ModelNameAr, TrimNameAr = l.TrimNameAr, Year = l.Year,
                ColorExterior = l.ColorExterior, ColorInterior = l.ColorInterior, EngineNumber = l.EngineNumber, CustomsCardNumber = l.CustomsCardNumber,
                VatMode = l.VatMode, UnitPrice = l.UnitPrice, Discount = l.Discount, VatAmount = l.VatAmount, TotalBeforeVat = l.TotalBeforeVat,
                TotalAfterVat = l.TotalAfterVat, UnitCost = l.UnitCost, IsTemporaryChassis = l.IsTemporaryChassis, PriceOverride = l.PriceOverride,
            });
            vehicles[l.VehicleId!.Value].Status = isSales ? VehicleStatus.Available : VehicleStatus.WrittenOff;
        }
        // كمرتجع العقد: السيارة تعود بتكلفتها وقت البيع، ومصاريف ما بعد البيع تبقى على تكلفة المبيعات ولا تُحسب مرة ثانية
        if (isSales) await SoldVehicleAdjustments.ExpenseLateChargesAsync(_db, vehicleIds, ct);
        await _db.SaveChangesAsync(ct);
        if (isSales)
        {
            await VehicleCostLedger.RecomputeAsync(_db, vehicleIds, ct);
            await _db.SaveChangesAsync(ct);
        }
        return created;
    }


    /// <summary>
    /// يعيد سيارات الفاتورة المرحّلة لحالتها قبل الترحيل: البيع ← «متاحة» من جديد، وشراء الدفعات ← بطاقات «مسودة» تُطابَق
    /// مع الشواصي بعد التعديل (فتبقى نفس البطاقات ومصاريفها)، وشراء الأسطر القديمة ← تُحذف بطاقاتها فتُنشأ عند إعادة الترحيل.
    /// السيارة التي بيعت أو حُجزت أو دخلت عقداً أو أمر شراء بعد الشراء، أو فاتورة عليها مرتجع سيارات، لا تُعاد.
    /// </summary>
    private async Task UnpostVehiclesAsync(Invoice invoice, CancellationToken ct)
    {
        if (await _db.Set<Invoice>().AnyAsync(i => i.OriginalInvoiceId == invoice.Id, ct)) throw new ConflictException(Messages.PostedVehicleInvoiceHasReturns);
        var ids = invoice.VehicleLines.Where(l => l.VehicleId.HasValue).Select(l => l.VehicleId!.Value).Distinct().ToList();
        var vehicles = await _db.Set<Vehicle>().Where(v => ids.Contains(v.Id)).ToListAsync(ct);

        if (invoice.Kind == InvoiceKind.Sales)
        {
            // مصروف وصل بعد البيع قُيِّد على تكلفة المبيعات: إعادة الترحيل بالتكلفة الحالية تحسبه مرتين، والحذف يعيد السيارة بتكلفة لا يقابلها المخزون؛ التصحيح بمرتجع
            var late = await _db.Set<AdditionalChargeAllocation>().AsNoTracking()
                .Where(a => ids.Contains(a.VehicleId) && a.PostedToCogs && a.Line.Status == ChargeStatus.Actual && a.Line.Treatment == ChargeTreatment.Capitalize)
                .Select(a => (Guid?)a.VehicleId).FirstOrDefaultAsync(ct);
            if (late.HasValue) throw new ConflictException(string.Format(Messages.PostedSaleVehicleHasLateCharges, vehicles.First(v => v.Id == late).ChassisNumber));
            foreach (var v in vehicles.Where(v => v.Status == VehicleStatus.Sold)) v.Status = VehicleStatus.Available;
            await _db.SaveChangesAsync(ct);
            return;
        }

        var busy = vehicles.FirstOrDefault(v => v.Status != VehicleStatus.Available);
        if (busy == null)
        {
            var linked = await _db.Set<CarSalesContract>().AsNoTracking().Where(c => ids.Contains(c.VehicleId)).Select(c => (Guid?)c.VehicleId).FirstOrDefaultAsync(ct)
                ?? await _db.Set<CarProcurementOrderVin>().AsNoTracking().Where(o => o.VehicleId.HasValue && ids.Contains(o.VehicleId.Value)).Select(o => o.VehicleId).FirstOrDefaultAsync(ct);
            busy = linked.HasValue ? vehicles.First(v => v.Id == linked) : null;
        }
        if (busy != null) throw new ConflictException(string.Format(Messages.PostedPurchaseVehicleInUse, busy.ChassisNumber));

        var batchIds = invoice.VehicleLines.Where(l => l.BatchId.HasValue && l.VehicleId.HasValue).Select(l => l.VehicleId!.Value).ToHashSet();
        foreach (var v in vehicles.Where(v => batchIds.Contains(v.Id)))
        {
            v.Status = VehicleStatus.Draft; // تعود بطاقة مسودة للفاتورة نفسها، ويحدّثها التعديل بالشاسيه
            v.ReceivedAt = null;
        }
        var legacy = vehicles.Where(v => !batchIds.Contains(v.Id)).ToList();
        if (legacy.Count > 0)
        {
            var legacyIds = legacy.Select(v => v.Id).ToList();
            if (await _db.Set<AdditionalChargeAllocation>().AnyAsync(a => legacyIds.Contains(a.VehicleId), ct))
                throw new ConflictException(string.Format(Messages.DraftVehicleHasCharges, legacy[0].ChassisNumber));
            foreach (var l in invoice.VehicleLines.Where(l => l.VehicleId.HasValue && legacyIds.Contains(l.VehicleId.Value))) l.VehicleId = null;
            _db.RemoveRange(legacy);
        }
        await _db.SaveChangesAsync(ct);
    }
}
