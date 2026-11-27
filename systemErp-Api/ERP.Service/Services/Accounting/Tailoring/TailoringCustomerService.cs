using System.Text.Json;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Shared;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// ملف العميل للخياطة وقياساته. القواعد:
/// <list type="bullet">
/// <item>العميل من سجل العملاء القائم؛ الامتداد (التفضيلات والملاحظات) سطر واحد له يُنشأ عند أول حفظ.</item>
/// <item>ملف القياس للابس ونوع لباس فعّال؛ نوعه لا يتغير بعد أول نسخة، ولا يُحذف وله نسخ (يُعطَّل).</item>
/// <item>النسخة لا تُعدَّل ولا تُحذف: القياس الجديد نسخة برقم تالٍ. القيم بمفاتيح حقول النوع الفعّالة، والإلزامي حاضر، وضمن الحدود.</item>
/// </list>
/// </summary>
public class TailoringCustomerService : ITailoringCustomerService
{
    private readonly ErpDbContext _db;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _user;

    public TailoringCustomerService(ErpDbContext db, IAuditService audit, ICurrentUser user) { _db = db; _audit = audit; _user = user; }

    public Task<DevExtreme.AspNet.Data.ResponseModel.LoadResult> LoadAsync(DataSourceLoadOptions options, CancellationToken ct = default)
    {
        var profiles = _db.Set<TailoringMeasurementProfile>().AsNoTracking();
        var versions = _db.Set<TailoringMeasurementVersion>().AsNoTracking();
        var rows = from c in _db.Set<Customer>().AsNoTracking()
                   select new TailoringCustomerRowDto
                   {
                       Id = c.Id, Code = c.Code, NameAr = c.NameAr, NameEn = c.NameEn, Phone = c.Phone, City = c.City, CurrentBalance = c.CurrentBalance,
                       ProfileCount = profiles.Count(p => p.CustomerId == c.Id && p.IsActive),
                       LastMeasuredAt = versions.Where(v => profiles.Any(p => p.Id == v.ProfileId && p.CustomerId == c.Id)).Max(v => (DateTime?)v.MeasuredAt),
                   };
        return EntityLoader.LoadRowsAsync(rows, options, nameof(TailoringCustomerRowDto.Id), ct,
            EntityLoader.Desc(nameof(TailoringCustomerRowDto.LastMeasuredAt)), EntityLoader.Asc(nameof(TailoringCustomerRowDto.NameAr)));
    }

