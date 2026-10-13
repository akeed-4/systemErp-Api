using System.Text.RegularExpressions;
using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using ERP.Service.Data;
using ERP.Service.Services.Accounting;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

/// <summary>
/// دورة مشتريات السيارات (7 مراحل). المنطق التشغيلي هنا: التحقق، إنشاء المركبات عند استلام الشواسيه،
/// وعند الفوترة يُرسَل الأثر المالي فقط إلى محرك الفوترة/الترحيل المركزي (فاتورة شراء + قيد على مخزون السيارات).
/// </summary>
public class CarProcurementService : ICarProcurementService
{
    private static readonly string[] PaymentTypes = { "cash", "credit", "bank_lc", "advance_milestone" };
    private static readonly string[] Currencies = { "SAR", "USD", "EUR", "AED" };
    private static readonly string[] PurchaseCycles = { "individual", "corporate", "bank_lease" };

    private readonly ErpDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly IInvoiceService _invoices;
    private readonly IAccountingPostingService _posting;
    private readonly ITransactionRunner _tx;
    private readonly IAuditService _audit;
    private readonly IVehicleService _vehicles;

    public CarProcurementService(ErpDbContext db, INumberSequenceService numbers, IInvoiceService invoices, IAccountingPostingService posting, ITransactionRunner tx, IAuditService audit,
        IVehicleService vehicles)
    {
        _db = db; _numbers = numbers; _invoices = invoices; _posting = posting; _tx = tx; _audit = audit; _vehicles = vehicles;
    }

