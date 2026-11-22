using ERP.Service.Data;
using B = ERP.Core.Models.Accounting.RentalRateBases;
using BSt = ERP.Core.Models.Accounting.RentalBookingStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>سعر أساس واحد بعد حل الأولوية (المعدة ← الفئة ← الفئة الأم).</summary>
internal sealed record RentalPricedBasis(string Basis, decimal Rate, decimal? OperatorRate, decimal IncludedHours, decimal OvertimeRate, decimal MinimumPeriods);

internal sealed record RentalEstimate(List<RentalChargePartDto> Parts, decimal Rent, decimal Operator, bool MinimumApplied)
{
    public decimal Total => Rent + Operator;
}

/// <summary>
/// تسعير التأجير (منطق صرف، يستعمله التقدير والحجز والفوترة لاحقاً): الفترات الكاملة بالأساس المختار، ثم الباقي بالأسس الأصغر
/// المسعّرة من الأكبر للأصغر، وما يبقى بعدها وحدة واحدة من أصغر أساس مسعّر (O4). ثم الحد الأدنى للفترات بسعر الأساس المختار،
/// والمشغّل بالوحدات نفسها بأسعار مشغّل أسسها. المبالغ قبل الضريبة ومقرّبة لهللتين.
/// </summary>
internal static class RentalPricing
{
    public static DateTime Advance(DateTime t, string basis, int n) => basis switch
    {
        B.Month => t.AddMonths(n),
        B.Week => t.AddDays(7 * n),
        B.Day => t.AddDays(n),
        _ => t.AddHours(n),
    };

    /// <summary>عدد الفترات الكاملة من <paramref name="from"/> لا تتجاوز <paramref name="to"/>.</summary>
    public static int WholePeriods(DateTime from, DateTime to, string basis)
    {
        if (to <= from) return 0;
        if (basis != B.Month)
        {
            var hours = basis switch { B.Week => 168d, B.Day => 24d, _ => 1d };
            var n = (int)Math.Floor((to - from).TotalHours / hours);
            // تصحيح حدود الفاصلة العشرية
            while (n > 0 && Advance(from, basis, n) > to) n--;
            while (Advance(from, basis, n + 1) <= to) n++;
            return n;
        }
        var m = Math.Max(0, (to.Year - from.Year) * 12 + to.Month - from.Month - 1);
        while (m > 0 && from.AddMonths(m) > to) m--;
        while (from.AddMonths(m + 1) <= to) m++;
        return m;
    }

    public static RentalEstimate Estimate(IReadOnlyDictionary<string, RentalPricedBasis> rates, string chosen, DateTime from, DateTime to, bool withOperator)
    {
        if (to <= from) throw new ValidationFailedException(Messages.RentalPeriodInvalid);
        if (!rates.TryGetValue(chosen, out var main)) throw new ValidationFailedException(string.Format(Messages.RentalRateMissing, BasisLabel(chosen)));

        var units = new List<(string Basis, decimal Units)>();
        void Add(string basis, decimal n)
        {
            var i = units.FindIndex(u => u.Basis == basis);
            if (i >= 0) units[i] = (basis, units[i].Units + n); else units.Add((basis, n));
        }

        var cursor = from;
        var whole = WholePeriods(cursor, to, chosen);
        if (whole > 0) { Add(chosen, whole); cursor = Advance(cursor, chosen, whole); }
        var smaller = B.Descending.SkipWhile(b => b != chosen).Skip(1).Where(rates.ContainsKey).ToList();
        foreach (var basis in smaller)
        {
            if (cursor >= to) break;
            var k = WholePeriods(cursor, to, basis);
            if (k > 0) { Add(basis, k); cursor = Advance(cursor, basis, k); }
        }
        if (cursor < to) Add(smaller.LastOrDefault() ?? chosen, 1);

        var minimumApplied = false;
        var rent = units.Sum(u => u.Units * rates[u.Basis].Rate);
        if (main.MinimumPeriods > 0 && rent < main.MinimumPeriods * main.Rate)
        {
            units = new() { (chosen, main.MinimumPeriods) };
            rent = main.MinimumPeriods * main.Rate;
            minimumApplied = true;
        }

        decimal op = 0;
        if (withOperator)
            foreach (var (basis, n) in units)
                op += n * (rates[basis].OperatorRate ?? throw new ValidationFailedException(string.Format(Messages.RentalOperatorRateMissing, BasisLabel(basis))));

        var parts = units.Select(u => new RentalChargePartDto
        {
            Basis = u.Basis, Units = u.Units, Rate = rates[u.Basis].Rate, Amount = decimal.Round(u.Units * rates[u.Basis].Rate, 2),
        }).ToList();
        return new RentalEstimate(parts, decimal.Round(rent, 2), decimal.Round(op, 2), minimumApplied);
    }

