using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using St = ERP.Core.Models.Accounting.WorkOrderStatuses;
using ASt = ERP.Core.Models.Accounting.AppointmentStatuses;
using FSt = ERP.Core.Models.Accounting.FollowUpStatuses;

namespace ERP.Service.Services.Accounting;

internal static class WorkshopTimes
{
    public static DateTime Local(DateTime d) => d.Kind == DateTimeKind.Utc ? SaudiTime.FromUtc(d) : DateTime.SpecifyKind(d, DateTimeKind.Unspecified);

    public static string? Trim(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length > max ? v[..max] : v;
    }

    /// <summary>تعارض منصة: أمر قائم آخر أو موعد مفتوح آخر على نفس المنصة بنافذة متداخلة.</summary>
    public static async Task EnsureBayFreeAsync(ErpDbContext db, Guid bayId, DateTime start, DateTime end, Guid? exceptOrder, Guid? exceptAppointment, CancellationToken ct)
    {
        var orderClash = await db.Set<WorkOrder>().AnyAsync(o => o.BayId == bayId && o.Id != exceptOrder && St.Editable.Contains(o.Status)
            && o.PlannedStartAt != null && o.PlannedEndAt != null && o.PlannedStartAt < end && o.PlannedEndAt > start, ct);
        if (orderClash) throw new ConflictException(Messages.WorkshopBayBusy);
        var appointments = await db.Set<WorkshopAppointment>().AsNoTracking()
            .Where(a => a.BayId == bayId && a.Id != exceptAppointment && (a.Status == ASt.Booked || a.Status == ASt.Confirmed) && a.ScheduledAt < end)
            .Select(a => new { a.ScheduledAt, a.DurationMinutes }).ToListAsync(ct);
        if (appointments.Any(a => a.ScheduledAt.AddMinutes(a.DurationMinutes) > start)) throw new ConflictException(Messages.WorkshopBayBusy);
    }
}

/// <summary>
/// التخطيط (S-Plan): المنصات، وإسناد الأمر القائم قبل الفوترة لمنصة نشطة وفني نشط ونافذة (بداية &lt; نهاية)، ومنع تداخل المنصة الواحدة.
/// الإسناد لا يغيّر حالة الأمر ولا أثر مالي له.
/// </summary>
public class WorkshopPlanningService : IWorkshopPlanningService
{
    private readonly ErpDbContext _db;
    private readonly IWorkOrderService _orders;
    private readonly IAuditService _audit;

    public WorkshopPlanningService(ErpDbContext db, IWorkOrderService orders, IAuditService audit)
    {
        _db = db; _orders = orders; _audit = audit;
    }

    public async Task<List<WorkshopBayDto>> BaysAsync(CancellationToken ct = default)
    {
        var bays = await _db.Set<WorkshopBay>().AsNoTracking().OrderBy(b => b.SortOrder).ThenBy(b => b.Code).ToListAsync(ct);
        var whIds = bays.Where(b => b.WarehouseId != null).Select(b => b.WarehouseId!.Value).Distinct().ToList();
        var wh = await _db.Set<Warehouse>().AsNoTracking().Where(w => whIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.NameAr, ct);
        return bays.Select(b => ToDto(b, b.WarehouseId is Guid w ? wh.GetValueOrDefault(w) : null)).ToList();
    }

    private static WorkshopBayDto ToDto(WorkshopBay b, string? warehouse) => new()
    {
        Id = b.Id, Code = b.Code, NameAr = b.NameAr, NameEn = b.NameEn, WarehouseId = b.WarehouseId, WarehouseName = warehouse, SortOrder = b.SortOrder,
        IsActive = b.IsActive,
    };

    public async Task<WorkshopBayDto> CreateBayAsync(SaveWorkshopBayDto r, CancellationToken ct = default)
    {
        var b = new WorkshopBay();
        await ApplyAsync(b, r, ct);
        _db.Add(b);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WORKSHOP_BAY_CREATED", nameof(WorkshopBay), b.Id.ToString(), b.Code, ct);
        return (await BaysAsync(ct)).First(x => x.Id == b.Id);
    }

    public async Task<WorkshopBayDto> UpdateBayAsync(Guid id, SaveWorkshopBayDto r, CancellationToken ct = default)
    {
        var b = await _db.Set<WorkshopBay>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(Messages.WorkshopBayNotFound);
        await ApplyAsync(b, r, ct);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WORKSHOP_BAY_UPDATED", nameof(WorkshopBay), b.Id.ToString(), b.Code, ct);
        return (await BaysAsync(ct)).First(x => x.Id == b.Id);
    }

