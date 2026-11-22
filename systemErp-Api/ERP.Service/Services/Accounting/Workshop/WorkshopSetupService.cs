using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// إعداد الورشة. القواعد:
/// <list type="bullet">
/// <item>الإعداد سطر واحد للمنشأة يُنشأ عند أول حفظ (القراءة قبله تعيد الافتراضي)؛ المستودع الافتراضي موجود، والمبالغ والمدد غير سالبة.</item>
/// <item>المرحلة الفرعية داخل حالة ثابتة غير نهائية؛ لا تُحذف وعليها أمر عمل أو سجل (تُعطَّل بدلاً من ذلك). سبب التوقف كذلك.</item>
/// <item>الخدمة فوق صنف خدمي قائم (الإيراد والضريبة منه)، برمز فريد، وساعات معيارية غير سالبة؛ الثابتة بسعر موجب. الضمان افتراضه من الإعداد.</item>
/// <item>قالب الفحص باستخدامه وبنود نصية بنوع نتيجتها؛ الحفظ يستبدل البنود.</item>
/// </list>
/// </summary>
public class WorkshopSetupService : IWorkshopSetupService
{
    private const int MaxTemplateItems = 300;
    private static readonly string[] StageStatuses =
    {
        WorkOrderStatuses.Received, WorkOrderStatuses.Inspection, WorkOrderStatuses.AwaitingApproval, WorkOrderStatuses.Approved,
        WorkOrderStatuses.InProgress, WorkOrderStatuses.Paused, WorkOrderStatuses.Quality, WorkOrderStatuses.Ready,
    };

    private readonly ErpDbContext _db;
    private readonly IAuditService _audit;

    public WorkshopSetupService(ErpDbContext db, IAuditService audit) { _db = db; _audit = audit; }

    // ---------- الإعداد العام ----------

    public async Task<WorkshopSettingsDto> SettingsAsync(CancellationToken ct = default)
        => await ToDtoAsync(await _db.Set<WorkshopSettings>().AsNoTracking().FirstOrDefaultAsync(ct) ?? new WorkshopSettings(), ct);