    public static string BasisLabel(string basis)
        => Messages.ResourceManager.GetString("RentalBasis_" + basis, Messages.Culture) ?? basis;

    /// <summary>
    /// أسعار المعدة (أو الفئة) السارية في التاريخ لكل أساس: سعر المعدة يتقدم على فئتها ثم الفئة الأم، والأحدث سرياناً في المستوى نفسه.
    /// </summary>
    public static async Task<Dictionary<string, RentalPricedBasis>> RatesAtAsync(ErpDbContext db, Guid? equipmentId, Guid? categoryId, DateTime date,
        CancellationToken ct)
    {
        var day = date.Date;
        if (equipmentId is Guid eq && categoryId == null)
            categoryId = await db.Set<Equipment>().AsNoTracking().Where(e => e.Id == eq).Select(e => e.RentalCategoryId).FirstOrDefaultAsync(ct);
        Guid? parentId = categoryId is Guid cat
            ? await db.Set<RentalCategory>().AsNoTracking().Where(c => c.Id == cat).Select(c => c.ParentId).FirstOrDefaultAsync(ct)
            : null;
        var rows = await db.Set<RentalRate>().AsNoTracking()
            .Where(r => ((equipmentId != null && r.EquipmentId == equipmentId) || (categoryId != null && r.CategoryId == categoryId)
                         || (parentId != null && r.CategoryId == parentId))
                        && r.EffectiveFrom <= day && (r.EffectiveTo == null || r.EffectiveTo >= day))
            .ToListAsync(ct);
        int Level(RentalRate r) => r.EquipmentId != null ? 0 : r.CategoryId == categoryId ? 1 : 2;
        return rows.GroupBy(r => r.Basis)
            .Select(g => g.OrderBy(Level).ThenByDescending(r => r.EffectiveFrom).First())
            .ToDictionary(r => r.Basis, r => new RentalPricedBasis(r.Basis, r.Rate, r.OperatorRate, r.IncludedHours, r.OvertimeRate, r.MinimumPeriods));
    }
}

/// <summary>
/// توفر المعدات (مصدر واحد للتقويم والتقدير والحجز وإسناد المقاولات). الفترة المشغولة من:
/// <list type="bullet">
/// <item>إسناد نشط لمشروع مقاولات: من بدايته حتى الإرجاع (مفتوح).</item>
/// <item>حجز مؤقت لم ينقضِ موعده أو مؤكد: فترة سطره.</item>
/// <item>صيانة قيد التنفيذ: من بدئها حتى الإكمال (مفتوح)، ومفتوحة لم تبدأ: يوم موعدها.</item>
/// <item>المعدة خارج الخدمة أو مُعادة للمؤجر: كل الفترة.</item>
/// <item>المستأجرة من مورد: ما يقع خارج عقود إيجارها السارية (لا يُعاد تأجير ما لم يُستأجر).</item>
/// </list>
/// التعارض = تداخل الفترات (البداية قبل نهاية الأخرى والنهاية بعد بدايتها). الحفظ يقفل صفوف المعدات أولاً (تحديث بلا تغيير داخل
/// المعاملة) فيتسلسل حجزان أو حجز وإسناد متزامنان على المعدة نفسها.
/// </summary>
internal static class RentalAvailability
{
    public const string Assignment = "assignment";
    public const string Booking = "booking";
    public const string Contract = "contract";
    public const string Maintenance = "maintenance";
    public const string Status = "status";
    public const string SupplierContract = "supplier_contract";