    private async Task ApplyAsync(WorkshopBay b, SaveWorkshopBayDto r, CancellationToken ct)
    {
        var code = WorkshopTimes.Trim(r.Code, 32) ?? throw new ValidationFailedException(Messages.WorkshopBayCodeRequired);
        var name = WorkshopTimes.Trim(r.NameAr, 150) ?? throw new ValidationFailedException(Messages.WorkshopBayNameRequired);
        if (await _db.Set<WorkshopBay>().AnyAsync(x => x.Code == code && x.Id != b.Id, ct)) throw new ConflictException(Messages.WorkshopBayCodeExists);
        if (r.WarehouseId is Guid wh && !await _db.Set<Warehouse>().AnyAsync(w => w.Id == wh, ct))
            throw new ValidationFailedException(Messages.WorkshopWarehouseNotFound);
        b.Code = code; b.NameAr = name; b.NameEn = WorkshopTimes.Trim(r.NameEn, 150); b.WarehouseId = r.WarehouseId; b.SortOrder = r.SortOrder; b.IsActive = r.IsActive;
    }

    public async Task DeleteBayAsync(Guid id, CancellationToken ct = default)
    {
        var b = await _db.Set<WorkshopBay>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(Messages.WorkshopBayNotFound);
        if (await _db.Set<WorkOrder>().AnyAsync(o => o.BayId == id, ct) || await _db.Set<WorkshopAppointment>().AnyAsync(a => a.BayId == id, ct))
            throw new ConflictException(Messages.WorkshopBayInUse);
        _db.Remove(b);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WORKSHOP_BAY_DELETED", nameof(WorkshopBay), id.ToString(), b.Code, ct);
    }

    public async Task<WorkOrderDto> PlanAsync(Guid workOrderId, PlanWorkOrderDto r, CancellationToken ct = default)
    {
        var o = await _db.Set<WorkOrder>().FirstOrDefaultAsync(x => x.Id == workOrderId, ct) ?? throw new NotFoundException(Messages.WorkOrderNotFound);
        if (!St.Editable.Contains(o.Status)) throw new ConflictException(Messages.WorkshopPlanOrderLocked);
        if (r.BayId is Guid bay && !await _db.Set<WorkshopBay>().AnyAsync(b => b.Id == bay && b.IsActive, ct))
            throw new ValidationFailedException(Messages.WorkshopBayNotFound);
        if (r.AssignedTechnicianId is Guid tech && !await _db.Set<Employee>().AnyAsync(e => e.Id == tech && e.Status == EmployeeStatuses.Active, ct))
            throw new ValidationFailedException(Messages.TimeLogTechnicianNotFound);
        DateTime? start = r.PlannedStartAt is DateTime s ? WorkshopTimes.Local(s) : null;
        DateTime? end = r.PlannedEndAt is DateTime e2 ? WorkshopTimes.Local(e2) : null;
        if ((start == null) != (end == null) || start >= end) throw new ValidationFailedException(Messages.WorkshopPlanWindowInvalid);
        if (r.BayId is Guid b2 && start is DateTime ws && end is DateTime we)
            await WorkshopTimes.EnsureBayFreeAsync(_db, b2, ws, we, o.Id, null, ct);

        o.BayId = r.BayId; o.AssignedTechnicianId = r.AssignedTechnicianId; o.PlannedStartAt = start; o.PlannedEndAt = end;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WORK_ORDER_PLANNED", nameof(WorkOrder), o.Id.ToString(), $"{o.OrderNumber} • {start:yyyy-MM-dd HH:mm}-{end:HH:mm}", ct);
        return await _orders.GetAsync(o.Id, ct);
    }

