using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// إعداد التأجير للعملاء فوق سجل المعدات القائم: الفئات (رمز فريد، فئة أم بلا دورة، لا تُحذف وعليها معدات أو أسعار أو فئات فرعية)،
/// والأسعار (فئة أو معدة، بلا تداخل فترتي سريان لنفس الهدف والأساس)، وإعداد تأجير المعدة (قابلة للتأجير، فئتها، موقعها). لا يمس
/// تكاليف المعدة ولا إسنادها للمقاولات.
/// </summary>
public class RentalSetupService : IRentalSetupService
{
    private readonly ErpDbContext _db;
    private readonly IAuditService _audit;

    public RentalSetupService(ErpDbContext db, IAuditService audit) { _db = db; _audit = audit; }

    // ---------- الفئات ----------

    public async Task<List<RentalCategoryDto>> CategoriesAsync(CancellationToken ct = default)
    {
        var counts = await _db.Set<Equipment>().AsNoTracking().Where(e => e.RentalCategoryId != null)
            .GroupBy(e => e.RentalCategoryId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return (await _db.Set<RentalCategory>().AsNoTracking().OrderBy(c => c.Code).ToListAsync(ct))
            .Select(c => ToDto(c, counts.GetValueOrDefault(c.Id))).ToList();
    }

    public async Task<RentalCategoryDto> CreateCategoryAsync(SaveRentalCategoryDto request, CancellationToken ct = default)
    {
        var c = new RentalCategory();
        await ApplyAsync(c, request, ct);
        _db.Add(c);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("RENTAL_CATEGORY_CREATED", nameof(RentalCategory), c.Id.ToString(), $"{c.Code} • {c.NameAr}", ct);
        return ToDto(c, 0);
    }

    public async Task<RentalCategoryDto> UpdateCategoryAsync(Guid id, SaveRentalCategoryDto request, CancellationToken ct = default)
    {
        var c = await _db.Set<RentalCategory>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(Messages.RentalCategoryNotFound);
        await ApplyAsync(c, request, ct);
        await _db.SaveChangesAsync(ct);
        return ToDto(c, await _db.Set<Equipment>().CountAsync(e => e.RentalCategoryId == id, ct));
    }

    public async Task DeleteCategoryAsync(Guid id, CancellationToken ct = default)
    {
        var c = await _db.Set<RentalCategory>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(Messages.RentalCategoryNotFound);
        if (await _db.Set<Equipment>().AnyAsync(e => e.RentalCategoryId == id, ct)
            || await _db.Set<RentalRate>().AnyAsync(r => r.CategoryId == id, ct)
            || await _db.Set<RentalCategory>().AnyAsync(x => x.ParentId == id, ct)
            || await _db.Set<RentalBookingLine>().AnyAsync(l => l.CategoryId == id, ct))
            throw new ConflictException(Messages.RentalCategoryInUse);
        _db.Remove(c);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("RENTAL_CATEGORY_DELETED", nameof(RentalCategory), id.ToString(), $"{c.Code} • {c.NameAr}", ct);
    }

    private async Task ApplyAsync(RentalCategory c, SaveRentalCategoryDto r, CancellationToken ct)
    {
        var name = r.NameAr?.Trim() ?? string.Empty;
        if (name.Length == 0) throw new ValidationFailedException(Messages.RentalCategoryNameRequired);
        var code = Trim(r.Code, 32)?.ToUpperInvariant();
        if (code != null && await _db.Set<RentalCategory>().AnyAsync(x => x.Code == code && x.Id != c.Id, ct))
            throw new ValidationFailedException(string.Format(Messages.RentalCategoryCodeTaken, code));
        if (r.ParentId is Guid parent)
        {
            if (parent == c.Id) throw new ValidationFailedException(Messages.RentalCategoryCycle);
            // لا دورة: الأم ليست هذه الفئة ولا أحد فروعها
            var cursor = (Guid?)parent;
            for (var depth = 0; cursor is Guid at; depth++)
            {
                if (at == c.Id || depth > 32) throw new ValidationFailedException(Messages.RentalCategoryCycle);
                var row = await _db.Set<RentalCategory>().AsNoTracking().Where(x => x.Id == at).Select(x => new { x.ParentId }).FirstOrDefaultAsync(ct)
                    ?? throw new ValidationFailedException(Messages.RentalCategoryNotFound);
                cursor = row.ParentId;
            }
        }
        c.Code = code ?? (string.IsNullOrEmpty(c.Code) ? await NextCategoryCodeAsync(ct) : c.Code);
        c.NameAr = name[..Math.Min(name.Length, 200)]; c.NameEn = Trim(r.NameEn, 200); c.ParentId = r.ParentId;
        c.Description = Trim(r.Description, 500); c.IsActive = r.IsActive;
    }

    private async Task<string> NextCategoryCodeAsync(CancellationToken ct)
    {
        var n = await _db.Set<RentalCategory>().CountAsync(ct);
        while (true)
        {
            var code = $"RCAT-{++n:000}";
            if (!await _db.Set<RentalCategory>().AnyAsync(x => x.Code == code, ct)) return code;
        }
    }

    private static RentalCategoryDto ToDto(RentalCategory c, int count) => new()
    {
        Id = c.Id, Code = c.Code, NameAr = c.NameAr, NameEn = c.NameEn, ParentId = c.ParentId, Description = c.Description, IsActive = c.IsActive,
        EquipmentCount = count,
    };

    // ---------- الأسعار ----------

    public async Task<List<RentalRateDto>> RatesAsync(Guid? categoryId, Guid? equipmentId, CancellationToken ct = default)
    {
        var q = _db.Set<RentalRate>().AsNoTracking().AsQueryable();
        if (categoryId is Guid c) q = q.Where(r => r.CategoryId == c);
        if (equipmentId is Guid e) q = q.Where(r => r.EquipmentId == e);
        return await RateDtosAsync(await q.OrderBy(r => r.CategoryId).ThenBy(r => r.EquipmentId).ThenBy(r => r.Basis).ThenByDescending(r => r.EffectiveFrom).ToListAsync(ct), ct);
    }

    public async Task<RentalRateDto> CreateRateAsync(SaveRentalRateDto request, CancellationToken ct = default)
    {
        var r = new RentalRate();
        await ApplyAsync(r, request, ct);
        _db.Add(r);
        await _db.SaveChangesAsync(ct);
        await LogRateAsync("RENTAL_RATE_CREATED", r, ct);
        return (await RateDtosAsync(new List<RentalRate> { r }, ct)).Single();
    }

    public async Task<RentalRateDto> UpdateRateAsync(Guid id, SaveRentalRateDto request, CancellationToken ct = default)
    {
        var r = await _db.Set<RentalRate>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(Messages.RentalRateNotFound);
        await ApplyAsync(r, request, ct);
        await _db.SaveChangesAsync(ct);
        await LogRateAsync("RENTAL_RATE_UPDATED", r, ct);
        return (await RateDtosAsync(new List<RentalRate> { r }, ct)).Single();
    }

    public async Task DeleteRateAsync(Guid id, CancellationToken ct = default)
    {
        var r = await _db.Set<RentalRate>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(Messages.RentalRateNotFound);
        // الحجوزات تحفظ لقطة سعرها، فحذف السعر لا يغيّرها
        _db.Remove(r);
        await _db.SaveChangesAsync(ct);
        await LogRateAsync("RENTAL_RATE_DELETED", r, ct);
    }

    private async Task ApplyAsync(RentalRate rate, SaveRentalRateDto r, CancellationToken ct)
    {
        var errors = new List<string>();
        if ((r.CategoryId == null) == (r.EquipmentId == null)) errors.Add(Messages.RentalRateTargetRequired);
        var basis = r.Basis?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!RentalRateBases.IsValid(basis)) errors.Add(Messages.RentalBasisInvalid);
        if (r.Rate <= 0) errors.Add(Messages.RentalRateMustBePositive);
        if (r.OperatorRate < 0 || r.IncludedHours < 0 || r.OvertimeRate < 0) errors.Add(Messages.RentalRateNegative);
        if (r.MinimumPeriods < 0) errors.Add(Messages.RentalRateNegative);
        var from = (r.EffectiveFrom ?? SaudiTime.Today).Date;
        var to = r.EffectiveTo?.Date;
        if (to < from) errors.Add(Messages.RentalRateEffectiveRangeInvalid);
        if (r.CategoryId is Guid cat && !await _db.Set<RentalCategory>().AnyAsync(c => c.Id == cat, ct)) errors.Add(Messages.RentalCategoryNotFound);
        if (r.EquipmentId is Guid eq && !await _db.Set<Equipment>().AnyAsync(e => e.Id == eq, ct)) errors.Add(Messages.RentalEquipmentNotFound);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        // فترتا سريان متداخلتان لنفس الهدف والأساس تجعلان السعر الساري ملتبساً
        var overlap = await _db.Set<RentalRate>().AnyAsync(x => x.Id != rate.Id && x.Basis == basis
            && x.CategoryId == r.CategoryId && x.EquipmentId == r.EquipmentId
            && (to == null || x.EffectiveFrom <= to) && (x.EffectiveTo == null || x.EffectiveTo >= from), ct);
        if (overlap) throw new ValidationFailedException(Messages.RentalRateOverlap);

        rate.CategoryId = r.CategoryId; rate.EquipmentId = r.EquipmentId; rate.Basis = basis; rate.Rate = decimal.Round(r.Rate, 4);
        rate.OperatorRate = r.OperatorRate is decimal op ? decimal.Round(op, 4) : null;
        rate.IncludedHours = r.IncludedHours; rate.OvertimeRate = decimal.Round(r.OvertimeRate, 4); rate.MinimumPeriods = r.MinimumPeriods;
        rate.EffectiveFrom = from; rate.EffectiveTo = to; rate.Notes = Trim(r.Notes, 500);
    }