    /// <summary>يقفل صفوف المعدات حتى نهاية المعاملة (يجب أن يُستدعى داخل معاملة).</summary>
    public static async Task LockAsync(ErpDbContext db, IReadOnlyCollection<Guid> equipmentIds, CancellationToken ct)
    {
        if (equipmentIds.Count == 0) return;
        await db.Set<Equipment>().Where(e => equipmentIds.Contains(e.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsRentable, e => e.IsRentable), ct);
    }

    /// <summary>
    /// الفترات التي تشغل المعدات وتتداخل مع [from, to). المستثنى: الحجز أو العقد قيد الحفظ نفسه، و<paramref name="parentContractId"/> للعقد
    /// المجدِّد (معدته ما زالت على السابق وتنتقل إليه بمحضر تسليمه).
    /// </summary>
    public static async Task<List<RentalBlockDto>> BlocksAsync(ErpDbContext db, IReadOnlyCollection<Guid> equipmentIds, DateTime from, DateTime to,
        Guid? excludeBookingId, DateTime now, CancellationToken ct, Guid? excludeContractId = null, Guid? parentContractId = null)
    {
        var blocks = new List<RentalBlockDto>();
        if (equipmentIds.Count == 0) return blocks;
        var ids = equipmentIds.ToList();

        // إسناد المقاولات: الوصف رقم العقد (اسم المشروع)
        var assignments = await (from a in db.Set<EquipmentAssignment>().AsNoTracking()
                                 join c in db.Set<CommercialContract>().AsNoTracking() on a.ContractId equals c.Id into cs
                                 from c in cs.DefaultIfEmpty()
                                 where ids.Contains(a.EquipmentId) && a.Status == EquipmentAssignmentStatuses.Active && a.FromDate < to
                                 select new { a.Id, a.EquipmentId, a.FromDate, Number = c != null ? c.ContractNumber : null, Name = c != null ? (c.ProjectName ?? c.Title) : null })
            .ToListAsync(ct);
        blocks.AddRange(assignments.Select(a => new RentalBlockDto
        {
            EquipmentId = a.EquipmentId, Kind = Assignment, ReferenceId = a.Id, From = a.FromDate,
            Reference = a.Number == null ? a.Name : $"{a.Number} · {a.Name}",
        }));

        var bookings = await (from l in db.Set<RentalBookingLine>().AsNoTracking()
                              join b in db.Set<RentalBooking>().AsNoTracking() on l.BookingId equals b.Id
                              where l.EquipmentId != null && ids.Contains(l.EquipmentId.Value) && l.From < to && l.To > @from
                                    && (excludeBookingId == null || b.Id != excludeBookingId)
                                    && (b.Status == BSt.Confirmed || (b.Status == BSt.Held && b.HoldUntil != null && b.HoldUntil > now))
                              select new { l.EquipmentId, l.From, l.To, b.Id, b.BookingNumber, b.Status })
            .ToListAsync(ct);
        blocks.AddRange(bookings.Select(b => new RentalBlockDto
        {
            EquipmentId = b.EquipmentId!.Value, Kind = Booking, ReferenceId = b.Id, Reference = b.BookingNumber, From = b.From, To = b.To, Status = b.Status,
        }));

        // عقود التأجير (المسودة والسارية): السطر الخارج يبقى شاغلاً حتى استلامه ولو تجاوز نهايته المخططة
        var contractLines = await (from l in db.Set<RentalContractLine>().AsNoTracking()
                                   join c in db.Set<RentalContract>().AsNoTracking() on l.ContractId equals c.Id
                                   where ids.Contains(l.EquipmentId)
                                         && (c.Status == RentalContractStatuses.Draft || c.Status == RentalContractStatuses.Active)
                                         && (l.Status == RentalLineStatuses.Pending || l.Status == RentalLineStatuses.Out)
                                         && (excludeContractId == null || c.Id != excludeContractId)
                                         && (parentContractId == null || c.Id != parentContractId)
                                   select new { l.EquipmentId, l.PlannedFrom, l.PlannedTo, l.ActualOut, l.Status, c.Id, c.ContractNumber })
            .ToListAsync(ct);
        foreach (var l in contractLines)
        {
            var block = new RentalBlockDto
            {
                EquipmentId = l.EquipmentId, Kind = Contract, ReferenceId = l.Id, Reference = l.ContractNumber, Status = l.Status,
                From = l.Status == RentalLineStatuses.Out && l.ActualOut is DateTime outAt && outAt < l.PlannedFrom ? outAt : l.PlannedFrom,
                To = l.Status == RentalLineStatuses.Out && l.PlannedTo <= now ? null : l.PlannedTo,
            };
            if (Overlaps(block, from, to)) blocks.Add(block);
        }

        var orders = await db.Set<EquipmentMaintenanceOrder>().AsNoTracking()
            .Where(o => ids.Contains(o.EquipmentId)
                        && (o.Status == EquipmentMaintenanceStatuses.InProgress || o.Status == EquipmentMaintenanceStatuses.Open))
            .Select(o => new { o.Id, o.EquipmentId, o.OrderNumber, o.Status, o.ScheduledDate, o.StartedAt })
            .ToListAsync(ct);
        foreach (var o in orders)
        {
            var block = o.Status == EquipmentMaintenanceStatuses.InProgress
                ? new RentalBlockDto { From = o.StartedAt ?? o.ScheduledDate, To = null }
                : new RentalBlockDto { From = o.ScheduledDate.Date, To = o.ScheduledDate.Date.AddDays(1) };
            block.EquipmentId = o.EquipmentId; block.Kind = Maintenance; block.ReferenceId = o.Id; block.Reference = o.OrderNumber; block.Status = o.Status;
            if (Overlaps(block, from, to)) blocks.Add(block);
        }

        var equipment = await db.Set<Equipment>().AsNoTracking().Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.Status, e.Ownership }).ToListAsync(ct);
        foreach (var e in equipment.Where(e => e.Status is EquipmentStatuses.OutOfService or EquipmentStatuses.Returned))
            blocks.Add(new RentalBlockDto { EquipmentId = e.Id, Kind = Status, Reference = e.Status, From = from, To = to, Status = e.Status });

        // المستأجرة من مورد: الفجوات خارج عقودها السارية داخل الفترة
        var rentedIds = equipment.Where(e => e.Ownership == EquipmentOwnerships.Rented && e.Status is not (EquipmentStatuses.OutOfService or EquipmentStatuses.Returned))
            .Select(e => e.Id).ToList();
        if (rentedIds.Count > 0)
        {
            var contracts = await db.Set<EquipmentRentalContract>().AsNoTracking()
                .Where(c => rentedIds.Contains(c.EquipmentId) && c.Status == EquipmentRentalStatuses.Active)
                .Select(c => new { c.EquipmentId, c.StartDate, c.EndDate }).ToListAsync(ct);
            foreach (var id in rentedIds)
            {
                var cursor = from;
                foreach (var c in contracts.Where(c => c.EquipmentId == id).OrderBy(c => c.StartDate))
                {
                    var start = c.StartDate.Date;
                    DateTime? end = c.EndDate?.Date.AddDays(1);
                    if (end != null && end <= cursor) continue;
                    if (start > cursor)
                    {
                        blocks.Add(new RentalBlockDto { EquipmentId = id, Kind = SupplierContract, From = cursor, To = start < to ? start : to });
                        if (start >= to) { cursor = to; break; }
                    }
                    cursor = end == null ? to : end.Value > cursor ? end.Value : cursor;
                    if (cursor >= to) break;
                }
                if (cursor < to) blocks.Add(new RentalBlockDto { EquipmentId = id, Kind = SupplierContract, From = cursor, To = to });
            }
        }
        return blocks.OrderBy(b => b.EquipmentId).ThenBy(b => b.From).ToList();
    }

    public static bool Overlaps(RentalBlockDto block, DateTime from, DateTime to) => block.From < to && (block.To == null || block.To > from);

    /// <summary>
    /// قبل إسناد المعدة لمشروع مقاولات (إسناد مفتوح من تاريخه): يقفل صفها ويرفض إن كان لها حجز مؤقت أو مؤكد ينتهي بعد التاريخ.
    /// بلا حجوزات (منشأة لا تؤجّر) لا يتغير شيء.
    /// </summary>
    public static async Task EnsureFreeForAssignmentAsync(ErpDbContext db, Guid equipmentId, DateTime date, CancellationToken ct)
    {
        await LockAsync(db, new[] { equipmentId }, ct);
        var now = SaudiTime.Now;
        var conflict = await (from l in db.Set<RentalBookingLine>().AsNoTracking()
                              join b in db.Set<RentalBooking>().AsNoTracking() on l.BookingId equals b.Id
                              where l.EquipmentId == equipmentId && l.To > date
                                    && (b.Status == BSt.Confirmed || (b.Status == BSt.Held && b.HoldUntil != null && b.HoldUntil > now))
                              orderby l.From
                              select new { b.BookingNumber, l.From, l.To })
            .FirstOrDefaultAsync(ct);
        if (conflict != null)
            throw new ConflictException(string.Format(Messages.EquipmentBookedForRental, conflict.BookingNumber, conflict.From, conflict.To));

        var rented = await (from l in db.Set<RentalContractLine>().AsNoTracking()
                            join c in db.Set<RentalContract>().AsNoTracking() on l.ContractId equals c.Id
                            where l.EquipmentId == equipmentId
                                  && (c.Status == RentalContractStatuses.Draft || c.Status == RentalContractStatuses.Active)
                                  && (l.Status == RentalLineStatuses.Out || (l.Status == RentalLineStatuses.Pending && l.PlannedTo > date))
                            orderby l.PlannedFrom
                            select new { c.ContractNumber, l.PlannedFrom, l.PlannedTo })
            .FirstOrDefaultAsync(ct);
        if (rented != null)
            throw new ConflictException(string.Format(Messages.EquipmentBookedForRental, rented.ContractNumber, rented.PlannedFrom, rented.PlannedTo));
    }
}

