using System.Text.RegularExpressions;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using ERP.Service.Data;
using ERP.Service.Services.Accounting;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

public class VehicleService : CrudService<Vehicle, VehicleDto, CreateVehicleDto, UpdateVehicleDto>, IVehicleService
{
    // ISO 3779: 17 خانة، بدون الأحرف I و O و Q
    internal static readonly Regex VinPattern = new("^[A-HJ-NPR-Z0-9]{17}$", RegexOptions.Compiled);

    private readonly INumberSequenceService _numbers;
    private readonly ICostVisibility _cost;
    private readonly ERP.Core.Contracts.Accounting.IAccountingPostingService _posting;
    /// <summary>يُحمَّل قبل أي إرجاع لـ DTO؛ الافتراضي الحجب.</summary>
    private bool _canViewCost;

    private readonly IAuditService _audit;

    public VehicleService(ErpDbContext db, INumberSequenceService numbers, ICostVisibility cost, ERP.Core.Contracts.Accounting.IAccountingPostingService posting,
        IAuditService audit) : base(db)
    {
        _numbers = numbers; _cost = cost; _posting = posting; _audit = audit;
    }

    // ---------- أرقام الشاسيه: التحقق الجماعي واستبدال المؤقت ----------
    public Task<List<ChassisIssueDto>> ValidateChassisAsync(ValidateChassisRequestDto request, CancellationToken ct = default)
        => ChassisCheck.RunAsync(Db, request.Vins.Select(ChassisCheck.Normalize).ToList(), request.InvoiceId, ct);

    public Task<int> ReplaceChassisAsync(List<ReplaceChassisDto> items, CancellationToken ct = default)
        => new TransactionRunner(Db).RunAsync(async token =>
        {
            if (items.Count == 0) return 0;
            var ids = items.Select(i => i.VehicleId).ToList();
            var vehicles = await Db.Set<Vehicle>().Where(v => ids.Contains(v.Id)).ToDictionaryAsync(v => v.Id, token);
            var vins = items.Select(i => ChassisCheck.Normalize(i.ChassisNumber)).ToList();
            for (var i = 0; i < items.Count; i++)
            {
                if (!vehicles.TryGetValue(items[i].VehicleId, out var v) || ids.IndexOf(v.Id) != i) throw new ValidationFailedException(Messages.VehicleNotFound);
                // بطاقة المسودة يُعدَّل شاسيهها من فاتورتها
                if (v.Status == VehicleStatus.Draft) throw new ConflictException(Messages.DraftVehicleManagedByInvoice);
                if (!v.IsTemporaryChassis) throw new ConflictException(string.Format(Messages.ChassisNotTemporary, v.ChassisNumber));
                if (ChassisCheck.IsProvisional(vins[i])) throw new ValidationFailedException(string.Format(Messages.ReplacementMustBeReal, vins[i]));
            }
            var issues = await ChassisCheck.RunAsync(Db, vins, null, token);
            if (issues.Count > 0)
            {
                var messages = issues.Select(x => $"{(x.Vin.Length == 0 ? "—" : x.Vin)}: {x.Message}").ToList();
                throw new ValidationFailedException(messages[0], messages);
            }

            var replaced = new List<string>();
            for (var i = 0; i < items.Count; i++)
            {
                var v = vehicles[items[i].VehicleId];
                replaced.Add($"{v.ChassisNumber} ← {vins[i]}");
                v.ChassisNumber = vins[i]; v.IsTemporaryChassis = false;
            }
            await PropagateChassisAsync(vehicles.Values, token);
            await SaveAsync(token);
            // استعلامات تم التي سبقت معرفة الشاسيه الحقيقي تُنقل إلى تكلفة سيارته
            var inStock = vehicles.Values.Where(v => v.Status is VehicleStatus.Available or VehicleStatus.Reserved).Select(v => v.Id).ToList();
            if (inStock.Count > 0) await TammCharges.AttachPurchasedAsync(Db, _posting, inStock, token);
            await _audit.LogAsync("CHASSIS_REPLACED", nameof(Vehicle), items.Count == 1 ? items[0].VehicleId.ToString() : string.Empty,
                $"استبدال {items.Count} شاسيه مؤقت: " + string.Join("، ", replaced.Take(20)) + (replaced.Count > 20 ? " …" : string.Empty), token);
            return items.Count;
        }, ct);

