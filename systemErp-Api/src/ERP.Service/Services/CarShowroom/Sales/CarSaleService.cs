using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using ERP.Service.Data;
using ERP.Service.Services.Accounting;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

/// <summary>
/// دورة عقد بيع السيارة (5 مراحل). المنطق التشغيلي (حجز المركبة، التخصيص، التسليم، حساب ضريبة ZATCA بنمط
/// هامش الربح) هنا؛ والفوترة والقيد تمرّان عبر محرك الفوترة المركزي بحسابات إيراد/تكلفة/مخزون السيارات.
/// </summary>
public class CarSaleService : ICarSaleService
{
    private static readonly string[] PaymentMethods = { "cash", "credit", "bank_transfer", "bank_finance", "pos_mada" };

    private readonly ErpDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly IInvoiceService _invoices;
    private readonly ICurrentUser _user;
    private readonly ITransactionRunner _tx;

    public CarSaleService(ErpDbContext db, INumberSequenceService numbers, IInvoiceService invoices, ICurrentUser user, ITransactionRunner tx)
    {
        _db = db; _numbers = numbers; _invoices = invoices; _user = user; _tx = tx;
    }

    public async Task<PagedResult<CarSalesContractDto>> ListAsync(PaginationParams p, CancellationToken ct = default)
    {
        var q = _db.Set<CarSalesContract>().AsNoTracking().AsQueryable();
        if (Enum.TryParse<SalesContractStatus>(p.Status, true, out var st)) q = q.Where(c => c.Status == st);
        if (p.StartDate.HasValue) q = q.Where(c => c.Date >= p.StartDate);
        if (p.EndDate.HasValue) q = q.Where(c => c.Date <= p.EndDate);
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(c => c.ContractNumber.Contains(t) || c.BuyerName.Contains(t) || c.Vin.Contains(t) || c.BuyerNationalIdOrCr.Contains(t));
        }
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(c => c.Date).ThenByDescending(c => c.ContractNumber)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);
        return new PagedResult<CarSalesContractDto>
        {
            Items = items.Select(Mapper.Map<CarSalesContractDto>).ToList(),
            TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    public async Task<CarSalesContractDto> GetAsync(Guid id, CancellationToken ct = default)
        => Mapper.Map<CarSalesContractDto>(await _db.Set<CarSalesContract>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException("عقد البيع غير موجود"));

    public Task<CarSalesContractDto> CreateAsync(CreateCarSalesContractDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var contract = Mapper.Map<CarSalesContract>(r);
            ResetSystemFields(contract);
            var vehicle = await LoadVehicleAsync(r.VehicleId, null, token);
            await ValidateAsync(contract, token);
            ApplyVehicleAndPricing(contract, vehicle);

            contract.ContractNumber = await _numbers.NextAsync("car_sales_contract", "CSC-", token);
            contract.Date = r.Date == default ? DateTime.UtcNow : r.Date;
            contract.Status = SalesContractStatus.Draft;
            contract.SalespersonName ??= _user.Name;
            _db.Add(contract);
            await _db.SaveChangesAsync(token);
            return Mapper.Map<CarSalesContractDto>(contract);
        }, ct);

    public Task<CarSalesContractDto> UpdateAsync(Guid id, UpdateCarSalesContractDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var contract = await _db.Set<CarSalesContract>().FirstOrDefaultAsync(c => c.Id == id, token) ?? throw new NotFoundException("عقد البيع غير موجود");
            if (contract.Status is SalesContractStatus.Invoiced or SalesContractStatus.Cancelled or SalesContractStatus.Delivered)
                throw new ConflictException("لا يمكن تعديل عقد سُلّم أو فُوتر أو أُلغي.");
            if (r.VehicleId != contract.VehicleId && contract.Status != SalesContractStatus.Draft)
                throw new ConflictException("تغيير المركبة يتم بالتخصيص بعد الاعتماد أو قبله في المسودة.");

            var keep = _db.Entry(contract).CurrentValues.Clone();
            Mapper.Apply(r, contract);
            RestoreSystemFields(contract, keep);
            var vehicle = await LoadVehicleAsync(contract.VehicleId, contract.Id, token);
            await ValidateAsync(contract, token);
            ApplyVehicleAndPricing(contract, vehicle);
            await _db.SaveChangesAsync(token);
            return Mapper.Map<CarSalesContractDto>(contract);
        }, ct);

    public Task<CarSalesContractDto> AdvanceStatusAsync(Guid id, AdvanceSalesContractRequestDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var c = await _db.Set<CarSalesContract>().FirstOrDefaultAsync(x => x.Id == id, token) ?? throw new NotFoundException("عقد البيع غير موجود");
            if (c.Status is SalesContractStatus.Invoiced or SalesContractStatus.Cancelled)
                throw new ConflictException("العقد منتهٍ (مفوتر أو ملغى).");
            if (r.TargetStatus == SalesContractStatus.Cancelled) throw new ValidationFailedException("للإلغاء استخدم نقطة الإلغاء.");
            if (r.TargetStatus != c.Status + 1)
                throw new ConflictException($"المرحلة التالية المسموحة: {c.Status + 1}.");

            switch (r.TargetStatus)
            {
                case SalesContractStatus.Approved: await ApproveAsync(c, token); break;
                case SalesContractStatus.Allocated: await AllocateAsync(c, r, token); break;
                case SalesContractStatus.Delivered: Deliver(c, r); break;
                case SalesContractStatus.Invoiced: await InvoiceAsync(c, token); break;
            }
            if (!string.IsNullOrWhiteSpace(r.Notes)) c.Notes = string.IsNullOrWhiteSpace(c.Notes) ? r.Notes : $"{c.Notes}\n{r.Notes}";
            c.Status = r.TargetStatus;
            await _db.SaveChangesAsync(token);
            return Mapper.Map<CarSalesContractDto>(c);
        }, ct);

    public Task<CarSalesContractDto> CancelAsync(Guid id, string? reason, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var c = await _db.Set<CarSalesContract>().FirstOrDefaultAsync(x => x.Id == id, token) ?? throw new NotFoundException("عقد البيع غير موجود");
            if (c.Status == SalesContractStatus.Invoiced) throw new ConflictException("لا يُلغى عقد فُوتر؛ أنشئ مرتجع مبيعات.");
            if (c.Status == SalesContractStatus.Cancelled) throw new ConflictException("العقد ملغى مسبقاً.");
            var vehicle = await _db.Set<Vehicle>().FirstOrDefaultAsync(v => v.Id == c.VehicleId, token);
            if (vehicle is { Status: VehicleStatus.Reserved }) vehicle.Status = VehicleStatus.Available; // فك الحجز
            c.Status = SalesContractStatus.Cancelled;
            if (!string.IsNullOrWhiteSpace(reason)) c.Notes = string.IsNullOrWhiteSpace(c.Notes) ? $"سبب الإلغاء: {reason}" : $"{c.Notes}\nسبب الإلغاء: {reason}";
            await _db.SaveChangesAsync(token);
            return Mapper.Map<CarSalesContractDto>(c);
        }, ct);

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var c = await _db.Set<CarSalesContract>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("عقد البيع غير موجود");
        if (c.Status is not (SalesContractStatus.Draft or SalesContractStatus.Cancelled)) throw new ConflictException("يُحذف العقد المسودة أو الملغى فقط.");
        _db.Remove(c);
        await _db.SaveChangesAsync(ct);
    }

    // ---------------- الانتقالات ----------------
    private async Task ApproveAsync(CarSalesContract c, CancellationToken ct)
    {
        var errors = new List<string>();
        if (c.CycleType == CarSalesCycleType.BankLease)
        {
            if (string.IsNullOrWhiteSpace(c.FinancingBankName)) errors.Add("دورة التمويل البنكي تتطلب اسم البنك.");
            if ((c.FinancedAmount ?? 0) <= 0) errors.Add("مبلغ التمويل مطلوب.");
        }
        if (c.CycleType == CarSalesCycleType.Installment)
        {
            if ((c.MonthlyInstallment ?? 0) <= 0 || (c.FinanceTenorMonths ?? 0) <= 0) errors.Add("القسط الشهري ومدة التقسيط مطلوبان.");
        }
        if (c.PaymentMethod is "credit" or "bank_finance" && c.CustomerId == null) errors.Add("البيع الآجل/التمويل يتطلب ربط العقد بعميل.");
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        var vehicle = await LoadVehicleAsync(c.VehicleId, c.Id, ct);
        vehicle.Status = VehicleStatus.Reserved; // حجز لهذا العقد
    }

    private async Task AllocateAsync(CarSalesContract c, AdvanceSalesContractRequestDto r, CancellationToken ct)
    {
        var vehicle = await _db.Set<Vehicle>().FirstAsync(v => v.Id == c.VehicleId, ct);
        if (r.VehicleId.HasValue && r.VehicleId != c.VehicleId)
        {
            var replacement = await LoadVehicleAsync(r.VehicleId.Value, c.Id, ct);
            if (vehicle.Status == VehicleStatus.Reserved) vehicle.Status = VehicleStatus.Available; // فك حجز المركبة القديمة
            replacement.Status = VehicleStatus.Reserved;
            c.VehicleId = replacement.Id;
            ApplyVehicleAndPricing(c, replacement);
            vehicle = replacement;
        }
        if (vehicle.Status != VehicleStatus.Reserved) vehicle.Status = VehicleStatus.Reserved;

        c.AllocatedVin = vehicle.ChassisNumber; c.AllocatedAt = DateTime.UtcNow;
        c.PdiInspectionNotes = r.PdiInspectionNotes ?? c.PdiInspectionNotes;
        if (r.PdiChecklist != null)
        {
            var pdi = Mapper.Map<VehiclePdiChecklist>(r.PdiChecklist);
            pdi.InspectedAt = DateTime.UtcNow; pdi.InspectedBy = _user.Name;
            vehicle.PdiChecklist = pdi;
        }
    }

    private static void Deliver(CarSalesContract c, AdvanceSalesContractRequestDto r)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(r.HandoverProtocolNumber)) errors.Add("رقم محضر التسليم مطلوب.");
        if (string.IsNullOrWhiteSpace(r.HandoverSignee)) errors.Add("اسم المستلم (الموقّع) مطلوب.");
        if (string.IsNullOrWhiteSpace(r.HandoverSigneeNationalId)) errors.Add("هوية المستلم مطلوبة.");
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
        c.HandoverProtocolNumber = r.HandoverProtocolNumber; c.HandoverSignee = r.HandoverSignee;
        c.HandoverSigneeNationalId = r.HandoverSigneeNationalId;
        c.DeliveredAt = DateTime.UtcNow; c.DeliveryDate = r.DeliveryDate ?? DateTime.UtcNow; c.DeliveryLocation = r.DeliveryLocation ?? c.DeliveryLocation;
    }

    private async Task InvoiceAsync(CarSalesContract c, CancellationToken ct)
    {
        if (c.InvoiceId != null) throw new ConflictException("العقد فُوتر مسبقاً.");
        var vehicle = await _db.Set<Vehicle>().FirstAsync(v => v.Id == c.VehicleId, ct);
        var customer = c.CustomerId.HasValue ? await _db.Set<Customer>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == c.CustomerId, ct) : null;
        var net = c.NetPriceBeforeVat ?? c.SellingPrice - (c.DiscountAmount ?? 0);

        var (method, splits) = PaymentPlan(c);
        var invoice = await _invoices.CreateAsync(new CreateInvoiceDto
        {
            Kind = InvoiceKind.Sales,
            InvoiceType = string.IsNullOrWhiteSpace(customer?.VatNumber) ? InvoiceType.Simplified : InvoiceType.TaxInvoice,
            PartyId = c.CustomerId, PartyName = c.BuyerName, PartyVatNumber = customer?.VatNumber, PartyPhone = c.BuyerPhone, PartyEmail = c.BuyerEmail,
            PartyAddress = c.BuyerAddress, PaymentMethod = method, IsSplitPayment = splits.Count > 0, PaymentSplits = splits,
            Status = "posted",
            Notes = $"فاتورة مبيعات سيارة - عقد {c.ContractNumber} - VIN {c.Vin}",
            ReferenceType = "car_sales_contract", ReferenceId = c.Id, ReferenceNumber = c.ContractNumber,
            RevenueAccountCode = DefaultAccounts.CarSalesRevenue, CogsAccountCode = DefaultAccounts.CarCogs, InventoryAccountCode = DefaultAccounts.VehicleInventory,
            Items = new List<InvoiceItemDto>
            {
                new()
                {
                    ItemId = Guid.Empty, ItemName = $"{c.VehicleDescription} (VIN: {c.Vin})", Unit = "سيارة", Quantity = 1,
                    UnitPrice = net, UnitCost = c.CostPrice,
                    VatRate = c.VatMode == VatMode.Standard_15 ? 15 : 0,
                    VatAmountOverride = c.VatAmount, // الضريبة المحسوبة بنمط الاحتساب (قياسي/هامش الربح/معفى)
                },
            },
        }, ct);

        c.InvoiceId = invoice.Id;
        vehicle.Status = VehicleStatus.Sold;
    }

    /// <summary>طريقة الدفع في الفاتورة: التمويل/الآجل مع دفعة مقدمة تُقسَّم إلى نقد + ذمم.</summary>
    private static (PaymentMethod Method, List<InvoicePaymentSplitDto> Splits) PaymentPlan(CarSalesContract c)
    {
        var total = c.TotalWithVat;
        var down = c.DownPaymentAmount ?? 0;
        if (c.PaymentMethod is "credit" or "bank_finance" && down > 0 && down < total)
            return (PaymentMethod.Credit, new List<InvoicePaymentSplitDto>
            {
                new() { Method = PaymentMethod.Cash, Amount = down, Reference = c.DownPaymentReceiptNo },
                new() { Method = PaymentMethod.Credit, Amount = total - down, Reference = c.BankApprovalNumber },
            });
        return (c.PaymentMethod switch
        {
            "cash" => PaymentMethod.Cash,
            "bank_transfer" => PaymentMethod.BankTransfer,
            "pos_mada" => PaymentMethod.BankCard,
            _ => PaymentMethod.Credit,
        }, new List<InvoicePaymentSplitDto>());
    }

    // ---------------- التحقق والتسعير ----------------
    private async Task<Vehicle> LoadVehicleAsync(Guid vehicleId, Guid? contractId, CancellationToken ct)
    {
        var vehicle = await _db.Set<Vehicle>().FirstOrDefaultAsync(v => v.Id == vehicleId, ct) ?? throw new ValidationFailedException("المركبة غير موجودة.");
        if (vehicle.Status == VehicleStatus.Sold) throw new ConflictException("المركبة مباعة.");
        if (vehicle.Status == VehicleStatus.Reserved)
        {
            var reservedByThis = contractId.HasValue && await _db.Set<CarSalesContract>().AnyAsync(x => x.Id == contractId && x.VehicleId == vehicleId
                && x.Status != SalesContractStatus.Cancelled, ct);
            var reservedByOther = await _db.Set<CarSalesContract>().AnyAsync(x => x.VehicleId == vehicleId && x.Id != contractId
                && x.Status != SalesContractStatus.Cancelled && x.Status != SalesContractStatus.Draft, ct);
            if (reservedByOther || (!reservedByThis && !contractId.HasValue)) throw new ConflictException("المركبة محجوزة لعقد آخر.");
        }
        return vehicle;
    }

    private async Task ValidateAsync(CarSalesContract c, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(c.BuyerName)) errors.Add("اسم المشتري مطلوب.");
        if (string.IsNullOrWhiteSpace(c.BuyerNationalIdOrCr)) errors.Add("هوية/سجل المشتري مطلوب.");
        if (string.IsNullOrWhiteSpace(c.BuyerPhone)) errors.Add("جوال المشتري مطلوب.");
        if (!PaymentMethods.Contains(c.PaymentMethod)) errors.Add("طريقة الدفع: " + string.Join(" | ", PaymentMethods));
        if (c.SellingPrice < 0 || (c.DiscountAmount ?? 0) < 0 || (c.DiscountAmount ?? 0) > c.SellingPrice) errors.Add("السعر والخصم غير صالحين.");
        if (!string.IsNullOrWhiteSpace(c.BuyerEmail) && !c.BuyerEmail.Contains('@')) errors.Add("البريد الإلكتروني غير صالح.");
        if ((c.DownPaymentAmount ?? 0) < 0) errors.Add("الدفعة المقدمة لا تكون سالبة.");
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        if (c.CustomerId.HasValue && !await _db.Set<Customer>().AnyAsync(x => x.Id == c.CustomerId, ct))
            throw new ValidationFailedException("العميل غير موجود.");
        if (c.FinancingBankId.HasValue && !await _db.Set<BankEntity>().AnyAsync(x => x.Id == c.FinancingBankId, ct))
            throw new ValidationFailedException("البنك المموِّل غير موجود.");
    }

    /// <summary>ينسخ بيانات المركبة ويحسب التسعير والضريبة في الخادم (التكلفة من المركبة لا من العميل).</summary>
    private static void ApplyVehicleAndPricing(CarSalesContract c, Vehicle v)
    {
        c.VehicleId = v.Id; c.Vin = v.ChassisNumber; c.EngineNumber = v.EngineNumber; c.CustomsCardNumber = v.CustomsCardNumber;
        c.VehicleDescription = $"{v.BrandNameAr} {v.ModelNameAr} {v.TrimNameAr} {v.Year}".Replace("  ", " ").Trim();
        c.BrandNameAr = v.BrandNameAr; c.ModelNameAr = v.ModelNameAr; c.Year = v.Year;
        c.ColorExterior = v.ColorExterior; c.ColorInterior = v.ColorInterior; c.Condition = v.Condition;
        c.Mileage = v.MileageKm; c.FuelType = v.FuelType.ToString().ToLowerInvariant();
        c.Transmission = v.Transmission.ToString().ToLowerInvariant(); c.Location = v.Location;

        if (c.SellingPrice <= 0) c.SellingPrice = v.SellingPrice;
        var net = c.SellingPrice - (c.DiscountAmount ?? 0);
        if (v.MinSellingPrice.HasValue && net < v.MinSellingPrice)
            throw new ValidationFailedException($"سعر البيع بعد الخصم ({net:0.00}) أقل من الحد الأدنى للمركبة ({v.MinSellingPrice:0.00}).");

        c.CostPrice = v.TotalCost;
        var vat = CarVat.Calculate(c.CostPrice, net, c.VatMode);
        c.NetPriceBeforeVat = net; c.ProfitMargin = vat.ProfitMargin; c.ProfitMarginVat = vat.ProfitMarginVat;
        c.VatAmount = vat.VatAmount; c.TotalWithVat = vat.PriceWithVat;
        if ((c.DownPaymentAmount ?? 0) > c.TotalWithVat) throw new ValidationFailedException("الدفعة المقدمة تتجاوز إجمالي العقد.");
    }

    private static void ResetSystemFields(CarSalesContract c)
    {
        c.Status = SalesContractStatus.Draft; c.AllocatedVin = null; c.AllocatedAt = null; c.DeliveredAt = null;
        c.HandoverProtocolNumber = null; c.HandoverSignee = null; c.HandoverSigneeNationalId = null; c.InvoiceId = null;
    }

    private static void RestoreSystemFields(CarSalesContract c, Microsoft.EntityFrameworkCore.ChangeTracking.PropertyValues o)
    {
        c.ContractNumber = (string)o[nameof(CarSalesContract.ContractNumber)]!;
        c.Status = (SalesContractStatus)o[nameof(CarSalesContract.Status)]!;
        c.AllocatedVin = (string?)o[nameof(CarSalesContract.AllocatedVin)];
        c.AllocatedAt = (DateTime?)o[nameof(CarSalesContract.AllocatedAt)];
        c.DeliveredAt = (DateTime?)o[nameof(CarSalesContract.DeliveredAt)];
        c.HandoverProtocolNumber = (string?)o[nameof(CarSalesContract.HandoverProtocolNumber)];
        c.HandoverSignee = (string?)o[nameof(CarSalesContract.HandoverSignee)];
        c.HandoverSigneeNationalId = (string?)o[nameof(CarSalesContract.HandoverSigneeNationalId)];
        c.InvoiceId = (Guid?)o[nameof(CarSalesContract.InvoiceId)];
    }
}