    private async Task<List<RentalRateDto>> RateDtosAsync(List<RentalRate> list, CancellationToken ct)
    {
        var catIds = list.Where(r => r.CategoryId != null).Select(r => r.CategoryId!.Value).Distinct().ToList();
        var eqIds = list.Where(r => r.EquipmentId != null).Select(r => r.EquipmentId!.Value).Distinct().ToList();
        var cats = await _db.Set<RentalCategory>().AsNoTracking().Where(c => catIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.NameAr, ct);
        var eqs = await _db.Set<Equipment>().AsNoTracking().Where(e => eqIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => new { e.Code, e.NameAr }, ct);
        return list.Select(r => new RentalRateDto
        {
            Id = r.Id, CategoryId = r.CategoryId, EquipmentId = r.EquipmentId, Basis = r.Basis, Rate = r.Rate, OperatorRate = r.OperatorRate,
            IncludedHours = r.IncludedHours, OvertimeRate = r.OvertimeRate, MinimumPeriods = r.MinimumPeriods, EffectiveFrom = r.EffectiveFrom,
            EffectiveTo = r.EffectiveTo, Notes = r.Notes,
            CategoryName = r.CategoryId is Guid c ? cats.GetValueOrDefault(c) : null,
            EquipmentCode = r.EquipmentId is Guid e ? eqs.GetValueOrDefault(e)?.Code : null,
            EquipmentName = r.EquipmentId is Guid e2 ? eqs.GetValueOrDefault(e2)?.NameAr : null,
        }).ToList();
    }