    /// <summary>رقم الشاسيه منسوخ في أسطر الفواتير وسجل استلام الأوامر: يُحدَّث معهما (بلا حفظ).</summary>
    private async Task PropagateChassisAsync(IReadOnlyCollection<Vehicle> vehicles, CancellationToken ct)
    {
        var ids = vehicles.Select(v => (Guid?)v.Id).ToList();
        var byId = vehicles.ToDictionary(v => v.Id);
        foreach (var line in await Db.Set<InvoiceVehicleLine>().Where(l => ids.Contains(l.VehicleId)).ToListAsync(ct))
        {
            line.Vin = byId[line.VehicleId!.Value].ChassisNumber; line.IsTemporaryChassis = byId[line.VehicleId.Value].IsTemporaryChassis;
        }
        foreach (var received in await Db.Set<CarProcurementOrderVin>().Where(r => ids.Contains(r.VehicleId)).ToListAsync(ct))
            received.Vin = byId[received.VehicleId!.Value].ChassisNumber;
    }
    protected override string Label => Messages.LabelVehicle;

    // ---------- حجب التكلفة والربح عمّن لا يملك صلاحية car-cost-view ----------
    private async Task LoadCostVisibilityAsync(CancellationToken ct) => _canViewCost = await _cost.CanViewAsync(ct);

    protected override VehicleDto ToDto(Vehicle entity)
    {
        var dto = base.ToDto(entity);
        dto.VatCategory = CarVat.CategoryOf(entity.VatMode);
        if (_canViewCost) return dto;
        dto.PurchasePrice = null; dto.AdditionalCosts = null; dto.PreparationCost = null; dto.TotalCost = null;
        // في هامش الربح الضريبة = الهامش × 15/115، فإظهارها يكشف التكلفة
        if (CarVat.IsMarginScheme(entity.VatMode)) dto.VatAmount = null;
        return dto;
    }

    public override async Task<PagedResult<VehicleDto>> ListAsync(PaginationParams p, CancellationToken ct = default)
    {
        await LoadCostVisibilityAsync(ct);
        return await base.ListAsync(p, ct);
    }

    public override async Task<DevExtreme.AspNet.Data.ResponseModel.LoadResult> LoadAsync(DataSourceLoadOptions options, CancellationToken ct = default)
    {
        await LoadCostVisibilityAsync(ct);
        return await base.LoadAsync(options, ct);
    }