    public async Task<PagedResult<CarProcurementOrderDto>> ListAsync(PaginationParams p, string? purchaseCycle = null, CancellationToken ct = default)
    {
        var q = _db.Set<CarProcurementOrder>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(purchaseCycle)) q = q.Where(o => o.PurchaseCycle == purchaseCycle);
        if (Enum.TryParse<ProcurementStage>(p.Status, true, out var stage)) q = q.Where(o => o.Stage == stage);
        if (p.StartDate.HasValue) q = q.Where(o => o.Date >= p.StartDate);
        if (p.EndDate.HasValue) q = q.Where(o => o.Date <= p.EndDate);
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(o => o.OrderNumber.Contains(t) || o.SupplierName.Contains(t));
        }
        var total = await q.CountAsync(ct);
        var items = await q.Include(o => o.Items).Include(o => o.ReceivedVins).OrderByDescending(o => o.Date).ThenByDescending(o => o.OrderNumber)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).AsSplitQuery().ToListAsync(ct);
        return new PagedResult<CarProcurementOrderDto>
        {
            Items = items.Select(Mapper.Map<CarProcurementOrderDto>).ToList(),
            TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    /// <summary>القائمة بخيارات DevExtreme: الفلترة والفرز والترقيم في SQL.</summary>
    public Task<DevExtreme.AspNet.Data.ResponseModel.LoadResult> LoadAsync(DataSourceLoadOptions options, string? purchaseCycle = null, CancellationToken ct = default)
    {
        var q = _db.Set<CarProcurementOrder>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(purchaseCycle)) q = q.Where(o => o.PurchaseCycle == purchaseCycle);
        return EntityLoader.LoadAsync(q.Include(o => o.Items).Include(o => o.ReceivedVins).AsSplitQuery(), options, Mapper.Map<CarProcurementOrderDto>, ct,
            EntityLoader.Desc(nameof(CarProcurementOrder.Date)), EntityLoader.Desc(nameof(CarProcurementOrder.OrderNumber)));
    }

    public async Task<CarProcurementOrderDto> GetAsync(Guid id, CancellationToken ct = default)
        => Mapper.Map<CarProcurementOrderDto>(await _db.Set<CarProcurementOrder>().AsNoTracking()
            .Include(o => o.Items).Include(o => o.ReceivedVins).AsSplitQuery().FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new NotFoundException(Messages.ProcurementOrderNotFound));

    public Task<CarProcurementOrderDto> CreateAsync(CreateCarProcurementOrderDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var supplier = await ValidateAsync(r, token);
            var order = Mapper.Map<CarProcurementOrder>(r);
            order.OrderNumber = await _numbers.NextAsync("car_procurement_order", "CPO-", token);
            order.Stage = ProcurementStage.Requisition;
            order.Status = ProcurementOrderStatus.Draft;
            order.PurchaseSource = string.IsNullOrWhiteSpace(r.PurchaseSource) ? ProcurementSources.Company : r.PurchaseSource.Trim().ToLowerInvariant();
            order.Date = r.Date == default ? DateTime.UtcNow : r.Date;
            order.SupplierName = supplier.NameAr; order.SupplierCr ??= supplier.CrNumber; order.SupplierVat ??= supplier.VatNumber;
            ResetSystemFields(order);
            Recalculate(order, (await CarTax.LoadAsync(_db, token)).Rate);
            _db.Add(order);
            await _db.SaveChangesAsync(token);
            await ProcurementCharges.SyncOrderFeesAsync(_db, order, actual: false, createMissing: true, token);
            return await GetAsync(order.Id, token);
        }, ct);

    public Task<CarProcurementOrderDto> UpdateAsync(Guid id, UpdateCarProcurementOrderDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var order = await _db.Set<CarProcurementOrder>().Include(o => o.Items).Include(o => o.ReceivedVins).AsSplitQuery()
                .FirstOrDefaultAsync(o => o.Id == id, token) ?? throw new NotFoundException(Messages.ProcurementOrderNotFound);
            if (order.Stage == ProcurementStage.Invoiced || order.Status is ProcurementOrderStatus.Invoiced or ProcurementOrderStatus.Closed)
                throw new ConflictException(Messages.CannotEditInvoicedOrderProcurement);
            if (order.Status == ProcurementOrderStatus.Rejected) throw new ConflictException(Messages.CannotEditRejectedOrder);
            if (order.ReceivedVins.Count > 0 && r.Items.Sum(i => i.Quantity) != order.Items.Sum(i => i.Quantity))
                throw new ConflictException(Messages.CannotChangeQtyAfterVinsReceived);
            var supplier = await ValidateAsync(r, token);

            var keep = _db.Entry(order).CurrentValues.Clone(); // لاستعادة الحقول التي تديرها الدورة
            Mapper.Apply(r, order);
            order.OrderNumber = (string)keep[nameof(CarProcurementOrder.OrderNumber)]!;
            order.Stage = (ProcurementStage)keep[nameof(CarProcurementOrder.Stage)]!;
            order.Status = (ProcurementOrderStatus)keep[nameof(CarProcurementOrder.Status)]!;
            order.PurchaseSource = (string?)keep[nameof(CarProcurementOrder.PurchaseSource)]; // المصدر لا يتغيّر بعد الإنشاء
            order.PurchaseInvoiceId = (Guid?)keep[nameof(CarProcurementOrder.PurchaseInvoiceId)];
            order.MatchedInvoiceNumber = (string?)keep[nameof(CarProcurementOrder.MatchedInvoiceNumber)];
            order.PdiInspectionPassed = (bool?)keep[nameof(CarProcurementOrder.PdiInspectionPassed)];
            order.ApprovalNotes = (string?)keep[nameof(CarProcurementOrder.ApprovalNotes)];
            order.RejectionReason = (string?)keep[nameof(CarProcurementOrder.RejectionReason)];
            order.SupplierName = supplier.NameAr;

            _db.RemoveRange(order.Items);
            order.Items.Clear();
            foreach (var i in r.Items) order.Items.Add(Mapper.Map<CarProcurementOrderItem>(i));
            foreach (var i in order.Items) { i.Id = Guid.NewGuid(); _db.Add(i); }
            Recalculate(order, (await CarTax.LoadAsync(_db, token)).Rate);
            await _db.SaveChangesAsync(token);

            // المصاريف والسيارات في الطريق تتبع الأمر: رسوم الحقلين تُحدَّث، والسيارات تُطابَق مع البنود الجديدة، ثم يُعاد التوزيع
            var received = order.ReceivedVins.Count > 0;
            await ProcurementCharges.SyncOrderFeesAsync(_db, order, actual: received, createMissing: !received, token);
            if (!received && order.Stage >= ProcurementStage.PurchaseOrder) await SyncInTransitVehiclesAsync(order, token);
            await ProcurementCharges.ReallocateAsync(_db, order.Id, token);
            return await GetAsync(id, token);
        }, ct);

    public Task<CarProcurementOrderDto> AdvanceStageAsync(Guid id, AdvanceProcurementRequestDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var order = await _db.Set<CarProcurementOrder>().Include(o => o.Items).Include(o => o.ReceivedVins).AsSplitQuery()
                .FirstOrDefaultAsync(o => o.Id == id, token) ?? throw new NotFoundException(Messages.ProcurementOrderNotFound);
            if (order.Status is ProcurementOrderStatus.Rejected or ProcurementOrderStatus.Closed)
                throw new ConflictException(Messages.OrderRejectedOrClosed);
            if (!Enum.IsDefined(r.TargetStage)) throw new ValidationFailedException(Messages.InvalidStage);
            if (r.TargetStage != order.Stage + 1)
                throw new ConflictException(string.Format(Messages.NextAllowedStageIs, (order.Stage == ProcurementStage.Invoiced ? Messages.NoneValue : (order.Stage + 1).ToString())));

            var fromStage = order.Stage;
            switch (r.TargetStage)
            {
                case ProcurementStage.RequisitionApproved:
                case ProcurementStage.RfqApproved:
                    order.Status = ProcurementOrderStatus.Approved;
                    if (!string.IsNullOrWhiteSpace(r.Notes))
                        order.ApprovalNotes = string.IsNullOrWhiteSpace(order.ApprovalNotes) ? r.Notes : $"{order.ApprovalNotes}\n{r.Notes}";
                    break;
                case ProcurementStage.PurchaseOrder:
                    order.Status = ProcurementOrderStatus.InProgress;
                    // السيارات تبدأ «في الطريق» من أمر الشراء بشاسيه مؤقت، فتُسجَّل عليها المصاريف قبل وصولها
                    await SyncInTransitVehiclesAsync(order, token);
                    await ProcurementCharges.ReallocateAsync(_db, order.Id, token);
                    break;
                case ProcurementStage.VinReceived:
                    if (!await ReceiveVinsAsync(order, r, token))
                    {
                        await CancelInTransitAsync(order.Id, token);
                        await _audit.LogAsync("PDI_REJECTED", nameof(CarProcurementOrder), order.Id.ToString(), r.RejectionReason ?? "فشل فحص الاستلام (PDI)", token);
                        await _db.SaveChangesAsync(token); // فشل فحص الاستلام: رُفض الأمر
                        return await GetAsync(id, token);
                    }
                    break;
                case ProcurementStage.Invoiced:
                    await InvoiceAsync(order, r, token);
                    break;
            }
            order.Stage = r.TargetStage;
            await _audit.LogAsync("STAGE_ADVANCED", nameof(CarProcurementOrder), order.Id.ToString(), $"من {fromStage} إلى {r.TargetStage}" + (string.IsNullOrWhiteSpace(r.Notes) ? "" : $" — {r.Notes}"), token);
            await _db.SaveChangesAsync(token);
            return await GetAsync(id, token);
        }, ct);

    public Task<CarProcurementOrderDto> RejectAsync(Guid id, RejectProcurementRequestDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            if (string.IsNullOrWhiteSpace(r.Reason)) throw new ValidationFailedException(Messages.RejectionReasonRequired);
            var order = await _db.Set<CarProcurementOrder>().FirstOrDefaultAsync(o => o.Id == id, token) ?? throw new NotFoundException(Messages.ProcurementOrderNotFound);
            if (order.Stage >= ProcurementStage.VinReceived || order.Status is ProcurementOrderStatus.Invoiced or ProcurementOrderStatus.Closed)
                throw new ConflictException(Messages.CannotRejectReceivedOrInvoicedOrder);
            order.Status = ProcurementOrderStatus.Rejected; order.RejectionReason = r.Reason;
            await CancelInTransitAsync(order.Id, token);
            await _audit.LogAsync("ORDER_REJECTED", nameof(CarProcurementOrder), order.Id.ToString(), r.Reason, token);
            await _db.SaveChangesAsync(token);
            return await GetAsync(id, token);
        }, ct);

    /// <summary>
    /// إلغاء شراء بعد مصاريف: ما صُرف فعلاً ورُسمل على سياراته (الباقية «شراء ملغى») يتحول لخسارة أو لمطالبة على المورد.
    /// القيد يُخرج المبلغ من «سيارات في الطريق» (أو المخزون) إلى حساب الخسارة أو المطالبات من إعدادات المعرض.
    /// </summary>
    public Task<CarProcurementOrderDto> SettleCancelledChargesAsync(Guid id, SettleCancelledChargesDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var order = await _db.Set<CarProcurementOrder>().FirstOrDefaultAsync(o => o.Id == id, token) ?? throw new NotFoundException(Messages.ProcurementOrderNotFound);
            if (order.Status != ProcurementOrderStatus.Rejected) throw new ConflictException(Messages.OrderNotCancelledForSettlement);
            if (order.CancelledChargesSettlement != null) throw new ConflictException(Messages.CancelledChargesAlreadySettled);
            var treatment = (r.Treatment ?? string.Empty).Trim().ToLowerInvariant();
            if (treatment is not ("loss" or "supplier_claim")) throw new ValidationFailedException(Messages.CancelledChargesTreatmentInvalid);

            var vehicleIds = await _db.Set<Vehicle>().Where(v => v.ProcurementOrderId == id && v.Status == VehicleStatus.Cancelled).Select(v => v.Id).ToListAsync(token);
            var allocations = await _db.Set<AdditionalChargeAllocation>().Include(a => a.Line)
                .Where(a => vehicleIds.Contains(a.VehicleId) && a.Line.Status == ChargeStatus.Actual && a.Line.Treatment == ChargeTreatment.Capitalize).ToListAsync(token);
            var amount = allocations.Sum(a => a.Amount);
            if (amount <= 0) throw new ConflictException(Messages.CancelledChargesNothingToSettle);

            var settings = await SalesCharges.SettingsAsync(_db, token);
            await ChargePosting.EnsureAccountsAsync(_db, settings, token);
            // ما رُحِّل والسيارة في الطريق ولم يُنقل يخرج من «في الطريق»، وغيره من المخزون؛ ما لم يُرحَّل لا قيد له
            var posted = allocations.Where(a => a.Line.JournalEntryId != null).ToList();
            var fromTransit = posted.Where(a => a.PostedInTransit && !a.ReclassedToInventory).Sum(a => a.Amount);
            var fromStock = posted.Sum(a => a.Amount) - fromTransit;
            var lines = new List<PostingLine> { new(treatment == "loss" ? settings.PurchaseCancellationLossAccount : settings.SupplierClaimsAccount, fromTransit + fromStock, 0) };
            if (fromTransit > 0) lines.Add(new(settings.VehiclesInTransitAccount, 0, fromTransit));
            if (fromStock > 0) lines.Add(new(settings.VehicleInventoryAccount, 0, fromStock));
            if (fromTransit + fromStock > 0)
            {
                var entry = await _posting.PostAsync(new GenericPostingRequest
                {
                    Date = DateTime.UtcNow,
                    Description = (treatment == "loss" ? "خسارة مصاريف شراء ملغى - أمر " : "مطالبة على المورد بمصاريف شراء ملغى - أمر ") + $"{order.OrderNumber} - {order.SupplierName}",
                    SourceType = "car_purchase_cancellation", SourceId = order.Id, SourceNumber = order.OrderNumber, Lines = lines,
                }, token);
                order.CancelledChargesEntryId = entry.JournalEntryId;
            }

            // الأسطر لم تعد تكلفة سيارة: تبقى سجلاً بمعالجة «مصروف» (القيد أعلاه أخرجها من الأصول)
            foreach (var line in allocations.Select(a => a.Line).Distinct())
            {
                line.Treatment = ChargeTreatment.Expense;
                line.Notes = (string.IsNullOrWhiteSpace(line.Notes) ? string.Empty : line.Notes + " - ") + (treatment == "loss" ? "خسارة شراء ملغى" : "مطالبة على المورد (شراء ملغى)");
            }
            foreach (var allocation in allocations) { allocation.PostedInTransit = false; allocation.ReclassedToInventory = false; }
            order.CancelledChargesSettlement = treatment; order.CancelledChargesAmount = amount;
            if (!string.IsNullOrWhiteSpace(r.Notes)) order.Notes = string.IsNullOrWhiteSpace(order.Notes) ? r.Notes : $"{order.Notes}\n{r.Notes}";
            await _db.SaveChangesAsync(token);
            await VehicleCostLedger.RecomputeAsync(_db, vehicleIds, token);
            await _audit.LogAsync("CANCELLED_PURCHASE_CHARGES_SETTLED", nameof(CarProcurementOrder), order.Id.ToString(), $"{treatment}: {amount:0.00}", token);
            await _db.SaveChangesAsync(token);
            return await GetAsync(id, token);
        }, ct);

    public Task<CarProcurementOrderDto> QuickPurchaseAsync(QuickCarPurchaseRequestDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            if (string.IsNullOrWhiteSpace(r.Vin)) throw new ValidationFailedException(Messages.VinRequired);
            if (r.PurchasePrice <= 0) throw new ValidationFailedException(Messages.PurchasePriceMustBePositive);

            var notes = new List<string> { $"شراء سريع لمركبة (دورة {r.PurchaseCycle}) — VIN {r.Vin.Trim().ToUpperInvariant()}" };
            if (!string.IsNullOrWhiteSpace(r.FinancingBankName)) notes.Add($"الجهة: {r.FinancingBankName}");
            if (!string.IsNullOrWhiteSpace(r.BankApprovalNumber)) notes.Add($"رقم موافقة البنك: {r.BankApprovalNumber}");
            if (!string.IsNullOrWhiteSpace(r.Notes)) notes.Add(r.Notes);

            var created = await CreateAsync(new CreateCarProcurementOrderDto
            {
                SupplierId = r.SupplierId, Date = r.Date ?? DateTime.UtcNow, PurchaseCycle = r.PurchaseCycle, PurchaseSource = r.PurchaseSource,
                PaymentType = r.PaymentType, CreditDays = r.CreditDays, Currency = r.Currency, ExchangeRate = r.ExchangeRate,
                Notes = string.Join("\n", notes), WarehouseLocation = r.WarehouseLocation,
                Items = new List<CarProcurementOrderItemDto>
                {
                    new()
                    {
                        BrandName = r.BrandName, ModelName = r.ModelName, TrimName = r.TrimName, Year = r.Year, Quantity = 1, UnitPrice = r.PurchasePrice,
                        Color = r.ColorExterior, Transmission = r.Transmission, FuelType = r.FuelType,
                    },
                },
            }, token);

            foreach (var stage in new[] { ProcurementStage.RequisitionApproved, ProcurementStage.Rfq, ProcurementStage.RfqApproved, ProcurementStage.PurchaseOrder })
                await AdvanceStageAsync(created.Id, new AdvanceProcurementRequestDto { TargetStage = stage }, token);

            await AdvanceStageAsync(created.Id, new AdvanceProcurementRequestDto
            {
                TargetStage = ProcurementStage.VinReceived, PdiInspectionPassed = true, WarehouseLocation = r.WarehouseLocation,
                Vins = new List<ReceiveVinDto>
                {
                    new() { Vin = r.Vin, EngineNumber = r.EngineNumber, CustomsCardNumber = r.CustomsCardNumber, ColorExterior = r.ColorExterior, ColorInterior = r.ColorInterior, SellingPrice = r.SellingPrice },
                },
            }, token);

            var done = await AdvanceStageAsync(created.Id, new AdvanceProcurementRequestDto
            {
                TargetStage = ProcurementStage.Invoiced, SupplierInvoiceNumber = r.SupplierInvoiceNumber, SupplierInvoiceDate = r.SupplierInvoiceDate,
            }, token);
            await _audit.LogAsync("QUICK_PURCHASE", nameof(CarProcurementOrder), created.Id.ToString(), $"شراء سريع: أمر {done.OrderNumber} — VIN {r.Vin}", token);
            return done;
        }, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var order = await _db.Set<CarProcurementOrder>().Include(o => o.Items).Include(o => o.ReceivedVins).AsSplitQuery()
                .FirstOrDefaultAsync(o => o.Id == id, token) ?? throw new NotFoundException(Messages.ProcurementOrderNotFound);
            if (order.ReceivedVins.Count > 0 || order.PurchaseInvoiceId != null || order.Stage >= ProcurementStage.PurchaseOrder && order.Status != ProcurementOrderStatus.Rejected)
                throw new ConflictException(Messages.OnlyEarlyOrdersCanBeDeleted);
            var charges = await ProcurementCharges.OrderLines(_db, order.Id).Include(l => l.Allocations).ToListAsync(token);
            if (charges.Any(l => l.Status == ChargeStatus.Actual)) throw new ConflictException(Messages.OrderHasActualCharges);
            _db.RemoveRange(charges.SelectMany(l => l.Allocations));
            _db.RemoveRange(charges);
            _db.RemoveRange(order.Items);
            _db.Remove(order);
            await _db.SaveChangesAsync(token);
        }, ct);

    // ---------------- الداخلي ----------------
    private async Task<Supplier> ValidateAsync(CreateCarProcurementOrderDto r, CancellationToken ct)
    {
        var errors = new List<string>();
        if (!PaymentTypes.Contains(r.PaymentType)) errors.Add(Messages.PaymentTypePrefix + string.Join(" | ", PaymentTypes));
        if (!Currencies.Contains(r.Currency)) errors.Add(Messages.CurrencyPrefix + string.Join(" | ", Currencies));
        if (!string.IsNullOrWhiteSpace(r.PurchaseCycle) && !PurchaseCycles.Contains(r.PurchaseCycle)) errors.Add(Messages.PurchaseCyclePrefix + string.Join(" | ", PurchaseCycles));
        if (!string.IsNullOrWhiteSpace(r.PurchaseSource) && !ProcurementSources.All.Contains(r.PurchaseSource.Trim().ToLowerInvariant()))
            errors.Add("مصدر الشراء غير صالح؛ القيم المسموحة: " + string.Join(" | ", ProcurementSources.All));
        if (r.ExchangeRate <= 0) errors.Add(Messages.ExchangeRateMustBePositive);
        if (r.Currency == "SAR" && r.ExchangeRate != 1) errors.Add(Messages.SarExchangeRateIsOne);
        if (r.PaymentType == "credit" && (r.CreditDays ?? 0) <= 0) errors.Add(Messages.CreditPaymentRequiresDays);
        if (r.Items.Count == 0) errors.Add(Messages.AtLeastOneLineRequired);
        foreach (var i in r.Items)
        {
            if (string.IsNullOrWhiteSpace(i.BrandName) || string.IsNullOrWhiteSpace(i.ModelName)) errors.Add(Messages.BrandAndModelRequiredPerLine);
            if (i.Quantity <= 0 || i.UnitPrice < 0) errors.Add(Messages.QtyPositivePriceNonNegative);
            if (i.Year is < 1980 or > 2100) errors.Add(Messages.LineModelYearInvalid);
        }
        if (r.CustomsDutyFee < 0 || r.PortStorageFee < 0) errors.Add(Messages.FeesCannotBeNegative);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors.Distinct().ToList());

        return await _db.Set<Supplier>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == r.SupplierId, ct)
            ?? throw new ValidationFailedException(Messages.SupplierNotFound);
    }

    /// <summary>الحقول التي تديرها الدورة فقط - لا تُقبل من العميل عند الإنشاء.</summary>
    private static void ResetSystemFields(CarProcurementOrder o)
    {
        o.ApprovalNotes = null; o.RejectionReason = null; o.PdiInspectionPassed = null; o.MatchedInvoiceNumber = null;
        o.DebitNoteNumber = null; o.DebitNoteReason = null; o.PurchaseInvoiceId = null; o.ReceivedVins.Clear();
    }

    private static void Recalculate(CarProcurementOrder o, decimal vatRate)
    {
        foreach (var i in o.Items)
        {
            i.TotalBeforeVat = DocumentPricing.Round(i.Quantity * i.UnitPrice);
            i.Total = i.TotalBeforeVat + DocumentPricing.Round(i.TotalBeforeVat * vatRate / 100m);
        }
        o.Subtotal = o.Items.Sum(i => i.TotalBeforeVat);
        o.VatTotal = DocumentPricing.Round(o.Subtotal * vatRate / 100m);
        o.GrandTotal = o.Subtotal + o.VatTotal;
    }

    /// <summary>يُنشئ المركبات من الشواسيهات المستلمة. يُرجع false إن رُفض الاستلام في فحص PDI.</summary>
    private async Task<bool> ReceiveVinsAsync(CarProcurementOrder order, AdvanceProcurementRequestDto r, CancellationToken ct)
    {
        if (r.PdiInspectionPassed == false)
        {
            if (string.IsNullOrWhiteSpace(r.RejectionReason)) throw new ValidationFailedException(Messages.InspectionRejectionReasonRequired);
            order.PdiInspectionPassed = false; order.Status = ProcurementOrderStatus.Rejected; order.RejectionReason = r.RejectionReason;
            return false;
        }
        if (r.PdiInspectionPassed != true) throw new ValidationFailedException(Messages.PdiConfirmationRequired);

        var expected = order.Items.Sum(i => i.Quantity);
        if (r.Vins.Count != expected) throw new ValidationFailedException(string.Format(Messages.VinCountMustMatchQuantities, r.Vins.Count, expected));

        var vins = r.Vins.Select(v => v.Vin.Trim().ToUpperInvariant()).ToList();
        if (vins.Any(v => !VehicleService.VinPattern.IsMatch(v))) throw new ValidationFailedException(Messages.VinInvalid);
        if (vins.Distinct().Count() != vins.Count) throw new ValidationFailedException(Messages.DuplicateVinsInRequest);
        var existing = await _db.Set<Vehicle>().AsNoTracking().Where(v => vins.Contains(v.ChassisNumber)).Select(v => v.ChassisNumber).ToListAsync(ct);
        if (existing.Count > 0) throw new ConflictException(Messages.VinsAlreadyRegisteredPrefix + string.Join(", ", existing));

        var tax = await CarTax.LoadAsync(_db, ct);
        var remaining = order.Items.ToDictionary(i => i.Id, i => i.Quantity);
        // سيارات الأمر «في الطريق» تستلم شواسيها الحقيقية وتدخل المخزون؛ أمر سابق لهذه الحالة لا سيارات له فتُنشأ الآن
        var inTransit = await _db.Set<Vehicle>().Where(v => v.ProcurementOrderId == order.Id && v.Status == VehicleStatus.InTransit)
            .OrderBy(v => v.CreatedAt).ThenBy(v => v.ChassisNumber).ToListAsync(ct);
        var received = new List<(Vehicle Vehicle, bool Priced)>();

        for (var n = 0; n < r.Vins.Count; n++)
        {
            var src = r.Vins[n];
            var item = src.ItemId.HasValue
                ? order.Items.FirstOrDefault(i => i.Id == src.ItemId) ?? throw new ValidationFailedException(Messages.LineNotInOrder)
                : order.Items.FirstOrDefault(i => remaining[i.Id] > 0)!;
            if (remaining[item.Id] <= 0) throw new ValidationFailedException(Messages.VinCountExceedsLineQty);
            remaining[item.Id]--;

            var vehicle = inTransit.FirstOrDefault(v => v.ProcurementOrderItemId == item.Id) ?? inTransit.FirstOrDefault();
            var isNew = vehicle == null;
            if (vehicle != null) inTransit.Remove(vehicle);
            else
            {
                vehicle = new Vehicle { ProcurementOrderId = order.Id };
                _db.Add(vehicle);
            }
            await ApplyItemAsync(vehicle, order, item, isNew, ct);
            vehicle.ChassisNumber = vins[n]; vehicle.IsTemporaryChassis = ChassisCheck.IsProvisional(vins[n]); vehicle.EngineNumber = src.EngineNumber; vehicle.CustomsCardNumber = src.CustomsCardNumber;
            vehicle.ColorExterior = src.ColorExterior ?? item.Color ?? vehicle.ColorExterior;
            vehicle.ColorInterior = src.ColorInterior ?? vehicle.ColorInterior;
            vehicle.Status = VehicleStatus.Available;
            vehicle.ReceivedAt = DateTime.UtcNow;
            vehicle.Location = r.WarehouseLocation ?? order.WarehouseLocation ?? vehicle.Location;
            if (src.SellingPrice.HasValue) vehicle.SellingPrice = src.SellingPrice.Value;
            VehicleService.Recalculate(vehicle, tax);
            received.Add((vehicle, src.SellingPrice.HasValue));
            order.ReceivedVins.Add(new CarProcurementOrderVin
            {
                ItemId = item.Id, Vin = vins[n], EngineNumber = src.EngineNumber, CustomsCardNumber = src.CustomsCardNumber, VehicleId = vehicle.Id,
            });
        }
        order.PdiInspectionPassed = true;
        order.WarehouseLocation = r.WarehouseLocation ?? order.WarehouseLocation;
        order.Status = ProcurementOrderStatus.Received;
        await _db.SaveChangesAsync(ct);

        // رسوم الأمر (جمارك، أرضيات) تصير فعلية عند الاستلام وتُوزَّع على سياراته: مجموع الحصص = الرسوم تماماً
        await ProcurementCharges.SyncOrderFeesAsync(_db, order, actual: true, createMissing: true, ct);
        await ProcurementCharges.ReallocateAsync(_db, order.Id, ct);
        // استعلامات تم التي سبقت الشراء على هذه الشواسي تُنقل إلى تكلفة سياراتها
        await TammCharges.AttachPurchasedAsync(_db, _posting, received.Select(x => x.Vehicle.Id).ToList(), ct);
        // ما قُيِّد على السيارات وهي في الطريق يُنقل إلى مخزون السيارات بالتكلفة
        await ChargePosting.ReclassOnReceiptAsync(_db, _posting, order, received.Select(x => x.Vehicle.Id).ToList(), ct);
        foreach (var (vehicle, priced) in received.Where(x => !x.Priced))
        {
            vehicle.SellingPrice = vehicle.TotalCost; // السعر النهائي يُحدَّد لاحقاً من شاشة المركبات
            VehicleService.Recalculate(vehicle, tax);
        }
        return true;
    }

    /// <summary>بيانات المركبة من بند الأمر (الماركة، الموديل، ثمن الشراء بالعملة الأساسية). الافتراضيات تُضبط للمركبة الجديدة فقط.</summary>
    private async Task ApplyItemAsync(Vehicle vehicle, CarProcurementOrder order, CarProcurementOrderItem item, bool isNew, CancellationToken ct)
    {
        var fx = order.ExchangeRate <= 0 ? 1 : order.ExchangeRate;
        var brandId = await _db.Set<CarBrand>().Where(b => b.NameAr == item.BrandName).Select(b => (Guid?)b.Id).FirstOrDefaultAsync(ct);
        var modelId = brandId == null ? null : await _db.Set<CarModel>().Where(m => m.BrandId == brandId && m.NameAr == item.ModelName).Select(m => (Guid?)m.Id).FirstOrDefaultAsync(ct);
        vehicle.ProcurementOrderItemId = item.Id;
        vehicle.BrandId = brandId; vehicle.ModelId = modelId; vehicle.BrandNameAr = item.BrandName; vehicle.ModelNameAr = item.ModelName;
        vehicle.TrimNameAr = item.TrimName; vehicle.Trim = item.TrimName; vehicle.Year = item.Year;
        vehicle.FuelType = Enum.TryParse<FuelType>(item.FuelType, true, out var fuel) ? fuel : FuelType.Petrol;
        vehicle.Transmission = Enum.TryParse<TransmissionType>(item.Transmission, true, out var tr) ? tr : TransmissionType.Automatic;
        vehicle.PurchasePrice = DocumentPricing.Round(item.UnitPrice * fx);
        if (!isNew) return;
        vehicle.Condition = VehicleCondition.New; vehicle.VatMode = VatMode.Standard_15;
        vehicle.ColorExterior = item.Color ?? string.Empty; vehicle.ColorInterior = string.Empty;
        vehicle.Location = order.WarehouseLocation ?? string.Empty;
        vehicle.SellingPrice = vehicle.PurchasePrice ?? 0;
    }

    /// <summary>
    /// يطابق سيارات الأمر «في الطريق» مع بنوده: سيارة لكل وحدة بشاسيه مؤقت. تعديل البنود يعيد استخدام السيارات القائمة،
    /// ينشئ الناقص ويحذف الزائد؛ وسيارة زائدة عليها مصاريف خارج توزيع الأمر تمنع التعديل.
    /// </summary>
    private async Task SyncInTransitVehiclesAsync(CarProcurementOrder order, CancellationToken ct)
    {
        var existing = await _db.Set<Vehicle>().Where(v => v.ProcurementOrderId == order.Id && v.Status == VehicleStatus.InTransit)
            .OrderBy(v => v.CreatedAt).ThenBy(v => v.ChassisNumber).ToListAsync(ct);
        var slots = order.Items.SelectMany(i => Enumerable.Repeat(i, i.Quantity)).ToList();
        var tax = await CarTax.LoadAsync(_db, ct);
        var newVins = slots.Count > existing.Count ? await _vehicles.GenerateProvisionalVinsAsync(slots.Count - existing.Count, ct) : new List<string>();

        for (var i = 0; i < slots.Count; i++)
        {
            var isNew = i >= existing.Count;
            var vehicle = isNew ? new Vehicle { ChassisNumber = newVins[i - existing.Count], IsTemporaryChassis = true, ProcurementOrderId = order.Id, Status = VehicleStatus.InTransit } : existing[i];
            if (isNew) _db.Add(vehicle);
            await ApplyItemAsync(vehicle, order, slots[i], isNew, ct);
            VehicleService.Recalculate(vehicle, tax);
        }

        var surplus = existing.Skip(slots.Count).ToList();
        if (surplus.Count > 0)
        {
            var surplusIds = surplus.Select(v => v.Id).ToList();
            var allocations = await _db.Set<AdditionalChargeAllocation>().Include(a => a.Line).ThenInclude(l => l.Allocations)
                .Where(a => surplusIds.Contains(a.VehicleId)).ToListAsync(ct);
            if (allocations.Any(a => a.Line.DocumentType != ChargeDocumentTypes.ProcurementOrder || a.Line.DocumentId != order.Id
                || (a.Line.AllocationMethod == ChargeAllocationMethod.Manual && a.Line.Source != ChargeSource.OrderFields)))
                throw new ConflictException(Messages.OrderItemsLockedByCharges);
            // أسطر الأمر التي شملت سيارة محذوفة يُعاد توزيعها كاملةً على السيارات الباقية
            foreach (var line in allocations.Select(a => a.Line).Distinct())
            {
                _db.RemoveRange(line.Allocations);
                line.Allocations.Clear();
            }
            _db.RemoveRange(surplus);
        }
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// إلغاء الشراء قبل الاستلام: سيارة في الطريق لم يُصرف عليها فعلياً تُحذف مع تقديراتها، ومن صُرف عليها تبقى «شراء ملغى»
    /// سجلاً لمصاريفها الفعلية. تقديرات الأمر تُلغى.
    /// </summary>
    private async Task CancelInTransitAsync(Guid orderId, CancellationToken ct)
    {
        var vehicles = await _db.Set<Vehicle>().Where(v => v.ProcurementOrderId == orderId && v.Status == VehicleStatus.InTransit).ToListAsync(ct);
        var ids = vehicles.Select(v => v.Id).ToList();
        var allocations = await _db.Set<AdditionalChargeAllocation>().Include(a => a.Line).Where(a => ids.Contains(a.VehicleId)).ToListAsync(ct);
        foreach (var vehicle in vehicles)
        {
            var mine = allocations.Where(a => a.VehicleId == vehicle.Id).ToList();
            if (mine.Any(a => a.Line.Status == ChargeStatus.Actual)) { vehicle.Status = VehicleStatus.Cancelled; continue; }
            _db.RemoveRange(mine);
            _db.RemoveRange(mine.Select(a => a.Line).Where(l => l.DocumentType == ChargeDocumentTypes.Vehicle && l.DocumentId == vehicle.Id).Distinct());
            _db.Remove(vehicle);
        }
        foreach (var line in await ProcurementCharges.OrderLines(_db, orderId).Where(l => l.Status == ChargeStatus.Estimated).ToListAsync(ct))
            line.Status = ChargeStatus.Cancelled;
        await _db.SaveChangesAsync(ct);
    }

    private async Task InvoiceAsync(CarProcurementOrder order, AdvanceProcurementRequestDto r, CancellationToken ct)
    {
        if (order.Status != ProcurementOrderStatus.Received || order.ReceivedVins.Count == 0)
            throw new ConflictException(Messages.CannotInvoiceBeforeVinsReceived);
        if (order.PurchaseInvoiceId != null) throw new ConflictException(Messages.OrderAlreadyInvoiced);

        var fx = order.ExchangeRate <= 0 ? 1 : order.ExchangeRate;
        var settings = await SalesCharges.SettingsAsync(_db, ct);
        await ChargePosting.EnsureAccountsAsync(_db, settings, ct);
        var credit = order.PaymentType != "cash";
        var invoice = await _invoices.CreateAsync(new CreateInvoiceDto
        {
            Kind = InvoiceKind.Purchase, InvoiceType = InvoiceType.TaxInvoice,
            PartyId = order.SupplierId, PartyName = order.SupplierName, PartyVatNumber = order.SupplierVat,
            PaymentMethod = credit ? PaymentMethod.Credit : PaymentMethod.BankTransfer, Status = "posted",
            CurrencyCode = "SAR", ExchangeRate = 1,
            Notes = $"فاتورة شراء سيارات - أمر {order.OrderNumber}",
            ReferenceType = "car_procurement_order", ReferenceId = order.Id, ReferenceNumber = order.OrderNumber,
            InventoryAccountCode = settings.VehicleInventoryAccount,
            Items = order.Items.Select(i => new InvoiceItemDto
            {
                ItemId = Guid.Empty,
                ItemName = $"{i.BrandName} {i.ModelName} {i.TrimName} {i.Year}".Replace("  ", " ").Trim(),
                Unit = "سيارة", Quantity = i.Quantity, UnitPrice = DocumentPricing.Round(i.UnitPrice * fx), VatRate = settings.StandardVatRate,
            }).ToList(),
        }, ct);

        // رسوم الأمر الفعلية (جمارك + أرضيات موانئ) داخلة في تكلفة المركبات، فتُرحَّل إلى مخزون السيارات مقابل مستحقات:
        // بتاريخ الفاتورة، وبمجموع أسطرها (= مجموع حصص السيارات تماماً). سطر استبدله سند تكاليف يُرحَّل مع سنده.
        var feeLines = await ProcurementCharges.OrderLines(_db, order.Id).Where(l => l.Source == ChargeSource.OrderFields).ToListAsync(ct);
        var unposted = feeLines.Where(l => l.Status == ChargeStatus.Actual && l.JournalEntryId == null).ToList();
        // أمر استُلم قبل سجل المصاريف لا أسطر له: رسومه في حقليه كما حُمّلت على مركباته
        var landed = feeLines.Count > 0 ? unposted.Sum(l => l.CostAmount) : (order.CustomsDutyFee ?? 0) + (order.PortStorageFee ?? 0);
        if (landed > 0)
        {
            var posted = await _posting.PostAsync(new GenericPostingRequest
            {
                Date = invoice.IssueDate, Description = $"تكاليف محمّلة على مركبات أمر {order.OrderNumber} (جمارك ورسوم موانئ)",
                SourceType = "car_landed_costs", SourceId = order.Id, SourceNumber = order.OrderNumber,
                Lines = new List<PostingLine>
                {
                    new(settings.VehicleInventoryAccount, landed, 0),
                    new(settings.AccruedChargesAccount, 0, landed),
                },
            }, ct);
            foreach (var line in unposted) line.JournalEntryId = posted.JournalEntryId;
        }

        order.PurchaseInvoiceId = invoice.Id;
        order.MatchedInvoiceNumber = string.IsNullOrWhiteSpace(r.SupplierInvoiceNumber) ? invoice.InvoiceNumber : r.SupplierInvoiceNumber;
        order.SupplierInvoiceDate = r.SupplierInvoiceDate ?? DateTime.UtcNow;
        order.Status = ProcurementOrderStatus.Invoiced;
    }
}
