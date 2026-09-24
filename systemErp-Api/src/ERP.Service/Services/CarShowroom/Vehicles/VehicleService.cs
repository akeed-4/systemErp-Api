using System.Text.RegularExpressions;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

public class VehicleService : CrudService<Vehicle, VehicleDto, CreateVehicleDto, UpdateVehicleDto>, IVehicleService
{
    // ISO 3779: 17 خانة، بدون الأحرف I و O و Q
    private static readonly Regex VinPattern = new("^[A-HJ-NPR-Z0-9]{17}$", RegexOptions.Compiled);

    public VehicleService(ErpDbContext db) : base(db) { }
    protected override string Label => "المركبة";

    public CarVatResult CalculateVat(CalculateCarVatRequestDto r) => CarVat.Calculate(r.CostPrice, r.SellingPrice, r.Mode);

    protected override IQueryable<Vehicle> ApplySearch(IQueryable<Vehicle> q, string t)
        => q.Where(v => v.ChassisNumber.Contains(t) || v.BrandNameAr.Contains(t) || v.ModelNameAr.Contains(t) || (v.PlateNumber != null && v.PlateNumber.Contains(t)));

    protected override IQueryable<Vehicle> ApplyFilters(IQueryable<Vehicle> q, PaginationParams p)
        => Enum.TryParse<VehicleStatus>(p.Status, true, out var st) ? q.Where(v => v.Status == st) : q;

    public async Task<List<VehicleDto>> ListAvailableAsync(string? search, CancellationToken ct = default)
    {
        var q = Db.Set<Vehicle>().AsNoTracking().Where(v => v.Status == VehicleStatus.Available);
        if (!string.IsNullOrWhiteSpace(search)) q = ApplySearch(q, search.Trim());
        return (await q.OrderBy(v => v.BrandNameAr).ThenBy(v => v.ModelNameAr).Take(200).ToListAsync(ct)).Select(ToDto).ToList();
    }

    protected override async Task ValidateAsync(CreateVehicleDto d, Vehicle? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        d.ChassisNumber = (d.ChassisNumber ?? string.Empty).Trim().ToUpperInvariant();
        if (!VinPattern.IsMatch(d.ChassisNumber)) errors.Add("رقم الشاسيه (VIN) يجب أن يتكون من 17 خانة (أحرف وأرقام بدون I/O/Q).");
        if (d.Year is < 1980 or > 2100) errors.Add("سنة الصنع غير صالحة.");
        if (d.SellingPrice < 0 || d.PurchasePrice < 0 || d.AdditionalCosts < 0 || d.PreparationCost < 0 || d.MinSellingPrice < 0)
            errors.Add("الأسعار والتكاليف لا تكون سالبة.");
        if (d.MinSellingPrice > d.SellingPrice) errors.Add("الحد الأدنى للسعر أعلى من سعر البيع.");
        if (d.Condition == VehicleCondition.New && d.MileageKm is > 500) errors.Add("السيارة الجديدة لا يتجاوز عدّادها 500 كم.");
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        if (await Db.Set<Vehicle>().AnyAsync(v => v.ChassisNumber == d.ChassisNumber && (existing == null || v.Id != existing.Id), ct))
            throw new ConflictException("رقم الشاسيه مسجّل مسبقاً.");

        if (d.BrandId.HasValue && !await Db.Set<CarBrand>().AnyAsync(b => b.Id == d.BrandId, ct)) throw new ValidationFailedException("الماركة غير موجودة.");
        if (d.ModelId.HasValue && !await Db.Set<CarModel>().AnyAsync(m => m.Id == d.ModelId, ct)) throw new ValidationFailedException("الموديل غير موجود.");
        if (d.TrimId.HasValue && !await Db.Set<CarTrim>().AnyAsync(t => t.Id == d.TrimId, ct)) throw new ValidationFailedException("الفئة غير موجودة.");
        if (existing != null && existing.Status != VehicleStatus.Available && d.VatMode != existing.VatMode)
            throw new ConflictException("لا يمكن تغيير نمط الضريبة لمركبة محجوزة أو مباعة.");
    }

    protected override async Task OnCreatingAsync(Vehicle e, CreateVehicleDto d, CancellationToken ct)
    {
        e.Status = VehicleStatus.Available;
        await FillDenormalizedAsync(e, ct);
        Recalculate(e);
    }

    protected override async Task OnUpdatingAsync(Vehicle e, UpdateVehicleDto d, CancellationToken ct)
    {
        var o = Db.Entry(e).OriginalValues;
        e.Status = o.GetValue<VehicleStatus>(nameof(Vehicle.Status)); // الحالة تتغير بدورة البيع فقط
        e.ProcurementOrderId = o.GetValue<Guid?>(nameof(Vehicle.ProcurementOrderId));
        if (e.Status == VehicleStatus.Sold) throw new ConflictException("لا يمكن تعديل مركبة مباعة.");
        await FillDenormalizedAsync(e, ct);
        Recalculate(e);
    }

    protected override async Task OnDeletingAsync(Vehicle e, CancellationToken ct)
    {
        if (e.Status != VehicleStatus.Available) throw new ConflictException("لا يمكن حذف مركبة محجوزة أو مباعة.");
        if (await Db.Set<CarSalesContract>().AnyAsync(c => c.VehicleId == e.Id, ct)) throw new ConflictException("المركبة مرتبطة بعقد بيع.");
        if (await Db.Set<CarProcurementOrderVin>().AnyAsync(v => v.VehicleId == e.Id, ct)) throw new ConflictException("المركبة واردة من أمر شراء ولا تُحذف.");
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
            throw new ValidationFailedException("الماركة والموديل مطلوبان (اختر من القوائم أو اكتب الاسم).");
    }

    /// <summary>التكلفة الإجمالية والضريبة تُحسب في الخادم دائماً.</summary>
    internal static void Recalculate(Vehicle v)
    {
        v.TotalCost = (v.PurchasePrice ?? 0) + (v.AdditionalCosts ?? 0) + (v.PreparationCost ?? 0);
        var vat = CarVat.Calculate(v.TotalCost, v.SellingPrice, v.VatMode);
        v.VatAmount = vat.VatAmount;
        v.PriceWithVat = vat.PriceWithVat;
    }
}