    public override async Task<VehicleDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        await LoadCostVisibilityAsync(ct);
        return await base.GetAsync(id, ct);
    }

    public override async Task<VehicleDto> CreateAsync(CreateVehicleDto dto, CancellationToken ct = default)
    {
        await LoadCostVisibilityAsync(ct);
        return await base.CreateAsync(dto, ct);
    }

    public override async Task<VehicleDto> UpdateAsync(Guid id, UpdateVehicleDto dto, CancellationToken ct = default)
    {
        await LoadCostVisibilityAsync(ct);
        return await base.UpdateAsync(id, dto, ct);
    }

    /// <summary>حاسبة الضريبة: costPrice أساس الهامش كما يرسله المتصل، والنسبة من إعدادات المعرض.</summary>
    public async Task<CarVatResult> CalculateVatAsync(CalculateCarVatRequestDto r, CancellationToken ct = default)
        => CarVat.Calculate(r.CostPrice, r.SellingPrice, r.Mode, (await CarTax.LoadAsync(Db, ct)).Rate);

    /// <summary>أقصى عدد يُولَّد في طلب واحد (يطابق أقصى دفعة في الواجهة).</summary>
    public const int MaxGeneratedVins = 1000;
    /// <summary>بادئة الأرقام المؤقتة: تميّزها عن الشواسيه الحقيقية (WMI حقيقي لا يبدأ بـ TMP للمصنّعين المعتمدين هنا).</summary>
    public const string ProvisionalVinPrefix = "TMP";

    public async Task<List<string>> GenerateProvisionalVinsAsync(int count, CancellationToken ct = default)
    {
        if (count < 1 || count > MaxGeneratedVins) throw new ValidationFailedException(string.Format(Messages.VinGenerateCountInvalid, MaxGeneratedVins));
        var result = new List<string>(count);
        while (result.Count < count)
        {
            var need = count - result.Count;
            var first = await _numbers.ReserveAsync("vin_provisional", ProvisionalVinPrefix, need, ct);
            var batch = Enumerable.Range(0, need).Select(i => ProvisionalVinPrefix + (first + i).ToString().PadLeft(17 - ProvisionalVinPrefix.Length, '0')).ToList();
            // أي تصادم مع شاسيه موجود (إدخال يدوي بنفس الصيغة) يُتخطى ويُحجز بدله رقم جديد
            var taken = (await FindExistingChassisNumbersAsync(batch, ct)).ToHashSet();
            result.AddRange(batch.Where(v => !taken.Contains(v)));
        }
        return result;
    }

    public async Task<List<string>> FindExistingChassisNumbersAsync(List<string> vins, CancellationToken ct = default)
    {
        var normalized = vins.Select(v => v.Trim().ToUpperInvariant()).ToList();
        return await Db.Set<Vehicle>().AsNoTracking().Where(v => normalized.Contains(v.ChassisNumber))
            .Select(v => v.ChassisNumber).ToListAsync(ct);
    }

    public async Task<PagedResult<VehicleDto>> ListByProcurementOrderAsync(Guid orderId, PaginationParams p, CancellationToken ct = default)
    {
        await LoadCostVisibilityAsync(ct);
        var query = Db.Set<Vehicle>().AsNoTracking().Where(v => v.ProcurementOrderId == orderId);
        if (!string.IsNullOrWhiteSpace(p.SearchTerm)) query = ApplySearch(query, p.SearchTerm.Trim());

        var total = await query.CountAsync(ct);
        var entities = await query.OrderByDescending(v => v.CreatedAt)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);

        return new PagedResult<VehicleDto>
        {
            Items = entities.Select(ToDto).ToList(),
            TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    protected override IQueryable<Vehicle> ApplySearch(IQueryable<Vehicle> q, string t)
        => q.Where(v => v.ChassisNumber.Contains(t) || v.BrandNameAr.Contains(t) || v.ModelNameAr.Contains(t) || (v.PlateNumber != null && v.PlateNumber.Contains(t)));

    protected override IQueryable<Vehicle> ApplyFilters(IQueryable<Vehicle> q, PaginationParams p)
        // in_transit أو InTransit. بطاقات مسودات فواتير الشراء لا تظهر إلا بطلبها صراحةً (status=draft)
        => Enum.TryParse<VehicleStatus>(p.Status?.Replace("_", ""), true, out var st) ? q.Where(v => v.Status == st) : q.Where(v => v.Status != VehicleStatus.Draft);

    public async Task<List<VehicleDto>> ListAvailableAsync(string? search, CancellationToken ct = default)
    {
        await LoadCostVisibilityAsync(ct);
        await VehicleReservations.ExpireDueAsync(Db, ct); // حجز انتهت مدته يعيد سيارته للعرض
        var q = Db.Set<Vehicle>().AsNoTracking().Where(v => v.Status == VehicleStatus.Available);
        if (!string.IsNullOrWhiteSpace(search)) q = ApplySearch(q, search.Trim());
        return (await q.OrderBy(v => v.BrandNameAr).ThenBy(v => v.ModelNameAr).Take(200).ToListAsync(ct)).Select(ToDto).ToList();
    }

    protected override async Task ValidateAsync(CreateVehicleDto d, Vehicle? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        d.ChassisNumber = (d.ChassisNumber ?? string.Empty).Trim().ToUpperInvariant();
        if (!VinPattern.IsMatch(d.ChassisNumber)) errors.Add(Messages.VinFormatInvalid);
        if (d.Year is < 1980 or > 2100) errors.Add(Messages.ModelYearInvalid);
        if (d.SellingPrice < 0 || d.PurchasePrice < 0 || d.AdditionalCosts < 0 || d.PreparationCost < 0 || d.MinSellingPrice < 0)
            errors.Add(Messages.PricesAndCostsCannotBeNegative);
        if (d.MinSellingPrice > d.SellingPrice) errors.Add(Messages.MinPriceAboveSellingPrice);
        if (d.VolumeCbm < 0 || d.WeightKg < 0) errors.Add(Messages.VehicleDimensionsInvalid);
        if (d.Condition == VehicleCondition.New && d.MileageKm is > 500) errors.Add(Messages.NewCarMileageLimit);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        if (await Db.Set<Vehicle>().AnyAsync(v => v.ChassisNumber == d.ChassisNumber && (existing == null || v.Id != existing.Id), ct))
            throw new ConflictException(Messages.VinAlreadyRegistered);

        if (d.BrandId.HasValue && !await Db.Set<CarBrand>().AnyAsync(b => b.Id == d.BrandId, ct)) throw new ValidationFailedException(Messages.BrandNotFound);
        if (d.ModelId.HasValue && !await Db.Set<CarModel>().AnyAsync(m => m.Id == d.ModelId, ct)) throw new ValidationFailedException(Messages.ModelNotFound);
        if (d.TrimId.HasValue && !await Db.Set<CarTrim>().AnyAsync(t => t.Id == d.TrimId, ct)) throw new ValidationFailedException(Messages.TrimNotFound);
        if (d.ModelId.HasValue && d.BrandId.HasValue && !await Db.Set<CarModel>().AnyAsync(m => m.Id == d.ModelId && m.BrandId == d.BrandId, ct))
            throw new ValidationFailedException(Messages.ModelNotInBrand);
        if (d.TrimId.HasValue && d.ModelId.HasValue && !await Db.Set<CarTrim>().AnyAsync(t => t.Id == d.TrimId && t.ModelId == d.ModelId, ct))
            throw new ValidationFailedException(Messages.TrimNotInModel);
        // التكلفة تُشتق من سجل المصاريف بعد الإنشاء: حقولها للقراءة فقط (قيمة فارغة أو مطابقة تُقبل، وتغييرها يُرفض صراحةً)
        if (existing != null && (Differs(d.PurchasePrice, existing.PurchasePrice) || Differs(d.AdditionalCosts, existing.AdditionalCosts)
            || Differs(d.PreparationCost, existing.PreparationCost)))
            throw new ConflictException(Messages.VehicleCostFieldsReadOnly);
        if (existing != null && existing.Status != VehicleStatus.Available && d.VatMode != existing.VatMode)
            throw new ConflictException(Messages.CannotChangeVatModeReservedOrSold);
        // شروط هامش الربح (سيارة مستعملة مؤهلة): مستعملة، ولم تُخصم ضريبة مدخلات عند شرائها. بطاقة قديمة على حالها لا تُرفض عند تعديل غيرها
        var marginChosen = CarVat.IsMarginScheme(d.VatMode) && (existing == null || !CarVat.IsMarginScheme(existing.VatMode));
        if (CarVat.IsMarginScheme(d.VatMode) && d.Condition != VehicleCondition.Used && (marginChosen || existing!.Condition == VehicleCondition.Used))
            throw new ValidationFailedException(Messages.MarginSchemeRequiresUsedVehicle);
        if (marginChosen && await PurchasedWithInputVatAsync(existing, d.ProcurementOrderId, ct))
            throw new ValidationFailedException(Messages.MarginSchemeNotAllowedWithInputVat);
    }

    /// <summary>هل خُصمت ضريبة مدخلات عند شراء المركبة (سطر فاتورة شراء قياسي 15% أو أمر توريد عليه ضريبة)؟</summary>
    private async Task<bool> PurchasedWithInputVatAsync(Vehicle? existing, Guid? procurementOrderId, CancellationToken ct)
    {
        if (existing?.PurchaseInvoiceId is Guid invoiceId
            && await Db.Set<InvoiceVehicleLine>().AnyAsync(l => l.InvoiceId == invoiceId && l.VehicleId == existing.Id && l.VatMode == VatMode.Standard_15, ct))
            return true;
        var orderId = existing?.ProcurementOrderId ?? procurementOrderId;
        return orderId.HasValue && await Db.Set<CarProcurementOrder>().AnyAsync(o => o.Id == orderId && o.VatTotal > 0, ct);
    }

    private static bool Differs(decimal? requested, decimal? current) => requested.HasValue && requested.Value != (current ?? 0);

    protected override async Task OnCreatingAsync(Vehicle e, CreateVehicleDto d, CancellationToken ct)
    {
        e.Status = VehicleStatus.Available;
        e.IsTemporaryChassis = ChassisCheck.IsProvisional(e.ChassisNumber);
        e.ReceivedAt = DateTime.UtcNow;
        await FillDenormalizedAsync(e, ct);
        Recalculate(e, await CarTax.LoadAsync(Db, ct));
    }

    /// <summary>تكلفة أُدخلت مع المركبة (مخزون قائم، استيراد Excel) تُثبَّت أسطر «رصيد افتتاحي» في سجل المصاريف.</summary>
    protected override async Task OnCreatedAsync(Vehicle e, CancellationToken ct)
    {
        await VehicleCostLedger.EnsureOpeningAsync(Db, new[] { e.Id }, ct);
        // استعلامات تم التي سبقت شراءها (على رقم شاسيهها) تُنقل من مصروف عام إلى تكلفتها
        await TammCharges.AttachPurchasedAsync(Db, _posting, new[] { e.Id }, ct);
    }

    protected override async Task OnUpdatingAsync(Vehicle e, UpdateVehicleDto d, CancellationToken ct)
    {
        var o = Db.Entry(e).OriginalValues;
        e.Status = o.GetValue<VehicleStatus>(nameof(Vehicle.Status)); // الحالة تتغير بدورة البيع فقط
        e.ProcurementOrderId = o.GetValue<Guid?>(nameof(Vehicle.ProcurementOrderId));
        e.PurchaseInvoiceId = o.GetValue<Guid?>(nameof(Vehicle.PurchaseInvoiceId));
        e.ReceivedAt = o.GetValue<DateTime?>(nameof(Vehicle.ReceivedAt));
        e.ProcurementOrderItemId = o.GetValue<Guid?>(nameof(Vehicle.ProcurementOrderItemId));
        e.PurchasePrice = o.GetValue<decimal?>(nameof(Vehicle.PurchasePrice));
        e.AdditionalCosts = o.GetValue<decimal?>(nameof(Vehicle.AdditionalCosts));
        e.PreparationCost = o.GetValue<decimal?>(nameof(Vehicle.PreparationCost));
        if (e.Status == VehicleStatus.Draft) throw new ConflictException(Messages.DraftVehicleManagedByInvoice);
        // تغيير رقم الشاسيه من البطاقة (استبدال المؤقت بالحقيقي): يُحدَّث معه ما نُسخ عنه
        if (o.GetValue<string>(nameof(Vehicle.ChassisNumber)) != e.ChassisNumber)
        {
            e.IsTemporaryChassis = ChassisCheck.IsProvisional(e.ChassisNumber);
            await PropagateChassisAsync(new[] { e }, ct);
        }
        if (e.Status == VehicleStatus.Sold) throw new ConflictException(Messages.CannotEditSoldVehicle);
        if (e.Status == VehicleStatus.WrittenOff) throw new ConflictException(Messages.VehicleWrittenOffRestoreByCount);
        await FillDenormalizedAsync(e, ct);
        Recalculate(e, await CarTax.LoadAsync(Db, ct));
    }

    protected override async Task OnDeletingAsync(Vehicle e, CancellationToken ct)
    {
        if (e.Status != VehicleStatus.Available) throw new ConflictException(Messages.CannotDeleteReservedOrSoldVehicle);
        if (e.PurchaseInvoiceId != null) throw new ConflictException(Messages.VehicleFromPurchaseInvoice);
        if (await Db.Set<CarSalesContract>().AnyAsync(c => c.VehicleId == e.Id, ct)) throw new ConflictException(Messages.VehicleLinkedToSalesContract);
        if (await Db.Set<CarProcurementOrderVin>().AnyAsync(v => v.VehicleId == e.Id, ct)) throw new ConflictException(Messages.VehicleFromPurchaseOrder);

        // أسطر الرصيد الافتتاحي تُحذف مع المركبة؛ أي مصروف آخر مسجَّل عليها يمنع حذفها
        var allocations = await Db.Set<AdditionalChargeAllocation>().Include(a => a.Line).ThenInclude(l => l.Allocations).Where(a => a.VehicleId == e.Id).ToListAsync(ct);
        if (allocations.Any(a => a.Line.Source != ChargeSource.OpeningBalance)) throw new ConflictException(Messages.VehicleHasChargeLines);
        foreach (var line in allocations.Select(a => a.Line).Distinct())
        {
            Db.RemoveRange(line.Allocations);
            Db.Remove(line);
        }
    }

    private async Task FillDenormalizedAsync(Vehicle e, CancellationToken ct)
    {
        if (e.BrandId.HasValue && string.IsNullOrWhiteSpace(e.BrandNameAr))
            e.BrandNameAr = await Db.Set<CarBrand>().Where(b => b.Id == e.BrandId).Select(b => b.NameAr).FirstAsync(ct);
        if (e.ModelId.HasValue && string.IsNullOrWhiteSpace(e.ModelNameAr))
            e.ModelNameAr = await Db.Set<CarModel>().Where(m => m.Id == e.ModelId).Select(m => m.NameAr).FirstAsync(ct);
        if (e.TrimId.HasValue && string.IsNullOrWhiteSpace(e.TrimNameAr))
            e.TrimNameAr = await Db.Set<CarTrim>().Where(t => t.Id == e.TrimId).Select(t => t.NameAr).FirstAsync(ct);
        if (e.AgentId.HasValue && string.IsNullOrWhiteSpace(e.AgentNameAr))
            e.AgentNameAr = await Db.Set<CarAgent>().Where(a => a.Id == e.AgentId).Select(a => a.NameAr).FirstAsync(ct);
        if (string.IsNullOrWhiteSpace(e.BrandNameAr) || string.IsNullOrWhiteSpace(e.ModelNameAr))
            throw new ValidationFailedException(Messages.BrandAndModelRequiredChooseOrType);
    }

    /// <summary>التكلفة الإجمالية والضريبة تُحسب في السرفر دائماً.</summary>
    internal static void Recalculate(Vehicle v, CarTax tax)
    {
        v.TotalCost = (v.PurchasePrice ?? 0) + (v.AdditionalCosts ?? 0) + (v.PreparationCost ?? 0);
        var vat = tax.Calculate(v.PurchasePrice, v.TotalCost, v.SellingPrice, v.VatMode);
        v.VatAmount = vat.VatAmount;
        v.PriceWithVat = vat.PriceWithVat;
    }
}
