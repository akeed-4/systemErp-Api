using System.Text.RegularExpressions;
using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// إعداد الخياطة. القواعد:
/// <list type="bullet">
/// <item>الإعداد سطر واحد للمنشأة؛ غيابه = منشأة لم تفتح الوحدة بعد، فتُزرع معه الافتراضيات (قالب الثوب السعودي ومراحل الإنتاج) مرة واحدة.</item>
/// <item>نوع اللباس برمز فريد وصنف خدمي اختياري هنا (إلزامي عند الفوترة). الحفظ يزامن الحقول والخيارات بمعرّفاتها.</item>
/// <item>النوع المستخدم في ملفات قياس: مفتاح الحقل القائم لا يتغير، والحقل المحذوف يُعطَّل لأن النسخ المحفوظة تشير لمفتاحه، والنوع لا يُحذف.</item>
/// <item>خيار «اختيار» يحتاج قيمة فعّالة واحدة على الأقل، وافتراضي واحد على الأكثر.</item>
/// </list>
/// </summary>
public partial class TailoringSetupService : ITailoringSetupService
{
    private const int MaxFields = 80, MaxOptions = 60, MaxValues = 60;

    [GeneratedRegex("^[a-z][a-z0-9_]{0,47}$")]
    private static partial Regex KeyPattern();

    private readonly ErpDbContext _db;
    private readonly IAuditService _audit;

    public TailoringSetupService(ErpDbContext db, IAuditService audit) { _db = db; _audit = audit; }

    // ---------- الإعداد العام ----------

    public async Task<TailoringSettingsDto> SettingsAsync(CancellationToken ct = default)
    {
        await EnsureSeededAsync(ct);
        return await ToDtoAsync(await _db.Set<TailoringSettings>().AsNoTracking().FirstAsync(ct), ct);
    }

    public async Task<TailoringSettingsDto> SaveSettingsAsync(SaveTailoringSettingsDto r, CancellationToken ct = default)
    {
        if (r.DefaultLeadDays < 0 || r.MinAdvancePercent < 0 || r.MinAdvancePercent > 100)
            throw new ValidationFailedException(Messages.TailoringSettingsInvalid);
        if (r.DefaultWarehouseId is Guid wh && !await _db.Set<Warehouse>().AnyAsync(w => w.Id == wh, ct))
            throw new ValidationFailedException(Messages.TailoringWarehouseNotFound);
        await EnsureSeededAsync(ct);
        var s = await _db.Set<TailoringSettings>().FirstAsync(ct);
        s.DefaultWarehouseId = r.DefaultWarehouseId; s.DefaultLeadDays = r.DefaultLeadDays;
        s.MinAdvancePercent = decimal.Round(r.MinAdvancePercent, 2); s.RequireQualityCheck = r.RequireQualityCheck;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("TAILORING_SETTINGS_SAVED", nameof(TailoringSettings), s.Id.ToString(), $"{s.DefaultLeadDays}d • {s.MinAdvancePercent:0.##}%", ct);
        return await ToDtoAsync(s, ct);
    }

    private async Task<TailoringSettingsDto> ToDtoAsync(TailoringSettings s, CancellationToken ct) => new()
    {
        DefaultWarehouseId = s.DefaultWarehouseId, DefaultLeadDays = s.DefaultLeadDays, MinAdvancePercent = s.MinAdvancePercent,
        RequireQualityCheck = s.RequireQualityCheck,
        DefaultWarehouseName = s.DefaultWarehouseId is Guid wh
            ? await _db.Set<Warehouse>().AsNoTracking().Where(w => w.Id == wh).Select(w => w.NameAr).FirstOrDefaultAsync(ct) : null,
    };

    // ---------- أنواع الملابس ----------