    private Task LogRateAsync(string action, RentalRate r, CancellationToken ct)
        => _audit.LogAsync(action, nameof(RentalRate), r.Id.ToString(),
            $"{(r.EquipmentId != null ? "معدة " + r.EquipmentId : "فئة " + r.CategoryId)} • {r.Basis} • {r.Rate:0.####} • من {r.EffectiveFrom:yyyy-MM-dd}", ct);

    // ---------- المعدات ----------

    public async Task<List<RentalEquipmentDto>> EquipmentAsync(bool rentableOnly, CancellationToken ct = default)
        => await EquipmentRowsAsync(_db, rentableOnly ? e => e.IsRentable : null, ct);

    public async Task<RentalEquipmentDto> UpdateEquipmentAsync(Guid equipmentId, UpdateRentalEquipmentDto request, CancellationToken ct = default)
    {
        var e = await _db.Set<Equipment>().FirstOrDefaultAsync(x => x.Id == equipmentId, ct) ?? throw new NotFoundException(Messages.RentalEquipmentNotFound);
        if (request.RentalCategoryId is Guid cat && !await _db.Set<RentalCategory>().AnyAsync(c => c.Id == cat, ct))
            throw new ValidationFailedException(Messages.RentalCategoryNotFound);
        if (request.HomeWarehouseId is Guid wh && !await _db.Set<Warehouse>().AnyAsync(w => w.Id == wh, ct))
            throw new ValidationFailedException(Messages.RentalWarehouseNotFound);
        e.IsRentable = request.IsRentable; e.RentalCategoryId = request.RentalCategoryId; e.HomeWarehouseId = request.HomeWarehouseId;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("RENTAL_EQUIPMENT_UPDATED", nameof(Equipment), e.Id.ToString(),
            $"{e.Code}: قابلة للتأجير {(e.IsRentable ? "نعم" : "لا")} • فئة {e.RentalCategoryId} • موقع {e.HomeWarehouseId}", ct);
        return (await EquipmentRowsAsync(_db, x => x.Id == equipmentId, ct)).Single();
    }

    /// <summary>صفوف المعدات بإعداد تأجيرها (للإعداد والتقويم).</summary>
    internal static async Task<List<RentalEquipmentDto>> EquipmentRowsAsync(ErpDbContext db, System.Linq.Expressions.Expression<Func<Equipment, bool>>? filter,
        CancellationToken ct)
    {
        var q = db.Set<Equipment>().AsNoTracking();
        if (filter != null) q = q.Where(filter);
        return await (from e in q
                      join c in db.Set<RentalCategory>().AsNoTracking() on e.RentalCategoryId equals c.Id into cs
                      from c in cs.DefaultIfEmpty()
                      join w in db.Set<Warehouse>().AsNoTracking() on e.HomeWarehouseId equals w.Id into ws
                      from w in ws.DefaultIfEmpty()
                      join s in db.Set<Supplier>().AsNoTracking() on e.SupplierId equals s.Id into ss
                      from s in ss.DefaultIfEmpty()
                      orderby e.Code
                      select new RentalEquipmentDto
                      {
                          Id = e.Id, Code = e.Code, NameAr = e.NameAr, NameEn = e.NameEn, Ownership = e.Ownership, Status = e.Status,
                          SupplierName = s != null ? s.NameAr : null, MeterHours = e.MeterHours, IsRentable = e.IsRentable,
                          RentalCategoryId = e.RentalCategoryId, RentalCategoryName = c != null ? c.NameAr : null,
                          HomeWarehouseId = e.HomeWarehouseId, HomeWarehouseName = w != null ? w.NameAr : null,
                      }).ToListAsync(ct);
    }

    private static string? Trim(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length > max ? v[..max] : v;
    }
}
