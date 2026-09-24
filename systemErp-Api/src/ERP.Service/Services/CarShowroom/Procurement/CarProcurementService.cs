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
    private static readonly Regex VinPattern = new("^[A-HJ-NPR-Z0-9]{17}$", RegexOptions.Compiled);
    private const decimal VatRate = 15m;

    private readonly ErpDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly IInvoiceService _invoices;
    private readonly IAccountingPostingService _posting;
    private readonly ITransactionRunner _tx;

    public CarProcurementService(ErpDbContext db, INumberSequenceService numbers, IInvoiceService invoices, IAccountingPostingService posting, ITransactionRunner tx)
    {
        _db = db; _numbers = numbers; _invoices = invoices; _posting = posting; _tx = tx;
    }

    public async Task<PagedResult<CarProcurementOrderDto>> ListAsync(PaginationParams p, CancellationToken ct = default)
    {
        var q = _db.Set<CarProcurementOrder>().AsNoTracking().AsQueryable();
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

    public async Task<CarProcurementOrderDto> GetAsync(Guid id, CancellationToken ct = default)
        => Mapper.Map<CarProcurementOrderDto>(await _db.Set<CarProcurementOrder>().AsNoTracking()
            .Include(o => o.Items).Include(o => o.ReceivedVins).AsSplitQuery().FirstOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new NotFoundException("أمر الشراء غير موجود"));

    public Task<CarProcurementOrderDto> CreateAsync(CreateCarProcurementOrderDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var supplier = await ValidateAsync(r, token);
            var order = Mapper.Map<CarProcurementOrder>(r);
            order.OrderNumber = await _numbers.NextAsync("car_procurement_order", "CPO-", token);
            order.Stage = ProcurementStage.Requisition;
            order.Status = ProcurementOrderStatus.Draft;
            order.Date = r.Date == default ? DateTime.UtcNow : r.Date;
            order.SupplierName = supplier.NameAr; order.SupplierCr ??= supplier.CrNumber; order.SupplierVat ??= supplier.VatNumber;
            ResetSystemFields(order);
            Recalculate(order);
            _db.Add(order);
            await _db.SaveChangesAsync(token);
            return await GetAsync(order.Id, token);
        }, ct);

    public Task<CarProcurementOrderDto> UpdateAsync(Guid id, UpdateCarProcurementOrderDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var order = await _db.Set<CarProcurementOrder>().Include(o => o.Items).Include(o => o.ReceivedVins).AsSplitQuery()
                .FirstOrDefaultAsync(o => o.Id == id, token) ?? throw new NotFoundException("أمر الشراء غير موجود");
            if (order.Stage == ProcurementStage.Invoiced || order.Status is ProcurementOrderStatus.Invoiced or ProcurementOrderStatus.Closed)
                throw new ConflictException("لا يمكن تعديل أمر تمت فوترته.");
            if (order.Status == ProcurementOrderStatus.Rejected) throw new ConflictException("لا يمكن تعديل أمر مرفوض.");
            if (order.ReceivedVins.Count > 0 && r.Items.Sum(i => i.Quantity) != order.Items.Sum(i => i.Quantity))
                throw new ConflictException("لا تغيّر كميات أمر استُلمت شواسيهه.");
            var supplier = await ValidateAsync(r, token);

            var keep = _db.Entry(order).CurrentValues.Clone(); // لاستعادة الحقول التي تديرها الدورة
            Mapper.Apply(r, order);
            order.OrderNumber = (string)keep[nameof(CarProcurementOrder.OrderNumber)]!;
            order.Stage = (ProcurementStage)keep[nameof(CarProcurementOrder.Stage)]!;
            order.Status = (ProcurementOrderStatus)keep[nameof(CarProcurementOrder.Status)]!;
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
            Recalculate(order);
            await _db.SaveChangesAsync(token);
            return await GetAsync(id, token);
        }, ct);

    public Task<CarProcurementOrderDto> AdvanceStageAsync(Guid id, AdvanceProcurementRequestDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var order = await _db.Set<CarProcurementOrder>().Include(o => o.Items).Include(o => o.ReceivedVins).AsSplitQuery()
                .FirstOrDefaultAsync(o => o.Id == id, token) ?? throw new NotFoundException("أمر الشراء غير موجود");
            if (order.Status is ProcurementOrderStatus.Rejected or ProcurementOrderStatus.Closed)
                throw new ConflictException("الأمر مرفوض أو مغلق ولا يتقدم في الدورة.");
            if (!Enum.IsDefined(r.TargetStage)) throw new ValidationFailedException("مرحلة غير صالحة.");
            if (r.TargetStage != order.Stage + 1)
                throw new ConflictException($"المرحلة التالية المسموحة هي: {(order.Stage == ProcurementStage.Invoiced ? "لا يوجد" : (order.Stage + 1).ToString())}.");

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
                    break;
                case ProcurementStage.VinReceived:
                    if (!await ReceiveVinsAsync(order, r, token))
                    {
                        await _db.SaveChangesAsync(token); // فشل فحص الاستلام: رُفض الأمر
                        return await GetAsync(id, token);
                    }
                    break;
                case ProcurementStage.Invoiced:
                    await InvoiceAsync(order, r, token);
                    break;
            }
            order.Stage = r.TargetStage;
            await _db.SaveChangesAsync(token);
            return await GetAsync(id, token);
        }, ct);

    public async Task<CarProcurementOrderDto> RejectAsync(Guid id, RejectProcurementRequestDto r, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Reason)) throw new ValidationFailedException("سبب الرفض مطلوب.");
        var order = await _db.Set<CarProcurementOrder>().FirstOrDefaultAsync(o => o.Id == id, ct) ?? throw new NotFoundException("أمر الشراء غير موجود");
        if (order.Stage >= ProcurementStage.VinReceived || order.Status is ProcurementOrderStatus.Invoiced or ProcurementOrderStatus.Closed)
            throw new ConflictException("لا يمكن رفض أمر استُلمت مركباته أو فُوتر.");
        order.Status = ProcurementOrderStatus.Rejected; order.RejectionReason = r.Reason;
        await _db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var order = await _db.Set<CarProcurementOrder>().Include(o => o.Items).Include(o => o.ReceivedVins).AsSplitQuery()
                .FirstOrDefaultAsync(o => o.Id == id, token) ?? throw new NotFoundException("أمر الشراء غير موجود");
            if (order.ReceivedVins.Count > 0 || order.PurchaseInvoiceId != null || order.Stage >= ProcurementStage.PurchaseOrder && order.Status != ProcurementOrderStatus.Rejected)
                throw new ConflictException("لا يُحذف إلا أمر لم يصل لمرحلة أمر الشراء (أو مرفوض) ولم تُستلم مركباته.");
            _db.RemoveRange(order.Items);
            _db.Remove(order);
            await _db.SaveChangesAsync(token);
        }, ct);

    // ---------------- الداخلي ----------------
    private async Task<Supplier> ValidateAsync(CreateCarProcurementOrderDto r, CancellationToken ct)
    {
        var errors = new List<string>();
        if (!PaymentTypes.Contains(r.PaymentType)) errors.Add("نوع الدفع: " + string.Join(" | ", PaymentTypes));
        if (!Currencies.Contains(r.Currency)) errors.Add("العملة: " + string.Join(" | ", Currencies));
        if (r.ExchangeRate <= 0) errors.Add("سعر الصرف يجب أن يكون موجباً.");
        if (r.Currency == "SAR" && r.ExchangeRate != 1) errors.Add("سعر صرف الريال 1.");
        if (r.PaymentType == "credit" && (r.CreditDays ?? 0) <= 0) errors.Add("الدفع الآجل يتطلب عدد أيام الائتمان.");
        if (r.Items.Count == 0) errors.Add("يجب إدخال بند واحد على الأقل.");
        foreach (var i in r.Items)
        {
            if (string.IsNullOrWhiteSpace(i.BrandName) || string.IsNullOrWhiteSpace(i.ModelName)) errors.Add("الماركة والموديل مطلوبان لكل بند.");
            if (i.Quantity <= 0 || i.UnitPrice < 0) errors.Add("الكمية موجبة والسعر غير سالب.");
            if (i.Year is < 1980 or > 2100) errors.Add("سنة صنع البند غير صالحة.");
        }
        if (r.CustomsDutyFee < 0 || r.PortStorageFee < 0) errors.Add("الرسوم لا تكون سالبة.");
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors.Distinct().ToList());

        return await _db.Set<Supplier>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == r.SupplierId, ct)
            ?? throw new ValidationFailedException("المورد غير موجود.");
    }

    /// <summary>الحقول التي تديرها الدورة فقط - لا تُقبل من العميل عند الإنشاء.</summary>
    private static void ResetSystemFields(CarProcurementOrder o)
    {
        o.ApprovalNotes = null; o.RejectionReason = null; o.PdiInspectionPassed = null; o.MatchedInvoiceNumber = null;
        o.DebitNoteNumber = null; o.DebitNoteReason = null; o.PurchaseInvoiceId = null; o.ReceivedVins.Clear();
    }

    private static void Recalculate(CarProcurementOrder o)
    {
        foreach (var i in o.Items)
        {
            i.TotalBeforeVat = DocumentPricing.Round(i.Quantity * i.UnitPrice);
            i.Total = i.TotalBeforeVat + DocumentPricing.Round(i.TotalBeforeVat * VatRate / 100m);
        }
        o.Subtotal = o.Items.Sum(i => i.TotalBeforeVat);
        o.VatTotal = DocumentPricing.Round(o.Subtotal * VatRate / 100m);
        o.GrandTotal = o.Subtotal + o.VatTotal;
    }

    /// <summary>يُنشئ المركبات من الشواسيهات المستلمة. يُرجع false إن رُفض الاستلام في فحص PDI.</summary>
    private async Task<bool> ReceiveVinsAsync(CarProcurementOrder order, AdvanceProcurementRequestDto r, CancellationToken ct)
    {
        if (r.PdiInspectionPassed == false)
        {
            if (string.IsNullOrWhiteSpace(r.RejectionReason)) throw new ValidationFailedException("سبب رفض الفحص مطلوب.");
            order.PdiInspectionPassed = false; order.Status = ProcurementOrderStatus.Rejected; order.RejectionReason = r.RejectionReason;
            return false;
        }
        if (r.PdiInspectionPassed != true) throw new ValidationFailedException("يلزم تأكيد اجتياز فحص الاستلام (PDI).");

        var expected = order.Items.Sum(i => i.Quantity);
        if (r.Vins.Count != expected) throw new ValidationFailedException($"عدد الشواسيهات ({r.Vins.Count}) يجب أن يساوي إجمالي الكميات المطلوبة ({expected}).");

        var vins = r.Vins.Select(v => v.Vin.Trim().ToUpperInvariant()).ToList();
        if (vins.Any(v => !VinPattern.IsMatch(v))) throw new ValidationFailedException("رقم شاسيه غير صالح (17 خانة بدون I/O/Q).");
        if (vins.Distinct().Count() != vins.Count) throw new ValidationFailedException("أرقام الشواسيه مكررة في الطلب.");
        var existing = await _db.Set<Vehicle>().AsNoTracking().Where(v => vins.Contains(v.ChassisNumber)).Select(v => v.ChassisNumber).ToListAsync(ct);
        if (existing.Count > 0) throw new ConflictException("شواسيهات مسجّلة مسبقاً: " + string.Join(", ", existing));

        var remaining = order.Items.ToDictionary(i => i.Id, i => i.Quantity);
        var landedCosts = (order.CustomsDutyFee ?? 0) + (order.PortStorageFee ?? 0);
        var perVehicleExtra = expected == 0 ? 0 : DocumentPricing.Round(landedCosts / expected);
        var fx = order.ExchangeRate <= 0 ? 1 : order.ExchangeRate;

        for (var n = 0; n < r.Vins.Count; n++)
        {
            var src = r.Vins[n];
            var item = src.ItemId.HasValue
                ? order.Items.FirstOrDefault(i => i.Id == src.ItemId) ?? throw new ValidationFailedException("بند غير موجود في الأمر.")
                : order.Items.FirstOrDefault(i => remaining[i.Id] > 0)!;
            if (remaining[item.Id] <= 0) throw new ValidationFailedException("عدد الشواسيهات لبند يتجاوز كميته.");
            remaining[item.Id]--;

            var brandId = await _db.Set<CarBrand>().Where(b => b.NameAr == item.BrandName).Select(b => (Guid?)b.Id).FirstOrDefaultAsync(ct);
            var modelId = brandId == null ? null : await _db.Set<CarModel>().Where(m => m.BrandId == brandId && m.NameAr == item.ModelName).Select(m => (Guid?)m.Id).FirstOrDefaultAsync(ct);
            var purchase = DocumentPricing.Round(item.UnitPrice * fx);

            var vehicle = new Vehicle
            {
                ChassisNumber = vins[n], EngineNumber = src.EngineNumber, CustomsCardNumber = src.CustomsCardNumber,
                BrandId = brandId, ModelId = modelId, BrandNameAr = item.BrandName, ModelNameAr = item.ModelName, TrimNameAr = item.TrimName, Trim = item.TrimName,
                Year = item.Year, ColorExterior = src.ColorExterior ?? item.Color ?? string.Empty, ColorInterior = src.ColorInterior ?? string.Empty,
                Condition = VehicleCondition.New,
                FuelType = Enum.TryParse<FuelType>(item.FuelType, true, out var fuel) ? fuel : FuelType.Petrol,
                Transmission = Enum.TryParse<TransmissionType>(item.Transmission, true, out var tr) ? tr : TransmissionType.Automatic,
                PurchasePrice = purchase, AdditionalCosts = perVehicleExtra, PreparationCost = 0,
                VatMode = VatMode.Standard_15, Status = VehicleStatus.Available,
                Location = r.WarehouseLocation ?? order.WarehouseLocation ?? string.Empty, ProcurementOrderId = order.Id,
            };
            vehicle.SellingPrice = src.SellingPrice ?? purchase + perVehicleExtra; // السعر النهائي يُحدَّد لاحقاً من شاشة المركبات
            VehicleService.Recalculate(vehicle);
            _db.Add(vehicle);
            order.ReceivedVins.Add(new CarProcurementOrderVin
            {
                ItemId = item.Id, Vin = vins[n], EngineNumber = src.EngineNumber, CustomsCardNumber = src.CustomsCardNumber, VehicleId = vehicle.Id,
            });
        }
        order.PdiInspectionPassed = true;
        order.WarehouseLocation = r.WarehouseLocation ?? order.WarehouseLocation;
        order.Status = ProcurementOrderStatus.Received;
        return true;
    }

    private async Task InvoiceAsync(CarProcurementOrder order, AdvanceProcurementRequestDto r, CancellationToken ct)
    {
        if (order.Status != ProcurementOrderStatus.Received || order.ReceivedVins.Count == 0)
            throw new ConflictException("لا تُفوتر الدورة قبل استلام الشواسيهات.");
        if (order.PurchaseInvoiceId != null) throw new ConflictException("الأمر فُوتر مسبقاً.");

        var fx = order.ExchangeRate <= 0 ? 1 : order.ExchangeRate;
        var credit = order.PaymentType != "cash";
        var invoice = await _invoices.CreateAsync(new CreateInvoiceDto
        {
            Kind = InvoiceKind.Purchase, InvoiceType = InvoiceType.TaxInvoice,
            PartyId = order.SupplierId, PartyName = order.SupplierName, PartyVatNumber = order.SupplierVat,
            PaymentMethod = credit ? PaymentMethod.Credit : PaymentMethod.BankTransfer, Status = "posted",
            CurrencyCode = "SAR", ExchangeRate = 1,
            Notes = $"فاتورة شراء سيارات - أمر {order.OrderNumber}",
            ReferenceType = "car_procurement_order", ReferenceId = order.Id, ReferenceNumber = order.OrderNumber,
            InventoryAccountCode = DefaultAccounts.VehicleInventory,
            Items = order.Items.Select(i => new InvoiceItemDto
            {
                ItemId = Guid.Empty,
                ItemName = $"{i.BrandName} {i.ModelName} {i.TrimName} {i.Year}".Replace("  ", " ").Trim(),
                Unit = "سيارة", Quantity = i.Quantity, UnitPrice = DocumentPricing.Round(i.UnitPrice * fx), VatRate = VatRate,
            }).ToList(),
        }, ct);

        // التكاليف المحمّلة (جمارك + رسوم موانئ) داخلة في تكلفة المركبات، فتُرحَّل إلى مخزون السيارات مقابل مستحقات.
        var landed = (order.CustomsDutyFee ?? 0) + (order.PortStorageFee ?? 0);
        if (landed > 0)
            await _posting.PostAsync(new GenericPostingRequest
            {
                Date = DateTime.UtcNow, Description = $"تكاليف محمّلة على مركبات أمر {order.OrderNumber} (جمارك ورسوم موانئ)",
                SourceType = "car_landed_costs", SourceId = order.Id, SourceNumber = order.OrderNumber,
                Lines = new List<PostingLine>
                {
                    new(DefaultAccounts.VehicleInventory, landed, 0),
                    new(DefaultAccounts.AccruedLandedCosts, 0, landed),
                },
            }, ct);

        order.PurchaseInvoiceId = invoice.Id;
        order.MatchedInvoiceNumber = string.IsNullOrWhiteSpace(r.SupplierInvoiceNumber) ? invoice.InvoiceNumber : r.SupplierInvoiceNumber;
        order.SupplierInvoiceDate = r.SupplierInvoiceDate ?? DateTime.UtcNow;
        order.Status = ProcurementOrderStatus.Invoiced;
    }
}