    public async Task<WorkshopSettingsDto> SaveSettingsAsync(SaveWorkshopSettingsDto r, CancellationToken ct = default)
    {
        if (r.DefaultLaborRate < 0 || r.ReservationHoldDays < 0 || r.DefaultWarrantyMonths < 0 || r.DefaultWarrantyKm < 0)
            throw new ValidationFailedException(Messages.WorkshopSettingsNegative);
        if (r.DefaultWarehouseId is Guid wh && !await _db.Set<Warehouse>().AnyAsync(w => w.Id == wh, ct))
            throw new ValidationFailedException(Messages.WorkshopWarehouseNotFound);
        var s = await _db.Set<WorkshopSettings>().FirstOrDefaultAsync(ct);
        if (s == null) { s = new WorkshopSettings(); _db.Add(s); }
        s.DefaultLaborRate = decimal.Round(r.DefaultLaborRate, 4); s.ReservationHoldDays = r.ReservationHoldDays;
        s.DefaultWarrantyMonths = r.DefaultWarrantyMonths; s.DefaultWarrantyKm = r.DefaultWarrantyKm; s.DefaultWarehouseId = r.DefaultWarehouseId;
        s.RequireReceptionInspection = r.RequireReceptionInspection;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WORKSHOP_SETTINGS_SAVED", nameof(WorkshopSettings), s.Id.ToString(), $"{s.DefaultLaborRate:0.##}", ct);
        return await ToDtoAsync(s, ct);
    }

    private async Task<WorkshopSettingsDto> ToDtoAsync(WorkshopSettings s, CancellationToken ct) => new()
    {
        DefaultLaborRate = s.DefaultLaborRate, ReservationHoldDays = s.ReservationHoldDays, DefaultWarrantyMonths = s.DefaultWarrantyMonths,
        DefaultWarrantyKm = s.DefaultWarrantyKm, DefaultWarehouseId = s.DefaultWarehouseId, RequireReceptionInspection = s.RequireReceptionInspection,
        DefaultWarehouseName = s.DefaultWarehouseId is Guid wh
            ? await _db.Set<Warehouse>().AsNoTracking().Where(w => w.Id == wh).Select(w => w.NameAr).FirstOrDefaultAsync(ct) : null,
    };

    // ---------- المراحل ----------

    public async Task<List<WorkshopStageDto>> StagesAsync(CancellationToken ct = default)
        => (await _db.Set<WorkshopStage>().AsNoTracking().ToListAsync(ct))
            .OrderBy(s => Array.IndexOf(StageStatuses, s.Status)).ThenBy(s => s.SortOrder).ThenBy(s => s.NameAr).Select(ToDto).ToList();

    public async Task<WorkshopStageDto> CreateStageAsync(SaveWorkshopStageDto request, CancellationToken ct = default)
    {
        var s = new WorkshopStage();
        Apply(s, request);
        _db.Add(s);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WORKSHOP_STAGE_CREATED", nameof(WorkshopStage), s.Id.ToString(), $"{s.Status} • {s.NameAr}", ct);
        return ToDto(s);
    }

    public async Task<WorkshopStageDto> UpdateStageAsync(Guid id, SaveWorkshopStageDto request, CancellationToken ct = default)
    {
        var s = await _db.Set<WorkshopStage>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(Messages.WorkshopStageNotFound);
        var status = s.Status;
        Apply(s, request);
        if (s.Status != status && await StageUsedAsync(id, ct)) throw new ConflictException(Messages.WorkshopStageInUse);
        await _db.SaveChangesAsync(ct);
        return ToDto(s);
    }

    public async Task DeleteStageAsync(Guid id, CancellationToken ct = default)
    {
        var s = await _db.Set<WorkshopStage>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(Messages.WorkshopStageNotFound);
        if (await StageUsedAsync(id, ct)) throw new ConflictException(Messages.WorkshopStageInUse);
        _db.Remove(s);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WORKSHOP_STAGE_DELETED", nameof(WorkshopStage), id.ToString(), s.NameAr, ct);
    }

    private async Task<bool> StageUsedAsync(Guid id, CancellationToken ct)
        => await _db.Set<WorkOrder>().AnyAsync(o => o.StageId == id, ct) || await _db.Set<WorkOrderStatusHistory>().AnyAsync(h => h.StageId == id, ct);

    private static void Apply(WorkshopStage s, SaveWorkshopStageDto r)
    {
        var status = r.Status?.Trim().ToLowerInvariant();
        if (status == null || !StageStatuses.Contains(status)) throw new ValidationFailedException(Messages.WorkshopStageStatusInvalid);
        var name = Trim(r.NameAr, 150) ?? throw new ValidationFailedException(Messages.WorkshopNameRequired);
        s.Status = status; s.NameAr = name; s.NameEn = Trim(r.NameEn, 150); s.SortOrder = r.SortOrder; s.IsActive = r.IsActive;
    }

    private static WorkshopStageDto ToDto(WorkshopStage s) => new()
    {
        Id = s.Id, Status = s.Status, NameAr = s.NameAr, NameEn = s.NameEn, SortOrder = s.SortOrder, IsActive = s.IsActive,
    };

    // ---------- أسباب التوقف ----------

    public async Task<List<WorkshopPauseReasonDto>> PauseReasonsAsync(CancellationToken ct = default)
        => (await _db.Set<WorkshopPauseReason>().AsNoTracking().OrderBy(r => r.SortOrder).ThenBy(r => r.NameAr).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<WorkshopPauseReasonDto> CreatePauseReasonAsync(SaveWorkshopPauseReasonDto request, CancellationToken ct = default)
    {
        var p = new WorkshopPauseReason();
        Apply(p, request);
        _db.Add(p);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WORKSHOP_PAUSE_REASON_CREATED", nameof(WorkshopPauseReason), p.Id.ToString(), p.NameAr, ct);
        return ToDto(p);
    }

    public async Task<WorkshopPauseReasonDto> UpdatePauseReasonAsync(Guid id, SaveWorkshopPauseReasonDto request, CancellationToken ct = default)
    {
        var p = await _db.Set<WorkshopPauseReason>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(Messages.WorkshopPauseReasonNotFound);
        Apply(p, request);
        await _db.SaveChangesAsync(ct);
        return ToDto(p);
    }

    public async Task DeletePauseReasonAsync(Guid id, CancellationToken ct = default)
    {
        var p = await _db.Set<WorkshopPauseReason>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(Messages.WorkshopPauseReasonNotFound);
        if (await _db.Set<WorkOrder>().AnyAsync(o => o.PauseReasonId == id, ct) || await _db.Set<WorkOrderStatusHistory>().AnyAsync(h => h.PauseReasonId == id, ct))
            throw new ConflictException(Messages.WorkshopPauseReasonInUse);
        _db.Remove(p);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WORKSHOP_PAUSE_REASON_DELETED", nameof(WorkshopPauseReason), id.ToString(), p.NameAr, ct);
    }

    private static void Apply(WorkshopPauseReason p, SaveWorkshopPauseReasonDto r)
    {
        p.NameAr = Trim(r.NameAr, 150) ?? throw new ValidationFailedException(Messages.WorkshopNameRequired);
        p.NameEn = Trim(r.NameEn, 150); p.SortOrder = r.SortOrder; p.IsActive = r.IsActive;
    }

    private static WorkshopPauseReasonDto ToDto(WorkshopPauseReason p) => new()
    {
        Id = p.Id, NameAr = p.NameAr, NameEn = p.NameEn, SortOrder = p.SortOrder, IsActive = p.IsActive,
    };

    // ---------- الخدمات ----------

    public async Task<List<WorkshopServiceDto>> ServicesAsync(bool activeOnly, CancellationToken ct = default)
    {
        var q = _db.Set<WorkshopServiceDefinition>().AsNoTracking();
        if (activeOnly) q = q.Where(s => s.IsActive);
        return await DtosAsync(await q.OrderBy(s => s.Code).ToListAsync(ct), ct);
    }

    public async Task<WorkshopServiceDto> CreateServiceAsync(SaveWorkshopServiceDto request, CancellationToken ct = default)
    {
        var s = new WorkshopServiceDefinition();
        await ApplyAsync(s, request, ct);
        _db.Add(s);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WORKSHOP_SERVICE_CREATED", nameof(WorkshopServiceDefinition), s.Id.ToString(), $"{s.Code} • {s.NameAr}", ct);
        return (await DtosAsync(new List<WorkshopServiceDefinition> { s }, ct)).Single();
    }

    public async Task<WorkshopServiceDto> UpdateServiceAsync(Guid id, SaveWorkshopServiceDto request, CancellationToken ct = default)
    {
        var s = await _db.Set<WorkshopServiceDefinition>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(Messages.WorkshopServiceNotFound);
        await ApplyAsync(s, request, ct);
        await _db.SaveChangesAsync(ct);
        return (await DtosAsync(new List<WorkshopServiceDefinition> { s }, ct)).Single();
    }

    public async Task DeleteServiceAsync(Guid id, CancellationToken ct = default)
    {
        var s = await _db.Set<WorkshopServiceDefinition>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(Messages.WorkshopServiceNotFound);
        if (await _db.Set<WorkOrderLine>().AnyAsync(l => l.ServiceId == id, ct)) throw new ConflictException(Messages.WorkshopServiceInUse);
        _db.Remove(s);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WORKSHOP_SERVICE_DELETED", nameof(WorkshopServiceDefinition), id.ToString(), $"{s.Code} • {s.NameAr}", ct);
    }

    private async Task ApplyAsync(WorkshopServiceDefinition s, SaveWorkshopServiceDto r, CancellationToken ct)
    {
        var product = await _db.Set<Product>().AsNoTracking().Where(p => p.Id == r.ProductId).Select(p => new { p.ItemType, p.NameAr, p.NameEn, p.Sku })
            .FirstOrDefaultAsync(ct) ?? throw new ValidationFailedException(Messages.WorkshopServiceProductInvalid);
        if (product.ItemType != ProductItemTypes.Service) throw new ValidationFailedException(Messages.WorkshopServiceProductInvalid);
        var method = r.PricingMethod?.Trim().ToLowerInvariant();
        if (!WorkshopPricingMethods.IsValid(method)) throw new ValidationFailedException(Messages.WorkshopPricingMethodInvalid);
        if (r.StandardHours < 0 || r.Price is < 0 || r.WarrantyMonths is < 0 || r.WarrantyKm is < 0) throw new ValidationFailedException(Messages.WorkshopSettingsNegative);
        if (method == WorkshopPricingMethods.Fixed && r.Price is not > 0) throw new ValidationFailedException(Messages.WorkshopFixedPriceRequired);
        if (method == WorkshopPricingMethods.Hourly && r.StandardHours <= 0) throw new ValidationFailedException(Messages.WorkshopStandardHoursRequired);
        var code = Trim(r.Code, 32)?.ToUpperInvariant();
        if (code != null && await _db.Set<WorkshopServiceDefinition>().AnyAsync(x => x.Code == code && x.Id != s.Id, ct))
            throw new ValidationFailedException(string.Format(Messages.WorkshopServiceCodeTaken, code));
        var settings = await _db.Set<WorkshopSettings>().AsNoTracking().FirstOrDefaultAsync(ct);

        s.Code = code ?? (string.IsNullOrEmpty(s.Code) ? await NextServiceCodeAsync(ct) : s.Code);
        s.ProductId = r.ProductId;
        s.NameAr = Trim(r.NameAr, 200) ?? product.NameAr; s.NameEn = Trim(r.NameEn, 200) ?? (string.IsNullOrWhiteSpace(product.NameEn) ? null : product.NameEn);
        s.StandardHours = decimal.Round(r.StandardHours, 2); s.PricingMethod = method!;
        s.Price = r.Price is decimal p ? decimal.Round(p, 4) : null;
        s.WarrantyMonths = r.WarrantyMonths ?? settings?.DefaultWarrantyMonths ?? 0; s.WarrantyKm = r.WarrantyKm ?? settings?.DefaultWarrantyKm ?? 0;
        s.Description = Trim(r.Description, 1000); s.IsActive = r.IsActive;
    }

    private async Task<string> NextServiceCodeAsync(CancellationToken ct)
    {
        var n = await _db.Set<WorkshopServiceDefinition>().CountAsync(ct);
        while (true)
        {
            var code = $"SRV-{++n:000}";
            if (!await _db.Set<WorkshopServiceDefinition>().AnyAsync(x => x.Code == code, ct)) return code;
        }
    }

    private async Task<List<WorkshopServiceDto>> DtosAsync(List<WorkshopServiceDefinition> list, CancellationToken ct)
    {
        var ids = list.Select(s => s.ProductId).Distinct().ToList();
        var products = await _db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => new { p.Sku, p.NameAr }, ct);
        var labor = (await _db.Set<WorkshopSettings>().AsNoTracking().FirstOrDefaultAsync(ct))?.DefaultLaborRate ?? 0;
        return list.Select(s => new WorkshopServiceDto
        {
            Id = s.Id, Code = s.Code, ProductId = s.ProductId, ProductSku = products.GetValueOrDefault(s.ProductId)?.Sku,
            ProductName = products.GetValueOrDefault(s.ProductId)?.NameAr, NameAr = s.NameAr, NameEn = s.NameEn, StandardHours = s.StandardHours,
            PricingMethod = s.PricingMethod, Price = s.Price, EffectivePrice = WorkshopPricing.ServicePrice(s, labor),
            WarrantyMonths = s.WarrantyMonths, WarrantyKm = s.WarrantyKm, Description = s.Description, IsActive = s.IsActive,
        }).ToList();
    }

    // ---------- قوالب الفحص ----------

    public async Task<List<InspectionTemplateDto>> TemplatesAsync(string? usage, CancellationToken ct = default)
    {
        var q = _db.Set<InspectionTemplate>().AsNoTracking().Include(t => t.Items).AsQueryable();
        if (!string.IsNullOrWhiteSpace(usage)) { var u = usage.Trim().ToLowerInvariant(); q = q.Where(t => t.Usage == u); }
        return (await q.OrderBy(t => t.Usage).ThenBy(t => t.NameAr).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<InspectionTemplateDto> GetTemplateAsync(Guid id, CancellationToken ct = default)
        => ToDto(await _db.Set<InspectionTemplate>().AsNoTracking().Include(t => t.Items).FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(Messages.InspectionTemplateNotFound));

    public async Task<InspectionTemplateDto> CreateTemplateAsync(SaveInspectionTemplateDto request, CancellationToken ct = default)
    {
        var t = new InspectionTemplate();
        Apply(t, request);
        _db.Add(t);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("INSPECTION_TEMPLATE_CREATED", nameof(InspectionTemplate), t.Id.ToString(), $"{t.Usage} • {t.NameAr}", ct);
        return ToDto(t);
    }

    public async Task<InspectionTemplateDto> UpdateTemplateAsync(Guid id, SaveInspectionTemplateDto request, CancellationToken ct = default)
    {
        var t = await _db.Set<InspectionTemplate>().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(Messages.InspectionTemplateNotFound);
        Apply(t, request);
        await _db.SaveChangesAsync(ct);
        return ToDto(t);
    }

    public async Task DeleteTemplateAsync(Guid id, CancellationToken ct = default)
    {
        var t = await _db.Set<InspectionTemplate>().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(Messages.InspectionTemplateNotFound);
        if (await _db.Set<WorkOrderInspection>().AnyAsync(i => i.TemplateId == id, ct)) throw new ConflictException(Messages.InspectionTemplateInUse);
        _db.RemoveRange(t.Items);
        _db.Remove(t);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("INSPECTION_TEMPLATE_DELETED", nameof(InspectionTemplate), id.ToString(), t.NameAr, ct);
    }

    private void Apply(InspectionTemplate t, SaveInspectionTemplateDto r)
    {
        var usage = r.Usage?.Trim().ToLowerInvariant();
        if (!InspectionUsages.IsValid(usage)) throw new ValidationFailedException(Messages.InspectionUsageInvalid);
        if (r.Items.Count == 0) throw new ValidationFailedException(Messages.InspectionItemsRequired);
        if (r.Items.Count > MaxTemplateItems) throw new ValidationFailedException(string.Format(Messages.InspectionTooManyItems, MaxTemplateItems));
        var items = new List<InspectionTemplateItem>();
        var order = 0;
        foreach (var i in r.Items.OrderBy(i => i.SortOrder))
        {
            var type = string.IsNullOrWhiteSpace(i.ResultType) ? InspectionResultTypes.Condition : i.ResultType.Trim().ToLowerInvariant();
            if (!InspectionResultTypes.IsValid(type)) throw new ValidationFailedException(Messages.InspectionResultTypeInvalid);
            items.Add(new InspectionTemplateItem
            {
                Section = Trim(i.Section, 100), TextAr = Trim(i.TextAr, 300) ?? throw new ValidationFailedException(Messages.InspectionItemTextRequired),
                TextEn = Trim(i.TextEn, 300), ResultType = type, PhotoRequiredOnFail = type == InspectionResultTypes.Condition && i.PhotoRequiredOnFail,
                SortOrder = ++order,
            });
        }
        t.NameAr = Trim(r.NameAr, 200) ?? throw new ValidationFailedException(Messages.WorkshopNameRequired);
        t.NameEn = Trim(r.NameEn, 200); t.Usage = usage!; t.IsActive = r.IsActive;
        // نتائج الفحص (W2) تحفظ نص البند لقطة، فاستبدال البنود لا يمس الفحوص المسجلة
        if (t.Items.Count > 0) _db.RemoveRange(t.Items.ToList());
        t.Items.Clear();
        foreach (var item in items) t.Items.Add(item);
    }

    private static InspectionTemplateDto ToDto(InspectionTemplate t) => new()
    {
        Id = t.Id, NameAr = t.NameAr, NameEn = t.NameEn, Usage = t.Usage, IsActive = t.IsActive,
        Items = t.Items.OrderBy(i => i.SortOrder).Select(i => new InspectionTemplateItemDto
        {
            Id = i.Id, Section = i.Section, TextAr = i.TextAr, TextEn = i.TextEn, ResultType = i.ResultType,
            PhotoRequiredOnFail = i.PhotoRequiredOnFail, SortOrder = i.SortOrder,
        }).ToList(),
    };

    private static string? Trim(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length > max ? v[..max] : v;
    }
}

/// <summary>تسعير الورشة (منطق صرف): الخدمة الثابتة بسعرها، وبالساعة = الساعات المعيارية × سعر ساعتها الخاص أو سعر الإعداد.</summary>
internal static class WorkshopPricing
{
    public static decimal ServicePrice(WorkshopServiceDefinition s, decimal defaultLaborRate) => s.PricingMethod == WorkshopPricingMethods.Fixed
        ? s.Price ?? 0
        : decimal.Round(s.StandardHours * (s.Price ?? defaultLaborRate), 2);
}