    public async Task<List<TailoringGarmentTypeDto>> GarmentTypesAsync(bool includeInactive, CancellationToken ct = default)
    {
        await EnsureSeededAsync(ct);
        var types = await _db.Set<TailoringGarmentType>().AsNoTracking().Where(t => includeInactive || t.IsActive)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.NameAr).ToListAsync(ct);
        return await ToDtosAsync(types, ct);
    }

    public async Task<TailoringGarmentTypeDto> GarmentTypeAsync(Guid id, CancellationToken ct = default)
    {
        var t = await _db.Set<TailoringGarmentType>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(Messages.TailoringGarmentTypeNotFound);
        return (await ToDtosAsync(new List<TailoringGarmentType> { t }, ct))[0];
    }

    public Task<TailoringGarmentTypeDto> CreateGarmentTypeAsync(SaveTailoringGarmentTypeDto request, CancellationToken ct = default)
        => SaveGarmentTypeAsync(null, request, ct);

    public Task<TailoringGarmentTypeDto> UpdateGarmentTypeAsync(Guid id, SaveTailoringGarmentTypeDto request, CancellationToken ct = default)
        => SaveGarmentTypeAsync(id, request, ct);

    private async Task<TailoringGarmentTypeDto> SaveGarmentTypeAsync(Guid? id, SaveTailoringGarmentTypeDto r, CancellationToken ct)
    {
        await ValidateTypeAsync(r, ct);
        var id2 = await new TransactionRunner(_db).RunAsync(async token =>
        {
            TailoringGarmentType t;
            if (id is Guid existing)
                t = await _db.Set<TailoringGarmentType>().FirstOrDefaultAsync(x => x.Id == existing, token)
                    ?? throw new NotFoundException(Messages.TailoringGarmentTypeNotFound);
            else { t = new TailoringGarmentType(); _db.Add(t); }

            var code = string.IsNullOrWhiteSpace(r.Code) ? (id == null ? await NextCodeAsync<TailoringGarmentType>("GT-", token) : t.Code) : r.Code.Trim().ToUpperInvariant();
            if (await _db.Set<TailoringGarmentType>().AnyAsync(x => x.Code == code && x.Id != t.Id, token))
                throw new ConflictException(Messages.TailoringCodeDuplicate);
            var inUse = id != null && await InUseAsync(t.Id, token);
            t.Code = code; t.NameAr = r.NameAr.Trim(); t.NameEn = Clean(r.NameEn); t.Category = r.Category; t.ProductId = r.ProductId;
            t.BasePrice = decimal.Round(r.BasePrice, 2); t.DefaultFabricMeters = decimal.Round(r.DefaultFabricMeters, 3);
            t.MeasurementUnit = r.MeasurementUnit; t.SortOrder = r.SortOrder; t.IsActive = r.IsActive; t.Notes = Clean(r.Notes);
            await _db.SaveChangesAsync(token);

            await SyncFieldsAsync(t.Id, r.Fields, inUse, token);
            await SyncOptionsAsync(t.Id, r.Options, token);
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync(id == null ? "TAILORING_GARMENT_TYPE_CREATED" : "TAILORING_GARMENT_TYPE_UPDATED",
                nameof(TailoringGarmentType), t.Id.ToString(), $"{t.Code} • {t.NameAr}", token);
            return t.Id;
        }, ct);
        return await GarmentTypeAsync(id2, ct);
    }

    private async Task ValidateTypeAsync(SaveTailoringGarmentTypeDto r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.NameAr)) throw new ValidationFailedException(Messages.TailoringNameRequired);
        if (!TailoringGarmentCategories.IsValid(r.Category) || !TailoringMeasurementUnits.IsValid(r.MeasurementUnit)
            || r.BasePrice < 0 || r.DefaultFabricMeters < 0)
            throw new ValidationFailedException(Messages.TailoringGarmentTypeInvalid);
        if (r.ProductId is Guid pid && !await _db.Set<Product>().AnyAsync(p => p.Id == pid && p.ItemType == ProductItemTypes.Service, ct))
            throw new ValidationFailedException(Messages.TailoringProductNotService);
        if (r.Fields.Count > MaxFields || r.Options.Count > MaxOptions || r.Options.Any(o => o.Values.Count > MaxValues))
            throw new ValidationFailedException(Messages.TailoringGarmentTypeInvalid);

        foreach (var f in r.Fields)
        {
            f.Key = (f.Key ?? string.Empty).Trim().ToLowerInvariant();
            if (!KeyPattern().IsMatch(f.Key)) throw new ValidationFailedException(string.Format(Messages.TailoringKeyInvalid, f.Key));
            if (string.IsNullOrWhiteSpace(f.NameAr)) throw new ValidationFailedException(Messages.TailoringNameRequired);
            if (f.MinValue < 0 || f.MaxValue < 0 || (f.MinValue is decimal mn && f.MaxValue is decimal mx && mn > mx))
                throw new ValidationFailedException(string.Format(Messages.TailoringFieldRangeInvalid, f.NameAr));
            f.InputType = string.IsNullOrWhiteSpace(f.InputType) ? TailoringOptionInputTypes.Number : f.InputType.Trim().ToLowerInvariant();
            if (f.InputType is not (TailoringOptionInputTypes.Number or TailoringOptionInputTypes.Select))
                throw new ValidationFailedException(Messages.TailoringGarmentTypeInvalid);
            if (f.InputType == TailoringOptionInputTypes.Number) { f.Choices.Clear(); continue; }
            // صفة جسم: قيم من قائمة بلا حدود رقمية
            f.MinValue = null; f.MaxValue = null;
            foreach (var c in f.Choices)
            {
                c.Code = (c.Code ?? string.Empty).Trim().ToLowerInvariant();
                if (!KeyPattern().IsMatch(c.Code)) throw new ValidationFailedException(string.Format(Messages.TailoringKeyInvalid, c.Code));
                if (string.IsNullOrWhiteSpace(c.NameAr)) throw new ValidationFailedException(Messages.TailoringNameRequired);
                c.NameAr = c.NameAr.Trim(); c.NameEn = Clean(c.NameEn);
            }
            if (f.Choices.Count == 0 || f.Choices.Count > MaxValues) throw new ValidationFailedException(string.Format(Messages.TailoringFieldChoicesRequired, f.NameAr));
            var dupChoice = f.Choices.GroupBy(c => c.Code).FirstOrDefault(g => g.Count() > 1);
            if (dupChoice != null) throw new ValidationFailedException(string.Format(Messages.TailoringKeyDuplicate, dupChoice.Key));
        }
        var dupField = r.Fields.GroupBy(f => f.Key).FirstOrDefault(g => g.Count() > 1);
        if (dupField != null) throw new ValidationFailedException(string.Format(Messages.TailoringKeyDuplicate, dupField.Key));

        foreach (var o in r.Options)
        {
            o.Key = (o.Key ?? string.Empty).Trim().ToLowerInvariant();
            if (!KeyPattern().IsMatch(o.Key)) throw new ValidationFailedException(string.Format(Messages.TailoringKeyInvalid, o.Key));
            if (string.IsNullOrWhiteSpace(o.NameAr) || !TailoringOptionInputTypes.IsValid(o.InputType))
                throw new ValidationFailedException(Messages.TailoringGarmentTypeInvalid);
            if (o.InputType != TailoringOptionInputTypes.Select) { o.Values.Clear(); continue; }
            foreach (var v in o.Values)
            {
                v.Code = (v.Code ?? string.Empty).Trim().ToLowerInvariant();
                if (!KeyPattern().IsMatch(v.Code)) throw new ValidationFailedException(string.Format(Messages.TailoringKeyInvalid, v.Code));
                if (string.IsNullOrWhiteSpace(v.NameAr)) throw new ValidationFailedException(Messages.TailoringNameRequired);
            }
            if (o.IsActive && !o.Values.Any(v => v.IsActive)) throw new ValidationFailedException(string.Format(Messages.TailoringOptionValuesRequired, o.NameAr));
            var dupValue = o.Values.GroupBy(v => v.Code).FirstOrDefault(g => g.Count() > 1);
            if (dupValue != null) throw new ValidationFailedException(string.Format(Messages.TailoringKeyDuplicate, dupValue.Key));
            if (o.Values.Count(v => v.IsDefault) > 1) throw new ValidationFailedException(string.Format(Messages.TailoringOptionDefaultMultiple, o.NameAr));
        }
        var dupOption = r.Options.GroupBy(o => o.Key).FirstOrDefault(g => g.Count() > 1);
        if (dupOption != null) throw new ValidationFailedException(string.Format(Messages.TailoringKeyDuplicate, dupOption.Key));
    }

    private async Task SyncFieldsAsync(Guid typeId, List<TailoringMeasurementFieldDto> rows, bool inUse, CancellationToken ct)
    {
        var existing = await _db.Set<TailoringMeasurementField>().Where(f => f.GarmentTypeId == typeId).ToListAsync(ct);
        foreach (var f in existing.Where(e => !rows.Any(r => r.Id == e.Id)))
        {
            if (inUse) f.IsActive = false; // النسخ المحفوظة تشير لمفتاحه
            else _db.Remove(f);
        }
        foreach (var r in rows)
        {
            var f = r.Id is Guid fid ? existing.FirstOrDefault(e => e.Id == fid) : null;
            if (f == null) { f = new TailoringMeasurementField { GarmentTypeId = typeId }; _db.Add(f); }
            else if (inUse && f.Key != r.Key) throw new ConflictException(string.Format(Messages.TailoringFieldKeyLocked, f.Key));
            else if (inUse && f.InputType != r.InputType) throw new ConflictException(string.Format(Messages.TailoringFieldTypeLocked, f.NameAr));
            f.Key = r.Key; f.NameAr = r.NameAr.Trim(); f.NameEn = Clean(r.NameEn); f.GroupName = Clean(r.GroupName); f.SortOrder = r.SortOrder;
            f.IsRequired = r.IsRequired; f.MinValue = r.MinValue; f.MaxValue = r.MaxValue; f.IsActive = r.IsActive;
            f.InputType = r.InputType; f.ChoicesJson = TailoringMeasurementJson.WriteChoices(r.Choices);
        }
        // مفتاح محذوف لم يُعطَّل بعد وأُعيد استخدامه بسطر جديد: الفهرس الفريد يرفضه برسالة صريحة قبله
        var kept = existing.Where(e => _db.Entry(e).State != EntityState.Deleted).ToList();
        foreach (var r in rows.Where(r => r.Id == null))
            if (kept.Any(e => e.Key == r.Key && !rows.Any(x => x.Id == e.Id)))
                throw new ConflictException(string.Format(Messages.TailoringFieldKeyLocked, r.Key));
    }

    private async Task SyncOptionsAsync(Guid typeId, List<TailoringDesignOptionDto> rows, CancellationToken ct)
    {
        var existing = await _db.Set<TailoringDesignOption>().Where(o => o.GarmentTypeId == typeId).ToListAsync(ct);
        var ids = existing.Select(o => o.Id).ToList();
        var values = await _db.Set<TailoringDesignOptionValue>().Where(v => ids.Contains(v.OptionId)).ToListAsync(ct);
        foreach (var o in existing.Where(e => !rows.Any(r => r.Id == e.Id)))
        {
            _db.RemoveRange(values.Where(v => v.OptionId == o.Id));
            _db.Remove(o);
        }
        await _db.SaveChangesAsync(ct);
        foreach (var r in rows)
        {
            var o = r.Id is Guid oid ? existing.FirstOrDefault(e => e.Id == oid) : null;
            if (o == null) { o = new TailoringDesignOption { GarmentTypeId = typeId }; _db.Add(o); }
            o.Key = r.Key; o.NameAr = r.NameAr.Trim(); o.NameEn = Clean(r.NameEn); o.InputType = r.InputType;
            o.IsRequired = r.IsRequired; o.SortOrder = r.SortOrder; o.IsActive = r.IsActive;
            await _db.SaveChangesAsync(ct);

            var own = values.Where(v => v.OptionId == o.Id).ToList();
            _db.RemoveRange(own.Where(e => !r.Values.Any(x => x.Id == e.Id)));
            foreach (var x in r.Values)
            {
                var v = x.Id is Guid vid ? own.FirstOrDefault(e => e.Id == vid) : null;
                if (v == null) { v = new TailoringDesignOptionValue { OptionId = o.Id }; _db.Add(v); }
                v.Code = x.Code; v.NameAr = x.NameAr.Trim(); v.NameEn = Clean(x.NameEn); v.PriceDelta = decimal.Round(x.PriceDelta, 2);
                v.SortOrder = x.SortOrder; v.IsDefault = x.IsDefault; v.IsActive = x.IsActive;
            }
        }
    }

    public async Task DeleteGarmentTypeAsync(Guid id, CancellationToken ct = default)
    {
        await new TransactionRunner(_db).RunAsync(async token =>
        {
            var t = await _db.Set<TailoringGarmentType>().FirstOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException(Messages.TailoringGarmentTypeNotFound);
            if (await InUseAsync(id, token)) throw new ConflictException(Messages.TailoringGarmentTypeInUse);
            var options = await _db.Set<TailoringDesignOption>().Where(o => o.GarmentTypeId == id).ToListAsync(token);
            var optionIds = options.Select(o => o.Id).ToList();
            _db.RemoveRange(await _db.Set<TailoringDesignOptionValue>().Where(v => optionIds.Contains(v.OptionId)).ToListAsync(token));
            _db.RemoveRange(options);
            _db.RemoveRange(await _db.Set<TailoringMeasurementField>().Where(f => f.GarmentTypeId == id).ToListAsync(token));
            _db.Remove(t);
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("TAILORING_GARMENT_TYPE_DELETED", nameof(TailoringGarmentType), id.ToString(), $"{t.Code} • {t.NameAr}", token);
            return true;
        }, ct);
    }

    public async Task<TailoringGarmentTypeDto> CompleteDefaultsAsync(Guid id, CancellationToken ct = default)
    {
        var added = await new TransactionRunner(_db).RunAsync(async token =>
        {
            var t = await _db.Set<TailoringGarmentType>().FirstOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException(Messages.TailoringGarmentTypeNotFound);
            if (t.Code != TailoringDefaults.ThobeCode) throw new ValidationFailedException(Messages.TailoringDefaultsNotApplicable);
            var n = await CompleteThobeAsync(t, token);
            await _audit.LogAsync("TAILORING_GARMENT_TYPE_DEFAULTS_COMPLETED", nameof(TailoringGarmentType), t.Id.ToString(), $"{t.Code} • +{n}", token);
            return n;
        }, ct);
        return await GarmentTypeAsync(id, ct);
    }

    private async Task<int> CompleteThobeAsync(TailoringGarmentType t, CancellationToken ct)
    {
        var fields = await _db.Set<TailoringMeasurementField>().Where(f => f.GarmentTypeId == t.Id).ToListAsync(ct);
        var options = await _db.Set<TailoringDesignOption>().Where(o => o.GarmentTypeId == t.Id).ToListAsync(ct);
        var n = TailoringDefaults.CompleteThobe(_db, t, fields, options);
        await _db.SaveChangesAsync(ct);
        return n;
    }

    private Task<bool> InUseAsync(Guid typeId, CancellationToken ct)
        => _db.Set<TailoringMeasurementProfile>().AnyAsync(p => p.GarmentTypeId == typeId, ct);

    private async Task<List<TailoringGarmentTypeDto>> ToDtosAsync(List<TailoringGarmentType> types, CancellationToken ct)
    {
        var ids = types.Select(t => t.Id).ToList();
        var fields = await _db.Set<TailoringMeasurementField>().AsNoTracking().Where(f => ids.Contains(f.GarmentTypeId)).ToListAsync(ct);
        var options = await _db.Set<TailoringDesignOption>().AsNoTracking().Where(o => ids.Contains(o.GarmentTypeId)).ToListAsync(ct);
        var optionIds = options.Select(o => o.Id).ToList();
        var values = await _db.Set<TailoringDesignOptionValue>().AsNoTracking().Where(v => optionIds.Contains(v.OptionId)).ToListAsync(ct);
        var productIds = types.Where(t => t.ProductId != null).Select(t => t.ProductId!.Value).ToList();
        var products = await _db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.NameAr, ct);
        var used = await _db.Set<TailoringMeasurementProfile>().AsNoTracking().Where(p => ids.Contains(p.GarmentTypeId))
            .Select(p => p.GarmentTypeId).Distinct().ToListAsync(ct);

        return types.Select(t => new TailoringGarmentTypeDto
        {
            Id = t.Id, Code = t.Code, NameAr = t.NameAr, NameEn = t.NameEn, Category = t.Category, ProductId = t.ProductId,
            ProductName = t.ProductId is Guid pid ? products.GetValueOrDefault(pid) : null, BasePrice = t.BasePrice,
            DefaultFabricMeters = t.DefaultFabricMeters, MeasurementUnit = t.MeasurementUnit, SortOrder = t.SortOrder, IsActive = t.IsActive,
            Notes = t.Notes, InUse = used.Contains(t.Id),
            Fields = fields.Where(f => f.GarmentTypeId == t.Id).OrderBy(f => f.SortOrder).ThenBy(f => f.NameAr).Select(f => new TailoringMeasurementFieldDto
            {
                Id = f.Id, Key = f.Key, NameAr = f.NameAr, NameEn = f.NameEn, GroupName = f.GroupName, SortOrder = f.SortOrder,
                IsRequired = f.IsRequired, MinValue = f.MinValue, MaxValue = f.MaxValue, IsActive = f.IsActive,
                InputType = f.InputType, Choices = TailoringMeasurementJson.ReadChoices(f.ChoicesJson),
            }).ToList(),
            Options = options.Where(o => o.GarmentTypeId == t.Id).OrderBy(o => o.SortOrder).ThenBy(o => o.NameAr).Select(o => new TailoringDesignOptionDto
            {
                Id = o.Id, Key = o.Key, NameAr = o.NameAr, NameEn = o.NameEn, InputType = o.InputType, IsRequired = o.IsRequired,
                SortOrder = o.SortOrder, IsActive = o.IsActive,
                Values = values.Where(v => v.OptionId == o.Id).OrderBy(v => v.SortOrder).ThenBy(v => v.NameAr).Select(v => new TailoringDesignOptionValueDto
                {
                    Id = v.Id, Code = v.Code, NameAr = v.NameAr, NameEn = v.NameEn, PriceDelta = v.PriceDelta, SortOrder = v.SortOrder,
                    IsDefault = v.IsDefault, IsActive = v.IsActive,
                }).ToList(),
            }).ToList(),
        }).ToList();
    }

    // ---------- مراحل الإنتاج ----------

    public async Task<List<TailoringStageDto>> StagesAsync(CancellationToken ct = default)
    {
        await EnsureSeededAsync(ct);
        return (await _db.Set<TailoringStage>().AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.NameAr).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<TailoringStageDto> CreateStageAsync(SaveTailoringStageDto request, CancellationToken ct = default)
    {
        var s = new TailoringStage();
        await ApplyAsync(s, request, ct);
        _db.Add(s);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("TAILORING_STAGE_CREATED", nameof(TailoringStage), s.Id.ToString(), $"{s.Code} • {s.NameAr}", ct);
        return ToDto(s);
    }

    public async Task<TailoringStageDto> UpdateStageAsync(Guid id, SaveTailoringStageDto request, CancellationToken ct = default)
    {
        var s = await _db.Set<TailoringStage>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(Messages.TailoringStageNotFound);
        await ApplyAsync(s, request, ct);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("TAILORING_STAGE_UPDATED", nameof(TailoringStage), s.Id.ToString(), $"{s.Code} • {s.NameAr}", ct);
        return ToDto(s);
    }

    public async Task DeleteStageAsync(Guid id, CancellationToken ct = default)
    {
        var s = await _db.Set<TailoringStage>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(Messages.TailoringStageNotFound);
        _db.Remove(s);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("TAILORING_STAGE_DELETED", nameof(TailoringStage), id.ToString(), $"{s.Code} • {s.NameAr}", ct);
    }

    private async Task ApplyAsync(TailoringStage s, SaveTailoringStageDto r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.NameAr)) throw new ValidationFailedException(Messages.TailoringNameRequired);
        if (r.DefaultPieceRate < 0) throw new ValidationFailedException(Messages.TailoringSettingsInvalid);
        var typeIds = r.GarmentTypeIds.Distinct().ToList();
        if (typeIds.Count > 0 && await _db.Set<TailoringGarmentType>().CountAsync(t => typeIds.Contains(t.Id), ct) != typeIds.Count)
            throw new ValidationFailedException(Messages.TailoringGarmentTypeNotFound);
        var code = string.IsNullOrWhiteSpace(r.Code) ? (s.Id == Guid.Empty ? await NextCodeAsync<TailoringStage>("ST-", ct) : s.Code) : r.Code.Trim().ToUpperInvariant();
        if (await _db.Set<TailoringStage>().AnyAsync(x => x.Code == code && x.Id != s.Id, ct)) throw new ConflictException(Messages.TailoringCodeDuplicate);
        s.Code = code; s.NameAr = r.NameAr.Trim(); s.NameEn = Clean(r.NameEn); s.SortOrder = r.SortOrder; s.IsMandatory = r.IsMandatory;
        s.IsQualityCheck = r.IsQualityCheck; s.DefaultPieceRate = decimal.Round(r.DefaultPieceRate, 2); s.IsActive = r.IsActive;
        s.GarmentTypeIds = typeIds.Count == 0 ? null : string.Join(',', typeIds);
    }

    private static TailoringStageDto ToDto(TailoringStage s) => new()
    {
        Id = s.Id, Code = s.Code, NameAr = s.NameAr, NameEn = s.NameEn, SortOrder = s.SortOrder, IsMandatory = s.IsMandatory,
        IsQualityCheck = s.IsQualityCheck, DefaultPieceRate = s.DefaultPieceRate, IsActive = s.IsActive,
        GarmentTypeIds = string.IsNullOrWhiteSpace(s.GarmentTypeIds) ? new() : s.GarmentTypeIds.Split(',').Select(Guid.Parse).ToList(),
    };

    // ---------- مساعدات ----------

    private async Task<string> NextCodeAsync<T>(string prefix, CancellationToken ct) where T : BaseEntity
    {
        var n = await _db.Set<T>().CountAsync(ct) + 1;
        while (true)
        {
            var code = $"{prefix}{n:000}";
            var exists = typeof(T) == typeof(TailoringStage)
                ? await _db.Set<TailoringStage>().AnyAsync(x => x.Code == code, ct)
                : await _db.Set<TailoringGarmentType>().AnyAsync(x => x.Code == code, ct);
            if (!exists) return code;
            n++;
        }
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    /// <summary>أول فتح للوحدة في المنشأة (لا سطر إعداد): يُنشأ الإعداد مع قالب الثوب السعودي ومراحل الإنتاج الافتراضية مرة واحدة.</summary>
    private async Task EnsureSeededAsync(CancellationToken ct)
    {
        var version = await _db.Set<TailoringSettings>().Select(s => (int?)s.DefaultsVersion).FirstOrDefaultAsync(ct);
        if (version >= TailoringDefaults.Version) return;
        if (version != null) { await UpgradeDefaultsAsync(ct); return; }
        try
        {
            await new TransactionRunner(_db).RunAsync(async token =>
            {
                _db.Add(new TailoringSettings { DefaultsVersion = TailoringDefaults.Version });
                if (!await _db.Set<TailoringGarmentType>().AnyAsync(token)) TailoringDefaults.SeedThobe(_db);
                if (!await _db.Set<TailoringStage>().AnyAsync(token)) TailoringDefaults.SeedStages(_db);
                await _db.SaveChangesAsync(token);
                return true;
            }, ct);
        }
        catch (DbUpdateException)
        {
            // فتح متزامن زرع الافتراضيات قبلنا: الفهارس الفريدة رفضت التكرار
            _db.ChangeTracker.Clear();
        }
    }

    /// <summary>منشأة قائمة بإصدار أقدم: يُستكمل قالب الثوب (إن وُجد) مرة واحدة ثم يُرفع الإصدار، فلا يعود ما حذفه المستخدم لاحقاً.</summary>
    private async Task UpgradeDefaultsAsync(CancellationToken ct)
    {
        try
        {
            await new TransactionRunner(_db).RunAsync(async token =>
            {
                var s = await _db.Set<TailoringSettings>().FirstAsync(token);
                if (s.DefaultsVersion >= TailoringDefaults.Version) return false;
                var thobe = await _db.Set<TailoringGarmentType>().FirstOrDefaultAsync(t => t.Code == TailoringDefaults.ThobeCode, token);
                if (thobe != null) await CompleteThobeAsync(thobe, token);
                s.DefaultsVersion = TailoringDefaults.Version;
                await _db.SaveChangesAsync(token);
                return true;
            }, ct);
        }
        catch (DbUpdateException)
        {
            // ترقية متزامنة سبقتنا: الفهرس الفريد للمفتاح رفض التكرار
            _db.ChangeTracker.Clear();
        }
    }
}
