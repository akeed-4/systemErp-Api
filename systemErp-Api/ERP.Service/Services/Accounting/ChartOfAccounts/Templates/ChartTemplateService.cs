using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// إدارة قوالب الدليل والأنشطة لمدير المنصة، على القاعدة المشتركة وحدها (<see cref="ErpDbContext.Catalog"/>) أياً كانت قاعدة منشأة المدير.
/// تعديل قالب لا يمسّ منشأة قائمة: يسري على التأسيس التالي وعلى ما يطبّقه المستخدم بعد مراجعته.
/// </summary>
public class ChartTemplateService : IChartTemplateService
{
    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;

    public ChartTemplateService(ErpDbContext db, ICurrentUser user, IAuditService audit)
    {
        _db = db; _user = user; _audit = audit;
    }

    private ErpDbContext Catalog => _db.Catalog;

    // ---------- الأنشطة ----------

    public async Task<List<BusinessActivityDto>> ListActivitiesAsync(bool activeOnly, CancellationToken ct = default)
    {
        await ChartTemplateCatalog.EnsureSeededAsync(Catalog, ct);
        return await Catalog.Set<BusinessActivity>().AsNoTracking()
            .Where(a => !activeOnly || a.IsActive)
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Key)
            .Select(a => new BusinessActivityDto
            {
                Key = a.Key, NameAr = a.NameAr, NameEn = a.NameEn, DescriptionAr = a.DescriptionAr, DescriptionEn = a.DescriptionEn,
                ParentKey = a.ParentKey, SortOrder = a.SortOrder, IsActive = a.IsActive,
            }).ToListAsync(ct);
    }

    public async Task<BusinessActivityDto> SaveActivityAsync(BusinessActivityDto dto, CancellationToken ct = default)
    {
        var key = (dto.Key ?? string.Empty).Trim().ToLowerInvariant();
        if (key.Length is < 2 or > 64 || !key.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_'))
            throw new ValidationFailedException(Messages.BusinessActivityKeyInvalid);
        if (string.IsNullOrWhiteSpace(dto.NameAr)) throw new ValidationFailedException(Messages.BusinessActivityNameRequired);
        await ChartTemplateCatalog.EnsureSeededAsync(Catalog, ct);
        var parent = string.IsNullOrWhiteSpace(dto.ParentKey) ? null : dto.ParentKey.Trim().ToLowerInvariant();
        if (parent != null && (parent == key || !await Catalog.Set<BusinessActivity>().AnyAsync(a => a.Key == parent && a.ParentKey == null, ct)))
            throw new ValidationFailedException(Messages.BusinessActivityParentInvalid);

        var activity = await Catalog.Set<BusinessActivity>().FirstOrDefaultAsync(a => a.Key == key, ct);
        if (activity == null) { activity = new BusinessActivity { Key = key }; Catalog.Add(activity); }
        activity.NameAr = dto.NameAr.Trim();
        activity.NameEn = string.IsNullOrWhiteSpace(dto.NameEn) ? dto.NameAr.Trim() : dto.NameEn.Trim();
        activity.DescriptionAr = dto.DescriptionAr?.Trim();
        activity.DescriptionEn = dto.DescriptionEn?.Trim();
        activity.ParentKey = parent;
        activity.SortOrder = dto.SortOrder;
        activity.IsActive = dto.IsActive;
        await Catalog.SaveChangesAsync(ct);
        await _audit.LogAsync("BUSINESS_ACTIVITY_SAVED", nameof(BusinessActivity), key, $"{activity.NameAr} ({(activity.IsActive ? "فعّال" : "موقوف")})", ct);
        return (await ListActivitiesAsync(false, ct)).First(a => a.Key == key);
    }

    // ---------- القوالب ----------

    public async Task<List<ChartTemplateSummaryDto>> ListTemplatesAsync(CancellationToken ct = default)
    {
        await ChartTemplateCatalog.EnsureSeededAsync(Catalog, ct);
        return await Catalog.Set<ChartTemplate>().AsNoTracking()
            .OrderBy(t => t.Kind).ThenBy(t => t.SortOrder).ThenBy(t => t.Key)
            .Select(t => new ChartTemplateSummaryDto
            {
                Id = t.Id, Key = t.Key, NameAr = t.NameAr, NameEn = t.NameEn, Kind = t.Kind, ActivityKey = t.ActivityKey, ModuleKey = t.ModuleKey,
                Version = t.Version, IsActive = t.IsActive, SortOrder = t.SortOrder, AccountCount = t.Accounts.Count, UpdatedAt = t.UpdatedAt, UpdatedBy = t.UpdatedBy,
            }).ToListAsync(ct);
    }

    public async Task<ChartTemplateDto> GetTemplateAsync(Guid id, CancellationToken ct = default)
    {
        var t = await Catalog.Set<ChartTemplate>().AsNoTracking().Include(x => x.Accounts).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(Messages.ChartTemplateNotFound);
        return ToDto(t);
    }

    public async Task<ChartTemplateDto> CreateTemplateAsync(SaveChartTemplateDto dto, CancellationToken ct = default)
    {
        await ChartTemplateCatalog.EnsureSeededAsync(Catalog, ct);
        var template = new ChartTemplate { Id = Guid.NewGuid() };
        await ApplyAsync(template, dto, isNew: true, ct);
        Catalog.Add(template);
        await Catalog.SaveChangesAsync(ct);
        await _audit.LogAsync("CHART_TEMPLATE_CREATED", nameof(ChartTemplate), template.Key, $"{template.NameAr}: {template.Accounts.Count} حساب", ct);
        return await GetTemplateAsync(template.Id, ct);
    }

    public async Task<ChartTemplateDto> UpdateTemplateAsync(Guid id, SaveChartTemplateDto dto, CancellationToken ct = default)
    {
        var template = await Catalog.Set<ChartTemplate>().Include(t => t.Accounts).FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(Messages.ChartTemplateNotFound);
        Catalog.RemoveRange(template.Accounts);
        template.Accounts.Clear();
        await ApplyAsync(template, dto, isNew: false, ct);
        template.Version++;
        await Catalog.SaveChangesAsync(ct);
        await _audit.LogAsync("CHART_TEMPLATE_UPDATED", nameof(ChartTemplate), template.Key, $"{template.NameAr} — الإصدار {template.Version}: {template.Accounts.Count} حساب", ct);
        return await GetTemplateAsync(id, ct);
    }

    public async Task DeleteTemplateAsync(Guid id, CancellationToken ct = default)
    {
        var template = await Catalog.Set<ChartTemplate>().Include(t => t.Accounts).FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException(Messages.ChartTemplateNotFound);
        if (template.Kind == ChartTemplateKind.Base) throw new ConflictException(Messages.ChartTemplateBaseCannotBeDeleted);
        Catalog.Remove(template); // حساباته تُحذف معه (Cascade)؛ دلائل المنشآت لا تُمس
        await Catalog.SaveChangesAsync(ct);
        await _audit.LogAsync("CHART_TEMPLATE_DELETED", nameof(ChartTemplate), template.Key, template.NameAr, ct);
    }

    private async Task ApplyAsync(ChartTemplate template, SaveChartTemplateDto dto, bool isNew, CancellationToken ct)
    {
        var key = (dto.Key ?? string.Empty).Trim().ToLowerInvariant();
        if (key.Length is < 2 or > 64 || !key.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '.'))
            throw new ValidationFailedException(Messages.ChartTemplateKeyInvalid);
        if (string.IsNullOrWhiteSpace(dto.NameAr)) throw new ValidationFailedException(Messages.ChartTemplateNameRequired);
        if (!Enum.IsDefined(dto.Kind)) throw new ValidationFailedException(Messages.ChartTemplateKindInvalid);
        if (!isNew && template.Kind == ChartTemplateKind.Base && dto.Kind != ChartTemplateKind.Base)
            throw new ValidationFailedException(Messages.ChartTemplateKindInvalid);
        if (await Catalog.Set<ChartTemplate>().AnyAsync(t => t.Key == key && t.Id != template.Id, ct))
            throw new ConflictException(Messages.ChartTemplateKeyTaken);
        if (dto.Kind == ChartTemplateKind.Base && await Catalog.Set<ChartTemplate>().AnyAsync(t => t.Kind == ChartTemplateKind.Base && t.Id != template.Id, ct))
            throw new ConflictException(Messages.ChartTemplateSingleBase);

        string? activity = null, module = null;
        if (dto.Kind == ChartTemplateKind.Activity)
        {
            activity = dto.ActivityKey?.Trim().ToLowerInvariant();
            if (activity == null || !await Catalog.Set<BusinessActivity>().AnyAsync(a => a.Key == activity, ct))
                throw new ValidationFailedException(Messages.BusinessActivityInvalid);
        }
        if (dto.Kind == ChartTemplateKind.Module)
        {
            module = dto.ModuleKey?.Trim().ToLowerInvariant();
            if (module == null || !PlatformModules.Licensable.Contains(module)) throw new ValidationFailedException(Messages.ChartTemplateModuleInvalid);
        }

        template.Key = key;
        template.NameAr = dto.NameAr.Trim();
        template.NameEn = string.IsNullOrWhiteSpace(dto.NameEn) ? dto.NameAr.Trim() : dto.NameEn.Trim();
        template.Kind = dto.Kind;
        template.ActivityKey = activity;
        template.ModuleKey = module;
        template.IsActive = dto.IsActive;
        template.SortOrder = dto.SortOrder;
        template.UpdatedAt = DateTime.UtcNow;
        template.UpdatedBy = _user.Email ?? _user.Name;
        var order = 0;
        foreach (var a in dto.Accounts)
            template.Accounts.Add(new ChartTemplateAccount
            {
                Id = Guid.NewGuid(), TemplateId = template.Id,
                Code = string.IsNullOrWhiteSpace(a.Code) ? null : a.Code.Trim(),
                NameAr = (a.NameAr ?? string.Empty).Trim(), NameEn = (a.NameEn ?? string.Empty).Trim(),
                ParentCode = string.IsNullOrWhiteSpace(a.ParentCode) ? null : a.ParentCode.Trim(),
                Type = a.Type, IsGroup = a.IsGroup,
                SemanticKey = string.IsNullOrWhiteSpace(a.SemanticKey) ? null : AccountSemanticKeys.Normalize(a.SemanticKey),
                IsRequired = a.IsRequired, IsActive = a.IsActive, SortOrder = a.SortOrder > 0 ? a.SortOrder : ++order,
            });

        // التحقق بالدمج نفسه: القالب فوق القالب الأساسي (وقالب نشاطه الأب) على شجرة فارغة
        var context = new List<ChartTemplate>();
        if (template.Kind != ChartTemplateKind.Base)
        {
            var all = await ChartTemplateCatalog.LoadAsync(Catalog, ct);
            context.AddRange(all.Where(t => t.Kind == ChartTemplateKind.Base && t.Id != template.Id));
            if (activity != null && (await ChartTemplateCatalog.ActivityAsync(Catalog, activity, ct))?.ParentKey is { } parentActivity)
                context.AddRange(all.Where(t => t.Kind == ChartTemplateKind.Activity && t.ActivityKey == parentActivity && t.Id != template.Id));
        }
        context.Add(template);
        var errors = ChartComposer.Compose(context, Array.Empty<ChartComposer.ExistingAccount>(), _ => null).Errors;
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
    }

    // ---------- المعاينة ----------

    public async Task<ChartCompositionDto> PreviewAsync(ChartCompositionRequestDto request, CancellationToken ct = default)
    {
        var all = await ChartTemplateCatalog.LoadAsync(Catalog, ct);
        var activity = string.IsNullOrWhiteSpace(request.ActivityKey) ? null : request.ActivityKey.Trim().ToLowerInvariant();
        var templates = await ChartTemplateCatalog.SelectAsync(Catalog, all, includeBase: true, activity, request.Modules ?? Array.Empty<string>(), ct);
        var result = ChartComposer.Compose(templates, Array.Empty<ChartComposer.ExistingAccount>(), _ => null);
        return new ChartCompositionDto
        {
            Templates = result.Templates.ToList(),
            Errors = result.Errors.ToList(),
            Accounts = result.Rows.Where(r => r.Code != null).Select(r => new ComposedAccountDto
            {
                Code = r.Code!, NameAr = r.NameAr, NameEn = r.NameEn, ParentCode = r.ParentCode, Type = r.Type, IsGroup = r.IsGroup,
                Level = r.Level, SemanticKey = r.SemanticKey, IsRequired = r.IsRequired, Sources = r.Sources.ToList(), CodeGenerated = r.CodeGenerated,
            }).ToList(),
        };
    }

    public List<AccountSemanticKeyDto> SemanticKeys() => AccountSemanticKeys.All.Select(d => new AccountSemanticKeyDto
    {
        Key = d.Key, Label = d.Label, DefaultCode = d.DefaultCode, Store = d.Store,
        Types = d.Role?.Types ?? (ReservedAccountCodes.Find(d.DefaultCode) is { } r ? new[] { r.Type } : Array.Empty<AccountCategory>()),
    }).ToList();

    private static ChartTemplateDto ToDto(ChartTemplate t) => new()
    {
        Id = t.Id, Key = t.Key, NameAr = t.NameAr, NameEn = t.NameEn, Kind = t.Kind, ActivityKey = t.ActivityKey, ModuleKey = t.ModuleKey,
        Version = t.Version, IsActive = t.IsActive, SortOrder = t.SortOrder, AccountCount = t.Accounts.Count, UpdatedAt = t.UpdatedAt, UpdatedBy = t.UpdatedBy,
        Accounts = t.Accounts.OrderBy(a => a.SortOrder).ThenBy(a => a.Code, StringComparer.Ordinal).Select(a => new ChartTemplateAccountDto
        {
            Id = a.Id, Code = a.Code, NameAr = a.NameAr, NameEn = a.NameEn, ParentCode = a.ParentCode, Type = a.Type, IsGroup = a.IsGroup,
            SemanticKey = a.SemanticKey, IsRequired = a.IsRequired, IsActive = a.IsActive, SortOrder = a.SortOrder,
            IsReserved = ReservedAccountCodes.IsReserved(a.Code),
        }).ToList(),
    };
}
