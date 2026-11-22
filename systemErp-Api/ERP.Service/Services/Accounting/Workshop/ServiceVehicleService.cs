using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using ShowroomVehicle = ERP.Core.Models.CarShowroom.Vehicle;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// سجل مركبات العملاء (D4). القواعد: العميل موجود؛ رقم الهيكل أو اللوحة مطلوب، والهيكل فريد في المنشأة (17 حرفاً ورقماً بلا I/O/Q إن
/// كان قياسياً، ويُقبل غيره للمركبات القديمة)؛ الماركة مطلوبة. ربط سيارة المعرض يكمّل الحقول الفارغة منها. العدّاد لا يُخفَّض تحت آخر
/// قراءة في أوامر العمل. الحذف لمركبة بلا أوامر عمل، وإلا تُعطَّل. تغيير المالك لا يمس لقطة العميل في الأوامر السابقة (O15).
/// </summary>
public class ServiceVehicleService : IServiceVehicleService
{
    private readonly ErpDbContext _db;
    private readonly IAuditService _audit;

    public ServiceVehicleService(ErpDbContext db, IAuditService audit) { _db = db; _audit = audit; }

    public async Task<List<ServiceVehicleDto>> ListAsync(Guid? customerId, string? search, CancellationToken ct = default)
    {
        var q = _db.Set<ServiceVehicle>().AsNoTracking();
        if (customerId is Guid cid) q = q.Where(v => v.CustomerId == cid);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(v => (v.Vin != null && v.Vin.Contains(s)) || (v.PlateNumber != null && v.PlateNumber.Contains(s))
                             || v.Make.Contains(s) || (v.Model != null && v.Model.Contains(s)));
        }
        return await DtosAsync(await q.OrderBy(v => v.Make).ThenBy(v => v.PlateNumber).Take(1000).ToListAsync(ct), ct);
    }

    public async Task<ServiceVehicleDto> GetAsync(Guid id, CancellationToken ct = default)
        => (await DtosAsync(new List<ServiceVehicle> { await FindAsync(id, false, ct) }, ct)).Single();

    public async Task<ServiceVehicleDto> CreateAsync(SaveServiceVehicleDto request, CancellationToken ct = default)
    {
        var v = await BuildAsync(request, ct);
        await _db.SaveChangesAsync(ct);
        return await GetAsync(v.Id, ct);
    }

    /// <summary>ينشئ المركبة ويضيفها للسياق دون حفظ.</summary>
    private async Task<ServiceVehicle> BuildAsync(SaveServiceVehicleDto request, CancellationToken ct)
    {
        var v = new ServiceVehicle();
        await ApplyAsync(v, request, ct);
        _db.Add(v);
        await _audit.LogAsync("SERVICE_VEHICLE_CREATED", nameof(ServiceVehicle), v.Id.ToString(), Label(v), ct);
        return v;
    }

    public async Task<ServiceVehicleDto> UpdateAsync(Guid id, SaveServiceVehicleDto request, CancellationToken ct = default)
    {
        var v = await FindAsync(id, true, ct);
        var owner = v.CustomerId;
        await ApplyAsync(v, request, ct);
        await _db.SaveChangesAsync(ct);
        if (owner != v.CustomerId)
            await _audit.LogAsync("SERVICE_VEHICLE_OWNER_CHANGED", nameof(ServiceVehicle), id.ToString(), $"{Label(v)} • {owner} → {v.CustomerId}", ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var v = await FindAsync(id, true, ct);
        if (await _db.Set<WorkOrder>().AnyAsync(o => o.VehicleId == id, ct)) throw new ConflictException(Messages.ServiceVehicleInUse);
        _db.Remove(v);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("SERVICE_VEHICLE_DELETED", nameof(ServiceVehicle), id.ToString(), Label(v), ct);
    }

    public async Task<List<ServiceVehicleHistoryDto>> HistoryAsync(Guid id, CancellationToken ct = default)
    {
        var vehicle = await FindAsync(id, false, ct);
        var orders = await _db.Set<WorkOrder>().AsNoTracking().Where(o => o.VehicleId == id).OrderByDescending(o => o.ReceivedAt).ToListAsync(ct);
        var ids = orders.Select(o => o.Id).ToList();
        // سجل المركبة استعلام على أوامرها: المنفَّذ من الأسطر، والفحوص المكتملة، والضمانات (بلا جدول إضافي)
        var excepted = await _db.Set<WorkOrderExecutionException>().AsNoTracking().Where(x => ids.Contains(x.WorkOrderId)).Select(x => x.LineId).ToListAsync(ct);
        var lines = await _db.Set<WorkOrderLine>().AsNoTracking()
            .Where(l => ids.Contains(l.WorkOrderId) && l.ExecutionStatus == LineExecutionStatuses.Done
                        && (l.ApprovalStatus == LineApprovalStatuses.Approved || excepted.Contains(l.Id)))
            .OrderBy(l => l.SortOrder).Select(l => new { l.WorkOrderId, l.Description, l.Quantity }).ToListAsync(ct);
        var inspections = await _db.Set<WorkOrderInspection>().AsNoTracking().Include(i => i.Results)
            .Where(i => ids.Contains(i.WorkOrderId) && i.Status == InspectionStatuses.Completed).ToListAsync(ct);
        var warranties = await _db.Set<WorkshopWarranty>().AsNoTracking().Where(w => ids.Contains(w.WorkOrderId)).ToListAsync(ct);
        var numbers = orders.ToDictionary(o => o.Id, o => o.OrderNumber);
        var today = SaudiTime.Today;
        return orders.Select(o => new ServiceVehicleHistoryDto
        {
            WorkOrderId = o.Id, OrderNumber = o.OrderNumber, Status = o.Status, ReceivedAt = o.ReceivedAt, DeliveredAt = o.DeliveredAt,
            OdometerIn = o.OdometerIn, OdometerOut = o.OdometerOut, CustomerName = o.CustomerName, Complaint = o.Complaint,
            InvoiceNumber = o.InvoiceNumber, IsWarrantyJob = o.IsWarrantyJob,
            Work = lines.Where(l => l.WorkOrderId == o.Id).Select(l => l.Quantity == 1 ? l.Description : $"{l.Description} × {l.Quantity:0.##}").ToList(),
            Inspections = inspections.Where(i => i.WorkOrderId == o.Id)
                .Select(i => $"{i.TemplateName} ({i.Results.Count(r => r.Condition == InspectionConditions.Fail)}/{i.Results.Count})").ToList(),
            Warranties = warranties.Where(w => w.WorkOrderId == o.Id).Select(w => new WarrantyDto
            {
                Id = w.Id, WorkOrderId = w.WorkOrderId, OrderNumber = o.OrderNumber, LineId = w.LineId, VehicleId = w.VehicleId,
                PlateNumber = vehicle.PlateNumber, CustomerName = o.CustomerName, Description = w.Description, StartDate = w.StartDate, EndDate = w.EndDate,
                KmLimit = w.KmLimit, Status = w.Status == WarrantyStatuses.Active && w.EndDate is DateTime end && end < today ? "expired" : w.Status,
                ClaimWorkOrderId = w.ClaimWorkOrderId, ClaimOrderNumber = w.ClaimWorkOrderId is Guid c ? numbers.GetValueOrDefault(c) : null, VoidReason = w.VoidReason,
            }).ToList(),
        }).ToList();
    }

    private async Task ApplyAsync(ServiceVehicle v, SaveServiceVehicleDto r, CancellationToken ct)
    {
        if (!await _db.Set<Customer>().AnyAsync(c => c.Id == r.CustomerId, ct)) throw new ValidationFailedException(Messages.WorkshopCustomerNotFound);
        string? vin = Normalize(r.Vin, 32), plate = Trim(r.PlateNumber, 30), make = Trim(r.Make, 100), model = Trim(r.Model, 100);
        string? color = Trim(r.Color, 50), engine = Trim(r.EngineNumber, 50);
        var year = r.Year;
        if (r.ShowroomVehicleId is Guid sid)
        {
            var s = await _db.Set<ShowroomVehicle>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == sid, ct)
                ?? throw new ValidationFailedException(Messages.ServiceVehicleShowroomNotFound);
            if (!s.IsTemporaryChassis) vin ??= Normalize(s.ChassisNumber, 32);
            engine ??= Trim(s.EngineNumber, 50); make ??= Trim(s.BrandNameAr, 100); model ??= Trim(s.ModelNameAr, 100);
            color ??= Trim(s.ColorExterior, 50); plate ??= Trim(s.PlateNumber, 30); year ??= s.Year > 0 ? s.Year : null;
        }
        if (vin == null && plate == null) throw new ValidationFailedException(Messages.ServiceVehicleIdentifierRequired);
        if (make == null) throw new ValidationFailedException(Messages.ServiceVehicleMakeRequired);
        if (year is < 1900 or > 2100) throw new ValidationFailedException(Messages.ServiceVehicleYearInvalid);
        if (vin != null && await _db.Set<ServiceVehicle>().AnyAsync(x => x.Vin == vin && x.Id != v.Id, ct))
            throw new ConflictException(string.Format(Messages.ServiceVehicleVinTaken, vin));
        var fuel = string.IsNullOrWhiteSpace(r.FuelType) ? ServiceVehicleFuelTypes.Petrol : r.FuelType.Trim().ToLowerInvariant();
        if (!ServiceVehicleFuelTypes.IsValid(fuel)) throw new ValidationFailedException(Messages.ServiceVehicleFuelInvalid);
        if (r.LastOdometer is < 0) throw new ValidationFailedException(Messages.WorkOrderOdometerInvalid);
        if (r.LastOdometer is decimal odo && v.Id != Guid.Empty)
        {
            var recorded = await _db.Set<WorkOrder>().Where(o => o.VehicleId == v.Id).Select(o => (decimal?)(o.OdometerOut ?? o.OdometerIn)).MaxAsync(ct) ?? 0;
            if (odo < recorded) throw new ValidationFailedException(string.Format(Messages.WorkOrderOdometerBackwards, odo, recorded));
        }

        v.CustomerId = r.CustomerId; v.Vin = vin; v.PlateNumber = plate; v.Make = make; v.Model = model; v.Year = year; v.Color = color;
        v.EngineNumber = engine; v.FuelType = fuel; v.ShowroomVehicleId = r.ShowroomVehicleId; v.Notes = Trim(r.Notes, 1000); v.IsActive = r.IsActive;
        if (r.LastOdometer is decimal reading) v.LastOdometer = decimal.Round(reading, 0);
    }

    private async Task<ServiceVehicle> FindAsync(Guid id, bool track, CancellationToken ct)
    {
        var q = _db.Set<ServiceVehicle>().AsQueryable();
        if (!track) q = q.AsNoTracking();
        return await q.FirstOrDefaultAsync(v => v.Id == id, ct) ?? throw new NotFoundException(Messages.ServiceVehicleNotFound);
    }

    private async Task<List<ServiceVehicleDto>> DtosAsync(List<ServiceVehicle> list, CancellationToken ct)
    {
        if (list.Count == 0) return new();
        var ids = list.Select(v => v.Id).ToList();
        var customerIds = list.Select(v => v.CustomerId).Distinct().ToList();
        var customers = await _db.Set<Customer>().AsNoTracking().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.NameAr, ct);
        var orders = await _db.Set<WorkOrder>().AsNoTracking().Where(o => ids.Contains(o.VehicleId))
            .Select(o => new { o.Id, o.VehicleId, o.OrderNumber, o.Status }).ToListAsync(ct);
        return list.Select(v =>
        {
            var open = orders.FirstOrDefault(o => o.VehicleId == v.Id && WorkOrderStatuses.Open.Contains(o.Status));
            return new ServiceVehicleDto
            {
                Id = v.Id, CustomerId = v.CustomerId, CustomerName = customers.GetValueOrDefault(v.CustomerId), Vin = v.Vin, PlateNumber = v.PlateNumber,
                Make = v.Make, Model = v.Model, Year = v.Year, Color = v.Color, EngineNumber = v.EngineNumber, FuelType = v.FuelType,
                LastOdometer = v.LastOdometer, ShowroomVehicleId = v.ShowroomVehicleId, Notes = v.Notes, IsActive = v.IsActive,
                OpenWorkOrderId = open?.Id, OpenWorkOrderNumber = open?.OrderNumber, WorkOrderCount = orders.Count(o => o.VehicleId == v.Id),
            };
        }).ToList();
    }

    internal static string Label(ServiceVehicle v)
        => string.Join(" • ", new[] { $"{v.Make} {v.Model}".Trim(), v.PlateNumber, v.Vin }.Where(x => !string.IsNullOrWhiteSpace(x)));

    /// <summary>رقم الهيكل بحروف كبيرة بلا مسافات أو شرطات.</summary>
    private static string? Normalize(string? value, int max)
    {
        var v = value == null ? null : new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return string.IsNullOrEmpty(v) ? null : v.Length > max ? v[..max] : v;
    }

    private static string? Trim(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length > max ? v[..max] : v;
    }
}