/// <summary>
/// تعارض المشغّل: الموظف لا يُعيَّن على سطرين متداخلين في عقود تأجير مسودة أو سارية (السطر بانتظار التسليم أو خارج)، ولا على فترة
/// إسناد معدة نشط في المقاولات يكون فيه مشغّلاً. يعيد وصف أول تعارض أو null.
/// </summary>
internal static class RentalOperators
{
    public static async Task<string?> ConflictAsync(ErpDbContext db, Guid employeeId, DateTime from, DateTime to, Guid? excludeLineId,
        Guid? excludeContractId, CancellationToken ct)
    {
        var rental = await (from l in db.Set<RentalContractLine>().AsNoTracking()
                            join c in db.Set<RentalContract>().AsNoTracking() on l.ContractId equals c.Id
                            where l.OperatorEmployeeId == employeeId && RentalLineStatuses.Blocking.Contains(l.Status)
                                  && RentalContractStatuses.Blocking.Contains(c.Status)
                                  && (excludeLineId == null || l.Id != excludeLineId)
                                  && (excludeContractId == null || c.Id != excludeContractId)
                                  && l.PlannedFrom < to && l.PlannedTo > @from
                            select c.ContractNumber).FirstOrDefaultAsync(ct);
        if (rental != null) return rental;
        var day = from.Date;
        var assignment = await (from a in db.Set<EquipmentAssignment>().AsNoTracking()
                                join e in db.Set<Equipment>().AsNoTracking() on a.EquipmentId equals e.Id
                                where a.OperatorEmployeeId == employeeId && a.Status == EquipmentAssignmentStatuses.Active
                                      && a.FromDate < to && (a.ToDate == null || a.ToDate >= day)
                                select e.Code).FirstOrDefaultAsync(ct);
        return assignment;
    }
}