    public async Task<WorkshopScheduleDto> ScheduleAsync(DateTime from, DateTime to, Guid? warehouseId, CancellationToken ct = default)
    {
        from = WorkshopTimes.Local(from); to = WorkshopTimes.Local(to);
        if (to <= from || (to - from).TotalDays > 31) throw new ValidationFailedException(Messages.WorkshopScheduleRangeInvalid);
        var now = SaudiTime.Now;
        var bays = (await BaysAsync(ct)).Where(b => b.IsActive && (warehouseId == null || b.WarehouseId == null || b.WarehouseId == warehouseId)).ToList();

        var orders = await _db.Set<WorkOrder>().AsNoTracking()
            .Where(o => St.Editable.Contains(o.Status) && (warehouseId == null || o.WarehouseId == warehouseId))
            .Where(o => o.PlannedStartAt == null || (o.PlannedStartAt < to && o.PlannedEndAt > from))
            .ToListAsync(ct);
        var vehicleIds = orders.Select(o => o.VehicleId).Distinct().ToList();
        var appts = await _db.Set<WorkshopAppointment>().AsNoTracking()
            .Where(a => (a.Status == ASt.Booked || a.Status == ASt.Confirmed) && a.ScheduledAt < to && a.ScheduledAt >= from.AddDays(-1)
                && (warehouseId == null || a.WarehouseId == null || a.WarehouseId == warehouseId))
            .ToListAsync(ct);
        appts = appts.Where(a => a.ScheduledAt.AddMinutes(a.DurationMinutes) > from).ToList();
        vehicleIds.AddRange(appts.Where(a => a.VehicleId != null).Select(a => a.VehicleId!.Value));
        var vehicles = await _db.Set<ServiceVehicle>().AsNoTracking().Where(v => vehicleIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);

        var technicianIds = await _db.Set<EmployeeStandardCostRate>().AsNoTracking().Where(x => x.Purpose == EmployeeCostPurposes.Technician)
            .Select(x => x.EmployeeId).Distinct().ToListAsync(ct);
        technicianIds.AddRange(orders.Where(o => o.AssignedTechnicianId != null).Select(o => o.AssignedTechnicianId!.Value));
        var techs = await _db.Set<Employee>().AsNoTracking().Where(e => technicianIds.Contains(e.Id) && e.Status == EmployeeStatuses.Active)
            .OrderBy(e => e.NameAr).Select(e => new { e.Id, e.NameAr }).ToListAsync(ct);
        var working = await _db.Set<WorkOrderTimeLog>().AsNoTracking().Where(l => l.EndedAt == null).Select(l => l.EmployeeId).Distinct().ToListAsync(ct);
        var techNames = techs.ToDictionary(t => t.Id, t => t.NameAr);

        ScheduleItemDto FromOrder(WorkOrder o)
        {
            var v = vehicles.GetValueOrDefault(o.VehicleId);
            return new ScheduleItemDto
            {
                Kind = "order", Id = o.Id, Number = o.OrderNumber, CustomerName = o.CustomerName, PlateNumber = o.PlateSnapshot ?? v?.PlateNumber,
                VehicleLabel = v == null ? null : $"{v.Make} {v.Model}".Trim(), Status = o.Status, Priority = o.Priority, BayId = o.BayId,
                TechnicianId = o.AssignedTechnicianId, TechnicianName = o.AssignedTechnicianId is Guid t ? techNames.GetValueOrDefault(t) : null,
                Start = o.PlannedStartAt, End = o.PlannedEndAt, PromisedAt = o.PromisedAt,
                IsOverdue = (o.PromisedAt is DateTime due && due < now && o.Status != St.Ready) || (o.PlannedEndAt is DateTime pe && pe < now),
            };
        }

        var items = orders.Where(o => o.PlannedStartAt != null).Select(FromOrder).ToList();
        items.AddRange(appts.Select(a =>
        {
            var v = a.VehicleId is Guid vid ? vehicles.GetValueOrDefault(vid) : null;
            return new ScheduleItemDto
            {
                Kind = "appointment", Id = a.Id, CustomerName = a.CustomerName, PlateNumber = v?.PlateNumber,
                VehicleLabel = v == null ? a.VehicleDescription : $"{v.Make} {v.Model}".Trim(), Status = a.Status, BayId = a.BayId,
                Start = a.ScheduledAt, End = a.ScheduledAt.AddMinutes(a.DurationMinutes),
            };
        }));

        return new WorkshopScheduleDto
        {
            From = from, To = to, Bays = bays,
            Items = items.OrderBy(i => i.Start).ToList(),
            Unplanned = orders.Where(o => o.PlannedStartAt == null).OrderByDescending(o => o.Priority == WorkOrderPriorities.Urgent)
                .ThenBy(o => o.PromisedAt ?? DateTime.MaxValue).ThenBy(o => o.ReceivedAt).Select(FromOrder).ToList(),
            Technicians = techs.Select(t => new ScheduleTechnicianDto
            {
                Id = t.Id, Name = t.NameAr, Working = working.Contains(t.Id),
                PlannedMinutes = (int)items.Where(i => i.Kind == "order" && i.TechnicianId == t.Id && i.Start != null && i.End != null)
                    .Sum(i => ((i.End! < to ? i.End!.Value : to) - (i.Start! > from ? i.Start!.Value : from)).TotalMinutes),
            }).ToList(),
        };
    }
}

/// <summary>
/// المواعيد: محجوز → مؤكد → وصل (بربطه بأمر عمل لنفس العميل) أو لم يحضر أو ملغى بسبب. التعديل والحذف للمفتوح فقط،
/// والمنصة لا تتداخل مع أوامر مخططة أو مواعيد مفتوحة أخرى.
/// </summary>
public class WorkshopAppointmentService : IWorkshopAppointmentService
{
    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;

    public WorkshopAppointmentService(ErpDbContext db, ICurrentUser user, IAuditService audit)
    {
        _db = db; _user = user; _audit = audit;
    }

    private string? By => _user.Name ?? _user.Email;

    public async Task<List<WorkshopAppointmentDto>> ListAsync(DateTime? from, DateTime? to, string? status, Guid? customerId, CancellationToken ct = default)
    {
        var q = _db.Set<WorkshopAppointment>().AsNoTracking();
        if (from is DateTime f) { var lf = WorkshopTimes.Local(f); q = q.Where(a => a.ScheduledAt >= lf); }
        if (to is DateTime t) { var lt = WorkshopTimes.Local(t); q = q.Where(a => a.ScheduledAt < lt); }
        if (status == "open") q = q.Where(a => a.Status == ASt.Booked || a.Status == ASt.Confirmed);
        else if (!string.IsNullOrWhiteSpace(status)) q = q.Where(a => a.Status == status);
        if (customerId is Guid c) q = q.Where(a => a.CustomerId == c);
        return await ToDtosAsync(await q.OrderBy(a => a.ScheduledAt).Take(500).ToListAsync(ct), ct);
    }

    public async Task<WorkshopAppointmentDto> GetAsync(Guid id, CancellationToken ct = default)
        => (await ToDtosAsync(new() { await LoadAsync(id, ct, track: false) }, ct))[0];