    public async Task<TailoringCustomerDto> GetAsync(Guid customerId, CancellationToken ct = default)
    {
        var c = await _db.Set<Customer>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == customerId, ct)
            ?? throw new NotFoundException(Messages.TailoringCustomerNotFound);
        var ext = await _db.Set<TailoringCustomerProfile>().AsNoTracking().FirstOrDefaultAsync(x => x.CustomerId == customerId, ct);
        var profiles = await _db.Set<TailoringMeasurementProfile>().AsNoTracking().Where(p => p.CustomerId == customerId)
            .OrderByDescending(p => p.IsActive).ThenBy(p => p.WearerName).ToListAsync(ct);
        return new TailoringCustomerDto
        {
            CustomerId = c.Id, Code = c.Code, NameAr = c.NameAr, NameEn = c.NameEn, Phone = c.Phone, Email = c.Email, City = c.City,
            District = c.District, CurrentBalance = c.CurrentBalance, Preferences = ext?.Preferences, Notes = ext?.Notes,
            Profiles = await ToDtosAsync(profiles, ct),
        };
    }

    public async Task<TailoringCustomerDto> SaveProfileAsync(Guid customerId, SaveTailoringCustomerProfileDto r, CancellationToken ct = default)
    {
        if (!await _db.Set<Customer>().AnyAsync(x => x.Id == customerId, ct)) throw new NotFoundException(Messages.TailoringCustomerNotFound);
        var ext = await _db.Set<TailoringCustomerProfile>().FirstOrDefaultAsync(x => x.CustomerId == customerId, ct);
        if (ext == null) { ext = new TailoringCustomerProfile { CustomerId = customerId }; _db.Add(ext); }
        ext.Preferences = Clean(r.Preferences); ext.Notes = Clean(r.Notes);
        await _db.SaveChangesAsync(ct);
        return await GetAsync(customerId, ct);
    }

    // ---------- ملفات القياس ----------

    public async Task<TailoringMeasurementProfileDto> CreateMeasurementProfileAsync(Guid customerId, SaveTailoringMeasurementProfileDto r, CancellationToken ct = default)
    {
        if (!await _db.Set<Customer>().AnyAsync(x => x.Id == customerId, ct)) throw new NotFoundException(Messages.TailoringCustomerNotFound);
        await RequireActiveTypeAsync(r.GarmentTypeId, ct);
        if (string.IsNullOrWhiteSpace(r.WearerName)) throw new ValidationFailedException(Messages.TailoringWearerRequired);
        var p = new TailoringMeasurementProfile
        {
            CustomerId = customerId, GarmentTypeId = r.GarmentTypeId, WearerName = r.WearerName.Trim(), Relation = Clean(r.Relation),
            Notes = Clean(r.Notes), IsActive = r.IsActive,
        };
        _db.Add(p);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("TAILORING_MEASUREMENT_PROFILE_CREATED", nameof(TailoringMeasurementProfile), p.Id.ToString(), p.WearerName, ct);
        return (await ToDtosAsync(new() { p }, ct))[0];
    }

    public async Task<TailoringMeasurementProfileDto> UpdateMeasurementProfileAsync(Guid profileId, SaveTailoringMeasurementProfileDto r, CancellationToken ct = default)
    {
        var p = await _db.Set<TailoringMeasurementProfile>().FirstOrDefaultAsync(x => x.Id == profileId, ct)
            ?? throw new NotFoundException(Messages.TailoringProfileNotFound);
        if (string.IsNullOrWhiteSpace(r.WearerName)) throw new ValidationFailedException(Messages.TailoringWearerRequired);
        if (r.GarmentTypeId != p.GarmentTypeId)
        {
            if (p.CurrentVersionNo > 0) throw new ConflictException(Messages.TailoringProfileTypeLocked);
            await RequireActiveTypeAsync(r.GarmentTypeId, ct);
            p.GarmentTypeId = r.GarmentTypeId;
        }
        p.WearerName = r.WearerName.Trim(); p.Relation = Clean(r.Relation); p.Notes = Clean(r.Notes); p.IsActive = r.IsActive;
        await _db.SaveChangesAsync(ct);
        return (await ToDtosAsync(new() { p }, ct))[0];
    }

    public async Task DeleteMeasurementProfileAsync(Guid profileId, CancellationToken ct = default)
    {
        var p = await _db.Set<TailoringMeasurementProfile>().FirstOrDefaultAsync(x => x.Id == profileId, ct)
            ?? throw new NotFoundException(Messages.TailoringProfileNotFound);
        if (await _db.Set<TailoringMeasurementVersion>().AnyAsync(v => v.ProfileId == profileId, ct))
            throw new ConflictException(Messages.TailoringProfileHasVersions);
        _db.Remove(p);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("TAILORING_MEASUREMENT_PROFILE_DELETED", nameof(TailoringMeasurementProfile), p.Id.ToString(), p.WearerName, ct);
    }

    // ---------- النسخ ----------

    public async Task<List<TailoringMeasurementVersionDto>> VersionsAsync(Guid profileId, CancellationToken ct = default)
    {
        if (!await _db.Set<TailoringMeasurementProfile>().AnyAsync(x => x.Id == profileId, ct)) throw new NotFoundException(Messages.TailoringProfileNotFound);
        return (await _db.Set<TailoringMeasurementVersion>().AsNoTracking().Where(v => v.ProfileId == profileId)
            .OrderByDescending(v => v.VersionNo).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<TailoringMeasurementVersionDto> AddVersionAsync(Guid profileId, CreateTailoringMeasurementVersionDto r, CancellationToken ct = default)
        => await new TransactionRunner(_db).RunAsync(async token =>
        {
            var p = await _db.Set<TailoringMeasurementProfile>().FirstOrDefaultAsync(x => x.Id == profileId, token)
                ?? throw new NotFoundException(Messages.TailoringProfileNotFound);
            var type = await RequireActiveTypeAsync(p.GarmentTypeId, token);
            var fields = await _db.Set<TailoringMeasurementField>().AsNoTracking()
                .Where(f => f.GarmentTypeId == p.GarmentTypeId && f.IsActive).ToListAsync(token);
            var (values, choices) = ValidateValues(fields, r.Values, r.Choices);

            string? by = null;
            if (r.MeasuredByEmployeeId is Guid eid)
                by = await _db.Set<Employee>().AsNoTracking().Where(e => e.Id == eid).Select(e => e.NameAr).FirstOrDefaultAsync(token)
                    ?? throw new ValidationFailedException(Messages.TailoringEmployeeNotFound);

            var v = new TailoringMeasurementVersion
            {
                ProfileId = p.Id, VersionNo = p.CurrentVersionNo + 1, MeasuredAt = r.MeasuredAt ?? DateTime.UtcNow,
                MeasuredByEmployeeId = r.MeasuredByEmployeeId, MeasuredByName = by ?? _user.Name, Unit = type.MeasurementUnit,
                ValuesJson = JsonSerializer.Serialize(values), ChoicesJson = TailoringMeasurementJson.WriteSelected(choices), Notes = Clean(r.Notes), CreatedBy = _user.Name,
            };
            _db.Add(v);
            await _db.SaveChangesAsync(token);
            // تحديث الملف يتقدم مع الحفظ نفسه؛ نسختان متزامنتان بالرقم نفسه يرفض الفهرس الفريد إحداهما
            p.CurrentVersionNo = v.VersionNo; p.CurrentVersionId = v.Id;
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("TAILORING_MEASUREMENT_RECORDED", nameof(TailoringMeasurementProfile), p.Id.ToString(), $"{p.WearerName} • v{v.VersionNo}", token);
            return ToDto(v);
        }, ct);

    public async Task<TailoringMeasurementCompareDto> CompareAsync(Guid fromVersionId, Guid toVersionId, CancellationToken ct = default)
    {
        var both = await _db.Set<TailoringMeasurementVersion>().AsNoTracking().Where(v => v.Id == fromVersionId || v.Id == toVersionId).ToListAsync(ct);
        var a = both.FirstOrDefault(v => v.Id == fromVersionId) ?? throw new NotFoundException(Messages.TailoringVersionNotFound);
        var b = both.FirstOrDefault(v => v.Id == toVersionId) ?? throw new NotFoundException(Messages.TailoringVersionNotFound);
        if (a.ProfileId != b.ProfileId) throw new ValidationFailedException(Messages.TailoringCompareDifferentProfiles);
        var typeId = await _db.Set<TailoringMeasurementProfile>().Where(p => p.Id == a.ProfileId).Select(p => p.GarmentTypeId).FirstAsync(ct);
        var fields = await _db.Set<TailoringMeasurementField>().AsNoTracking().Where(f => f.GarmentTypeId == typeId)
            .OrderBy(f => f.SortOrder).ToListAsync(ct);
        var from = ToDto(a); var to = ToDto(b);
        var keys = fields.Select(f => f.Key).Concat(from.Values.Keys).Concat(to.Values.Keys).Distinct().ToList();
        var rows = keys.Where(k => !fields.Any(x => x.Key == k && x.InputType == TailoringOptionInputTypes.Select)).Select(k =>
        {
            var f = fields.FirstOrDefault(x => x.Key == k);
            var x1 = from.Values.GetValueOrDefault(k); var x2 = to.Values.GetValueOrDefault(k);
            return new TailoringMeasurementCompareRowDto
            {
                Key = k, NameAr = f?.NameAr ?? k, NameEn = f?.NameEn, From = x1, To = x2,
                Difference = x1 is decimal d1 && x2 is decimal d2 ? d2 - d1 : null,
            };
        }).Where(row => row.From != null || row.To != null).ToList();
        // صفات الجسم: اسما القيمتين (الرمز المحذوف من القائمة يظهر برمزه)
        foreach (var f in fields.Where(x => x.InputType == TailoringOptionInputTypes.Select))
        {
            var c1 = from.Choices.GetValueOrDefault(f.Key); var c2 = to.Choices.GetValueOrDefault(f.Key);
            if (c1 == null && c2 == null) continue;
            var list = TailoringMeasurementJson.ReadChoices(f.ChoicesJson);
            TailoringMeasurementChoiceDto? Of(string? code) => code == null ? null : list.FirstOrDefault(c => c.Code == code) ?? new() { Code = code, NameAr = code };
            rows.Add(new TailoringMeasurementCompareRowDto
            {
                Key = f.Key, NameAr = f.NameAr, NameEn = f.NameEn, InputType = TailoringOptionInputTypes.Select,
                FromLabel = Of(c1)?.NameAr, ToLabel = Of(c2)?.NameAr, FromLabelEn = Of(c1)?.NameEn, ToLabelEn = Of(c2)?.NameEn,
            });
        }
        return new TailoringMeasurementCompareDto { From = from, To = to, Rows = rows };
    }

    // ---------- مساعدات ----------

    /// <summary>الأرقام لحقول «رقم» وحدها، ورموز الاختيار لحقول «اختيار» وحدها ومن قائمتها؛ الإلزامي يُطلب من أيهما.</summary>
    private static (Dictionary<string, decimal?> Values, Dictionary<string, string?> Choices) ValidateValues(
        List<TailoringMeasurementField> fields, Dictionary<string, decimal?> input, Dictionary<string, string?> selected)
    {
        var choices = new Dictionary<string, string?>();
        foreach (var (rawKey, raw) in selected)
        {
            var key = rawKey.Trim().ToLowerInvariant();
            var f = fields.FirstOrDefault(x => x.Key == key && x.InputType == TailoringOptionInputTypes.Select)
                ?? throw new ValidationFailedException(string.Format(Messages.TailoringMeasurementUnknownField, rawKey));
            var code = raw?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(code)) continue;
            if (!TailoringMeasurementJson.ReadChoices(f.ChoicesJson).Any(c => c.Code == code))
                throw new ValidationFailedException(string.Format(Messages.TailoringMeasurementChoiceInvalid, f.NameAr));
            choices[key] = code;
        }

        var values = new Dictionary<string, decimal?>();
        foreach (var (rawKey, value) in input)
        {
            var key = rawKey.Trim().ToLowerInvariant();
            var f = fields.FirstOrDefault(x => x.Key == key && x.InputType != TailoringOptionInputTypes.Select)
                ?? throw new ValidationFailedException(string.Format(Messages.TailoringMeasurementUnknownField, rawKey));
            if (value is not decimal d) continue;
            if (d < 0 || (f.MinValue is decimal mn && d < mn) || (f.MaxValue is decimal mx && d > mx))
                throw new ValidationFailedException(string.Format(Messages.TailoringMeasurementOutOfRange,
                    $"{f.NameAr} ({f.MinValue?.ToString("0.##") ?? "0"} – {f.MaxValue?.ToString("0.##") ?? "∞"})"));
            values[key] = decimal.Round(d, 2);
        }
        var missing = fields.Where(f => f.IsRequired && !values.ContainsKey(f.Key) && !choices.ContainsKey(f.Key)).Select(f => f.NameAr).ToList();
        if (missing.Count > 0) throw new ValidationFailedException(string.Format(Messages.TailoringMeasurementRequired, string.Join("، ", missing)));
        if (values.Count == 0) throw new ValidationFailedException(string.Format(Messages.TailoringMeasurementRequired, "-"));
        return (values, choices);
    }

    private async Task<TailoringGarmentType> RequireActiveTypeAsync(Guid typeId, CancellationToken ct)
    {
        var t = await _db.Set<TailoringGarmentType>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == typeId, ct)
            ?? throw new NotFoundException(Messages.TailoringGarmentTypeNotFound);
        if (!t.IsActive) throw new ValidationFailedException(Messages.TailoringGarmentTypeInactive);
        return t;
    }

    private async Task<List<TailoringMeasurementProfileDto>> ToDtosAsync(List<TailoringMeasurementProfile> profiles, CancellationToken ct)
    {
        var typeIds = profiles.Select(p => p.GarmentTypeId).Distinct().ToList();
        var types = await _db.Set<TailoringGarmentType>().AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var currentIds = profiles.Where(p => p.CurrentVersionId != null).Select(p => p.CurrentVersionId!.Value).ToList();
        var current = await _db.Set<TailoringMeasurementVersion>().AsNoTracking().Where(v => currentIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        return profiles.Select(p => new TailoringMeasurementProfileDto
        {
            Id = p.Id, CustomerId = p.CustomerId, GarmentTypeId = p.GarmentTypeId, WearerName = p.WearerName, Relation = p.Relation, Notes = p.Notes,
            IsActive = p.IsActive, CurrentVersionNo = p.CurrentVersionNo,
            GarmentTypeName = types.GetValueOrDefault(p.GarmentTypeId)?.NameAr ?? string.Empty, GarmentTypeNameEn = types.GetValueOrDefault(p.GarmentTypeId)?.NameEn,
            Current = p.CurrentVersionId is Guid vid && current.TryGetValue(vid, out var v) ? ToDto(v) : null,
        }).ToList();
    }

    private static TailoringMeasurementVersionDto ToDto(TailoringMeasurementVersion v) => new()
    {
        Id = v.Id, ProfileId = v.ProfileId, VersionNo = v.VersionNo, MeasuredAt = v.MeasuredAt, MeasuredByEmployeeId = v.MeasuredByEmployeeId,
        MeasuredByName = v.MeasuredByName, Unit = v.Unit, Notes = v.Notes, CreatedBy = v.CreatedBy, CreatedAt = v.CreatedAt,
        Values = JsonSerializer.Deserialize<Dictionary<string, decimal?>>(v.ValuesJson) ?? new(),
        Choices = TailoringMeasurementJson.ReadSelected(v.ChoicesJson),
    };

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
}