    private async Task<WorkshopAppointment> LoadAsync(Guid id, CancellationToken ct, bool track = true)
    {
        var q = track ? _db.Set<WorkshopAppointment>() : _db.Set<WorkshopAppointment>().AsNoTracking();
        return await q.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException(Messages.WorkshopAppointmentNotFound);
    }

    private async Task<List<WorkshopAppointmentDto>> ToDtosAsync(List<WorkshopAppointment> list, CancellationToken ct)
    {
        if (list.Count == 0) return new();
        var vIds = list.Where(a => a.VehicleId != null).Select(a => a.VehicleId!.Value).Distinct().ToList();
        var vehicles = await _db.Set<ServiceVehicle>().AsNoTracking().Where(v => vIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        var bIds = list.Where(a => a.BayId != null).Select(a => a.BayId!.Value).Distinct().ToList();
        var bays = await _db.Set<WorkshopBay>().AsNoTracking().Where(b => bIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.NameAr, ct);
        var oIds = list.Where(a => a.WorkOrderId != null).Select(a => a.WorkOrderId!.Value).Distinct().ToList();
        var orders = await _db.Set<WorkOrder>().AsNoTracking().Where(o => oIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.OrderNumber, ct);
        return list.Select(a =>
        {
            var v = a.VehicleId is Guid vid ? vehicles.GetValueOrDefault(vid) : null;
            return new WorkshopAppointmentDto
            {
                Id = a.Id, CustomerId = a.CustomerId, CustomerName = a.CustomerName, CustomerPhone = a.CustomerPhone, VehicleId = a.VehicleId,
                VehicleLabel = v == null ? null : $"{v.Make} {v.Model} {v.Year}".Trim(), PlateNumber = v?.PlateNumber, VehicleDescription = a.VehicleDescription,
                WarehouseId = a.WarehouseId, BayId = a.BayId, BayName = a.BayId is Guid b ? bays.GetValueOrDefault(b) : null, ScheduledAt = a.ScheduledAt,
                DurationMinutes = a.DurationMinutes, Complaint = a.Complaint, Status = a.Status, WorkOrderId = a.WorkOrderId,
                WorkOrderNumber = a.WorkOrderId is Guid o ? orders.GetValueOrDefault(o) : null, CancelReason = a.CancelReason, Notes = a.Notes,
                CreatedBy = a.CreatedBy, CreatedAt = a.CreatedAt,
            };
        }).ToList();
    }

    public async Task<WorkshopAppointmentDto> CreateAsync(SaveWorkshopAppointmentDto r, CancellationToken ct = default)
    {
        var a = new WorkshopAppointment { CreatedBy = By };
        await ApplyAsync(a, r, ct);
        _db.Add(a);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WORKSHOP_APPOINTMENT_CREATED", nameof(WorkshopAppointment), a.Id.ToString(), $"{a.CustomerName} • {a.ScheduledAt:yyyy-MM-dd HH:mm}", ct);
        return await GetAsync(a.Id, ct);
    }

    public async Task<WorkshopAppointmentDto> UpdateAsync(Guid id, SaveWorkshopAppointmentDto r, CancellationToken ct = default)
    {
        var a = await LoadAsync(id, ct);
        if (!ASt.IsOpen(a.Status)) throw new ConflictException(Messages.WorkshopAppointmentNotOpen);
        await ApplyAsync(a, r, ct);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WORKSHOP_APPOINTMENT_UPDATED", nameof(WorkshopAppointment), a.Id.ToString(), $"{a.ScheduledAt:yyyy-MM-dd HH:mm}", ct);
        return await GetAsync(a.Id, ct);
    }

    private async Task ApplyAsync(WorkshopAppointment a, SaveWorkshopAppointmentDto r, CancellationToken ct)
    {
        var customer = await _db.Set<Customer>().AsNoTracking().Where(c => c.Id == r.CustomerId).Select(c => new { c.NameAr, c.Phone }).FirstOrDefaultAsync(ct)
            ?? throw new ValidationFailedException(Messages.CustomerNotFound);
        if (r.VehicleId is Guid vid && !await _db.Set<ServiceVehicle>().AnyAsync(v => v.Id == vid && v.CustomerId == r.CustomerId, ct))
            throw new ValidationFailedException(Messages.WorkshopAppointmentVehicleMismatch);
        if (r.DurationMinutes is < 15 or > 24 * 60) throw new ValidationFailedException(Messages.WorkshopAppointmentDurationInvalid);
        var at = WorkshopTimes.Local(r.ScheduledAt);
        if (at == default) throw new ValidationFailedException(Messages.WorkshopAppointmentTimeInvalid);
        if (r.WarehouseId is Guid wh && !await _db.Set<Warehouse>().AnyAsync(w => w.Id == wh, ct))
            throw new ValidationFailedException(Messages.WorkshopWarehouseNotFound);
        if (r.BayId is Guid bay)
        {
            if (!await _db.Set<WorkshopBay>().AnyAsync(b => b.Id == bay && b.IsActive, ct)) throw new ValidationFailedException(Messages.WorkshopBayNotFound);
            await WorkshopTimes.EnsureBayFreeAsync(_db, bay, at, at.AddMinutes(r.DurationMinutes), null, a.Id == Guid.Empty ? (Guid?)null : a.Id, ct);
        }
        a.CustomerId = r.CustomerId; a.CustomerName = customer.NameAr; a.CustomerPhone = WorkshopTimes.Trim(r.CustomerPhone, 30) ?? customer.Phone;
        a.VehicleId = r.VehicleId; a.VehicleDescription = WorkshopTimes.Trim(r.VehicleDescription, 200); a.WarehouseId = r.WarehouseId; a.BayId = r.BayId;
        a.ScheduledAt = at; a.DurationMinutes = r.DurationMinutes; a.Complaint = WorkshopTimes.Trim(r.Complaint, 2000); a.Notes = WorkshopTimes.Trim(r.Notes, 1000);
    }

    public async Task<WorkshopAppointmentDto> SetStatusAsync(Guid id, AppointmentStatusDto r, CancellationToken ct = default)
    {
        var a = await LoadAsync(id, ct);
        var target = r.Status?.Trim().ToLowerInvariant();
        if (target is not (ASt.Confirmed or ASt.NoShow)) throw new ValidationFailedException(Messages.WorkshopAppointmentStatusInvalid);
        if (!ASt.IsOpen(a.Status)) throw new ConflictException(Messages.WorkshopAppointmentNotOpen);
        a.Status = target;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WORKSHOP_APPOINTMENT_STATUS", nameof(WorkshopAppointment), a.Id.ToString(), target, ct);
        return await GetAsync(a.Id, ct);
    }

    public async Task<WorkshopAppointmentDto> ArriveAsync(Guid id, AppointmentArriveDto r, CancellationToken ct = default)
    {
        var a = await LoadAsync(id, ct);
        if (!ASt.IsOpen(a.Status)) throw new ConflictException(Messages.WorkshopAppointmentNotOpen);
        var o = await _db.Set<WorkOrder>().AsNoTracking().Where(x => x.Id == r.WorkOrderId).Select(x => new { x.CustomerId, x.VehicleId, x.Status }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.WorkOrderNotFound);
        if (o.CustomerId != a.CustomerId || o.Status == St.Cancelled) throw new ValidationFailedException(Messages.WorkshopAppointmentCustomerMismatch);
        if (await _db.Set<WorkshopAppointment>().AnyAsync(x => x.WorkOrderId == r.WorkOrderId && x.Id != a.Id, ct))
            throw new ConflictException(Messages.WorkshopAppointmentOrderLinked);
        a.Status = ASt.Arrived; a.WorkOrderId = r.WorkOrderId; a.VehicleId ??= o.VehicleId;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WORKSHOP_APPOINTMENT_ARRIVED", nameof(WorkshopAppointment), a.Id.ToString(), r.WorkOrderId.ToString(), ct);
        return await GetAsync(a.Id, ct);
    }

    public async Task<WorkshopAppointmentDto> CancelAsync(Guid id, WorkshopReasonDto r, CancellationToken ct = default)
    {
        var a = await LoadAsync(id, ct);
        if (!ASt.IsOpen(a.Status)) throw new ConflictException(Messages.WorkshopAppointmentNotOpen);
        a.CancelReason = WorkshopTimes.Trim(r.Reason, 500) ?? throw new ValidationFailedException(Messages.WorkshopCancelReasonRequired);
        a.Status = ASt.Cancelled;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WORKSHOP_APPOINTMENT_CANCELLED", nameof(WorkshopAppointment), a.Id.ToString(), a.CancelReason, ct);
        return await GetAsync(a.Id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var a = await LoadAsync(id, ct);
        if (!ASt.IsOpen(a.Status)) throw new ConflictException(Messages.WorkshopAppointmentNotOpen);
        await _db.Set<WorkshopFollowUp>().Where(f => f.AppointmentId == a.Id).ExecuteUpdateAsync(x => x.SetProperty(f => f.AppointmentId, (Guid?)null), ct);
        _db.Remove(a);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WORKSHOP_APPOINTMENT_DELETED", nameof(WorkshopAppointment), id.ToString(), a.CustomerName, ct);
    }
}

/// <summary>إنشاء متابعات التسليم من الإعداد: اتصال بعد N يوم، وتذكير صيانة بعد N شهر و/أو N كم. القيم الفارغة = لا شيء.</summary>
public static class WorkshopFollowUpRules
{
    public static void AddOnDelivery(ErpDbContext db, WorkshopSettings? s, WorkOrder o, DateTime deliveredAt, decimal odometer, string? by)
    {
        if (s == null) return;
        if (s.FollowUpCallDays is int days and > 0)
            db.Add(new WorkshopFollowUp
            {
                VehicleId = o.VehicleId, CustomerId = o.CustomerId, WorkOrderId = o.Id, Kind = FollowUpKinds.PostDeliveryCall,
                DueDate = deliveredAt.Date.AddDays(days), CreatedBy = by,
            });
        if (s.ServiceReminderMonths is > 0 || s.ServiceReminderKm is > 0)
            db.Add(new WorkshopFollowUp
            {
                VehicleId = o.VehicleId, CustomerId = o.CustomerId, WorkOrderId = o.Id, Kind = FollowUpKinds.ServiceReminder,
                DueDate = deliveredAt.Date.AddMonths(s.ServiceReminderMonths is int m and > 0 ? m : 6),
                DueOdometer = s.ServiceReminderKm is int km and > 0 ? odometer + km : null, CreatedBy = by,
            });
    }
}

public class WorkshopFollowUpService : IWorkshopFollowUpService
{
    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;

    public WorkshopFollowUpService(ErpDbContext db, ICurrentUser user, IAuditService audit)
    {
        _db = db; _user = user; _audit = audit;
    }

    private string? By => _user.Name ?? _user.Email;

    public async Task<List<WorkshopFollowUpDto>> ListAsync(string? status, bool dueOnly, Guid? vehicleId, CancellationToken ct = default)
    {
        var q = _db.Set<WorkshopFollowUp>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(f => f.Status == status);
        if (dueOnly) { var limit = SaudiTime.Today.AddDays(7); q = q.Where(f => f.Status == FSt.Pending && f.DueDate <= limit); }
        if (vehicleId is Guid v) q = q.Where(f => f.VehicleId == v);
        return await ToDtosAsync(await q.OrderBy(f => f.DueDate).Take(500).ToListAsync(ct), ct);
    }

    private async Task<List<WorkshopFollowUpDto>> ToDtosAsync(List<WorkshopFollowUp> list, CancellationToken ct)
    {
        if (list.Count == 0) return new();
        var vIds = list.Select(f => f.VehicleId).Distinct().ToList();
        var vehicles = await _db.Set<ServiceVehicle>().AsNoTracking().Where(v => vIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        var cIds = list.Select(f => f.CustomerId).Distinct().ToList();
        var customers = await _db.Set<Customer>().AsNoTracking().Where(c => cIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => new { c.NameAr, c.Phone }, ct);
        var oIds = list.Where(f => f.WorkOrderId != null).Select(f => f.WorkOrderId!.Value).Distinct().ToList();
        var orders = await _db.Set<WorkOrder>().AsNoTracking().Where(o => oIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.OrderNumber, ct);
        var today = SaudiTime.Today;
        return list.Select(f =>
        {
            var v = vehicles.GetValueOrDefault(f.VehicleId);
            var c = customers.GetValueOrDefault(f.CustomerId);
            return new WorkshopFollowUpDto
            {
                Id = f.Id, VehicleId = f.VehicleId, VehicleLabel = v == null ? null : $"{v.Make} {v.Model} {v.Year}".Trim(), PlateNumber = v?.PlateNumber,
                CustomerId = f.CustomerId, CustomerName = c?.NameAr, CustomerPhone = c?.Phone, WorkOrderId = f.WorkOrderId,
                WorkOrderNumber = f.WorkOrderId is Guid o ? orders.GetValueOrDefault(o) : null, Kind = f.Kind, DueDate = f.DueDate, DueOdometer = f.DueOdometer,
                Status = f.Status, Notes = f.Notes, Outcome = f.Outcome, AppointmentId = f.AppointmentId, CompletedAt = f.CompletedAt, CompletedBy = f.CompletedBy,
                IsOverdue = f.Status == FSt.Pending && (f.DueDate < today || (f.DueOdometer is decimal km && v != null && v.LastOdometer >= km)),
                CreatedAt = f.CreatedAt,
            };
        }).ToList();
    }

    private async Task<WorkshopFollowUpDto> GetAsync(Guid id, CancellationToken ct)
        => (await ToDtosAsync(new() { await _db.Set<WorkshopFollowUp>().AsNoTracking().FirstAsync(f => f.Id == id, ct) }, ct))[0];

    public async Task<WorkshopFollowUpDto> CreateAsync(SaveWorkshopFollowUpDto r, CancellationToken ct = default)
    {
        var kind = r.Kind?.Trim().ToLowerInvariant();
        if (kind == null || !FollowUpKinds.All.Contains(kind)) throw new ValidationFailedException(Messages.WorkshopFollowUpKindInvalid);
        var vehicle = await _db.Set<ServiceVehicle>().AsNoTracking().FirstOrDefaultAsync(v => v.Id == r.VehicleId, ct)
            ?? throw new ValidationFailedException(Messages.ServiceVehicleNotFound);
        if (r.WorkOrderId is Guid oid && !await _db.Set<WorkOrder>().AnyAsync(o => o.Id == oid && o.VehicleId == vehicle.Id, ct))
            throw new ValidationFailedException(Messages.WorkOrderNotFound);
        if (r.DueOdometer is < 0) throw new ValidationFailedException(Messages.WorkshopSettingsNegative);
        var f = new WorkshopFollowUp
        {
            VehicleId = vehicle.Id, CustomerId = vehicle.CustomerId, WorkOrderId = r.WorkOrderId, Kind = kind, DueDate = WorkshopTimes.Local(r.DueDate).Date,
            DueOdometer = r.DueOdometer, Notes = WorkshopTimes.Trim(r.Notes, 1000), CreatedBy = By,
        };
        _db.Add(f);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WORKSHOP_FOLLOWUP_CREATED", nameof(WorkshopFollowUp), f.Id.ToString(), $"{kind} • {f.DueDate:yyyy-MM-dd}", ct);
        return await GetAsync(f.Id, ct);
    }

    public async Task<WorkshopFollowUpDto> CompleteAsync(Guid id, CompleteFollowUpDto r, CancellationToken ct = default)
    {
        var f = await PendingAsync(id, ct);
        if (r.AppointmentId is Guid ap && !await _db.Set<WorkshopAppointment>().AnyAsync(a => a.Id == ap && a.CustomerId == f.CustomerId, ct))
            throw new ValidationFailedException(Messages.WorkshopAppointmentNotFound);
        f.Status = FSt.Done; f.Outcome = WorkshopTimes.Trim(r.Outcome, 1000); f.AppointmentId = r.AppointmentId;
        f.CompletedAt = SaudiTime.Now; f.CompletedBy = By;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WORKSHOP_FOLLOWUP_DONE", nameof(WorkshopFollowUp), f.Id.ToString(), f.Outcome ?? string.Empty, ct);
        return await GetAsync(f.Id, ct);
    }

    public async Task<WorkshopFollowUpDto> CancelAsync(Guid id, WorkshopReasonDto r, CancellationToken ct = default)
    {
        var f = await PendingAsync(id, ct);
        f.Outcome = WorkshopTimes.Trim(r.Reason, 1000) ?? throw new ValidationFailedException(Messages.WorkshopCancelReasonRequired);
        f.Status = FSt.Cancelled; f.CompletedAt = SaudiTime.Now; f.CompletedBy = By;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WORKSHOP_FOLLOWUP_CANCELLED", nameof(WorkshopFollowUp), f.Id.ToString(), f.Outcome, ct);
        return await GetAsync(f.Id, ct);
    }

    private async Task<WorkshopFollowUp> PendingAsync(Guid id, CancellationToken ct)
    {
        var f = await _db.Set<WorkshopFollowUp>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(Messages.WorkshopFollowUpNotFound);
        if (f.Status != FSt.Pending) throw new ConflictException(Messages.WorkshopFollowUpNotPending);
        return f;
    }
}

/// <summary>
/// تجربة الفني: هويته = الموظف النشط المربوط بالمستخدم الحالي. يرى الأوامر المسندة إليه أو التي سجّل عليها عملاً وهي قيد التنفيذ،
/// ويبدأ ويوقف سجله هو فقط ويُنهي سطر عمالة في أمر مسند إليه. القواعد نفسها من <see cref="IWorkOrderExecutionService"/> و<see cref="IWorkOrderWorkService"/>.
/// </summary>
public class WorkshopTechnicianService : IWorkshopTechnicianService
{
    private static readonly string[] Active = { St.Approved, St.InProgress, St.Paused, St.Quality };
    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IWorkOrderExecutionService _exec;
    private readonly IWorkOrderWorkService _work;

    public WorkshopTechnicianService(ErpDbContext db, ICurrentUser user, IWorkOrderExecutionService exec, IWorkOrderWorkService work)
    {
        _db = db; _user = user; _exec = exec; _work = work;
    }

    private async Task<(Guid Id, string Name)> MeAsync(CancellationToken ct)
    {
        var uid = _user.UserId ?? throw new ForbiddenException(Messages.WorkshopTechnicianNotLinked);
        var me = await _db.Set<Employee>().AsNoTracking().Where(e => e.UserId == uid && e.Status == EmployeeStatuses.Active)
            .Select(e => new { e.Id, e.NameAr }).FirstOrDefaultAsync(ct) ?? throw new ForbiddenException(Messages.WorkshopTechnicianNotLinked);
        return (me.Id, me.NameAr);
    }

    private async Task<WorkOrder> MyOrderAsync(Guid me, Guid workOrderId, CancellationToken ct)
    {
        var o = await _db.Set<WorkOrder>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == workOrderId, ct) ?? throw new NotFoundException(Messages.WorkOrderNotFound);
        if (o.AssignedTechnicianId != me && !await _db.Set<WorkOrderTimeLog>().AnyAsync(l => l.WorkOrderId == o.Id && l.EmployeeId == me, ct))
            throw new ForbiddenException(Messages.WorkshopJobNotAssigned);
        return o;
    }

    public async Task<MyJobsDto> MyJobsAsync(CancellationToken ct = default)
    {
        var (me, name) = await MeAsync(ct);
        var logged = await _db.Set<WorkOrderTimeLog>().AsNoTracking().Where(l => l.EmployeeId == me).Select(l => l.WorkOrderId).Distinct().ToListAsync(ct);
        var orders = await _db.Set<WorkOrder>().AsNoTracking()
            .Where(o => Active.Contains(o.Status) && (o.AssignedTechnicianId == me || logged.Contains(o.Id)))
            .OrderByDescending(o => o.Priority == WorkOrderPriorities.Urgent).ThenBy(o => o.PlannedStartAt ?? o.PromisedAt ?? o.ReceivedAt).Take(50).ToListAsync(ct);
        var ids = orders.Select(o => o.Id).ToList();
        var lines = await _db.Set<WorkOrderLine>().AsNoTracking().Where(l => ids.Contains(l.WorkOrderId) && l.Kind == WorkOrderLineKinds.Labor)
            .OrderBy(l => l.SortOrder).ToListAsync(ct);
        var minutes = await _db.Set<WorkOrderTimeLog>().AsNoTracking().Where(l => ids.Contains(l.WorkOrderId) && l.EmployeeId == me && l.LineId != null)
            .GroupBy(l => l.LineId!.Value).Select(g => new { g.Key, M = g.Sum(l => l.Minutes) }).ToDictionaryAsync(x => x.Key, x => x.M, ct);
        var vIds = orders.Select(o => o.VehicleId).Distinct().ToList();
        var vehicles = await _db.Set<ServiceVehicle>().AsNoTracking().Where(v => vIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        var bIds = orders.Where(o => o.BayId != null).Select(o => o.BayId!.Value).Distinct().ToList();
        var bays = await _db.Set<WorkshopBay>().AsNoTracking().Where(b => bIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.NameAr, ct);
        var jobs = new List<MyJobDto>();
        foreach (var o in orders)
        {
            var executable = await WorkOrderRules.ExecutableAsync(_db, o.Id, ct);
            var v = vehicles.GetValueOrDefault(o.VehicleId);
            jobs.Add(new MyJobDto
            {
                WorkOrderId = o.Id, OrderNumber = o.OrderNumber, Status = o.Status, Priority = o.Priority, CustomerName = o.CustomerName,
                PlateNumber = o.PlateSnapshot ?? v?.PlateNumber, VehicleLabel = v == null ? null : $"{v.Make} {v.Model} {v.Year}".Trim(), Complaint = o.Complaint,
                BayName = o.BayId is Guid b ? bays.GetValueOrDefault(b) : null, PlannedStartAt = o.PlannedStartAt, PlannedEndAt = o.PlannedEndAt,
                PromisedAt = o.PromisedAt, AssignedToMe = o.AssignedTechnicianId == me,
                Lines = lines.Where(l => l.WorkOrderId == o.Id).Select(l => new MyJobLineDto
                {
                    Id = l.Id, Description = l.Description, Kind = l.Kind, Quantity = l.Quantity, ExecutionStatus = l.ExecutionStatus,
                    Executable = executable.Contains(l.Id), LoggedMinutes = (int)minutes.GetValueOrDefault(l.Id),
                }).ToList(),
            });
        }
        var open = (await _exec.OpenLogsAsync(me, ct)).FirstOrDefault();
        var today = SaudiTime.Today;
        var todayMinutes = await _db.Set<WorkOrderTimeLog>().AsNoTracking().Where(l => l.EmployeeId == me && l.StartedAt >= today && l.EndedAt != null)
            .SumAsync(l => (decimal?)l.Minutes, ct) ?? 0;
        return new MyJobsDto { EmployeeId = me, EmployeeName = name, OpenLog = open, TodayMinutes = (int)todayMinutes, Jobs = jobs };
    }

    public async Task<MyJobsDto> StartAsync(Guid workOrderId, MyJobStartDto r, CancellationToken ct = default)
    {
        var (me, _) = await MeAsync(ct);
        var o = await MyOrderAsync(me, workOrderId, ct);
        await _exec.StartLogAsync(o.Id, new SaveTimeLogDto { EmployeeId = me, Kind = r.Kind, LineId = r.LineId, PauseReasonId = r.PauseReasonId, Notes = r.Notes }, ct);
        return await MyJobsAsync(ct);
    }

    public async Task<MyJobsDto> StopAsync(StopTimeLogDto r, CancellationToken ct = default)
    {
        var (me, _) = await MeAsync(ct);
        var open = (await _exec.OpenLogsAsync(me, ct)).FirstOrDefault() ?? throw new ConflictException(Messages.WorkshopNoOpenLog);
        await _exec.StopLogAsync(open.WorkOrderId, open.Id, new StopTimeLogDto { Notes = r.Notes }, ct);
        return await MyJobsAsync(ct);
    }

    public async Task<MyJobsDto> CompleteLineAsync(Guid workOrderId, Guid lineId, CancellationToken ct = default)
    {
        var (me, _) = await MeAsync(ct);
        var o = await MyOrderAsync(me, workOrderId, ct);
        if (o.AssignedTechnicianId != me) throw new ForbiddenException(Messages.WorkshopJobNotAssigned);
        var open = (await _exec.OpenLogsAsync(me, ct)).FirstOrDefault();
        if (open != null && open.LineId == lineId) await _exec.StopLogAsync(open.WorkOrderId, open.Id, new StopTimeLogDto(), ct);
        await _work.ChangeExecutionAsync(o.Id, lineId, new ChangeLineExecutionDto { ExecutionStatus = LineExecutionStatuses.Done }, ct);
        return await MyJobsAsync(ct);
    }
}
