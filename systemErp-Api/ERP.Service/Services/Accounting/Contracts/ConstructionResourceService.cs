using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// موارد مكتبة المقاولات. القواعد: الرمز فريد في المنشأة، والوحدة معرّفة في وحدات القياس (تُحفظ برمزها)، ومصدر التكلفة:
/// <list type="bullet">
/// <item>manual: كما أُدخلت.</item>
/// <item>product (للمادة): من الصنف المخزني محوّلة لوحدة المورد، بشرط تحوّل الوحدة.</item>
/// <item>hr_job (للعمالة): من شؤون الموظفين، متوسط تكلفة ساعة المهنة × ساعات الوحدة، ووحدة المورد من نوع «وقت».
/// قراءة هذا المصدر تتطلب صلاحية شؤون الموظفين، فالتكلفة مشتقة من الرواتب.</item>
/// </list>
/// التكلفة غير اليدوية تُحسب هنا عند الحفظ وبأمر «تحديث التكاليف»، ولا تُقبل من الطلب.
/// </summary>
public class ConstructionResourceService
    : CrudService<ConstructionResource, ConstructionResourceDto, CreateConstructionResourceDto, UpdateConstructionResourceDto>, IConstructionResourceService
{
    private const int CostDecimals = 4;
    private const string HrScreen = "hr";
    private readonly IPermissionService _permissions;
    private readonly INumberSequenceService _numbers;

    public ConstructionResourceService(ErpDbContext db, IPermissionService permissions, INumberSequenceService numbers) : base(db)
    {
        _permissions = permissions; _numbers = numbers;
    }

    /// <summary>بادئة الرمز التلقائي لكل نوع (MAT-0001...).</summary>
    private static string CodePrefix(string type) => type switch
    {
        BoqResourceTypes.Material => "MAT-", BoqResourceTypes.Labor => "LAB-", BoqResourceTypes.Equipment => "EQP-",
        BoqResourceTypes.Subcontract => "SUB-", _ => "OTH-",
    };

    /// <summary>الرمز اختياري: فارغ في مورد جديد = رمز تلقائي بنوعه، وفارغ في تعديل = يبقى رمزه.</summary>
    private async Task<string> ResolveCodeAsync(CreateConstructionResourceDto d, ConstructionResource? existing, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(d.Code)) return d.Code;
        if (existing != null) return existing.Code;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var code = await _numbers.NextAsync("construction_resource_" + d.ResourceType, CodePrefix(d.ResourceType), ct);
            if (!await Db.Set<ConstructionResource>().AnyAsync(r => r.Code == code, ct)) return code;
        }
        throw new ConflictException(string.Format(Messages.SaveConstraintViolation, Label));
    }

    protected override string Label => Messages.LabelConstructionResource;

    protected override IQueryable<ConstructionResource> ApplySearch(IQueryable<ConstructionResource> q, string t)
        => q.Where(r => r.Code.Contains(t) || r.NameAr.Contains(t) || r.NameEn.Contains(t));

    /// <summary>فلتر الحالة يحمل نوع المورد (material، labor...).</summary>
    protected override IQueryable<ConstructionResource> ApplyFilters(IQueryable<ConstructionResource> q, PaginationParams p)
        => string.IsNullOrWhiteSpace(p.Status) ? q : q.Where(r => r.ResourceType == p.Status);

    protected override async Task ValidateAsync(CreateConstructionResourceDto d, ConstructionResource? existing, CancellationToken ct)
    {
        _hrEmployeeCount = 0;
        Normalize(d);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(d.NameAr)) errors.Add(Messages.ArabicNameRequired);
        if (!BoqResourceTypes.IsValid(d.ResourceType)) errors.Add(Messages.ResourceTypeInvalid);
        if (string.IsNullOrWhiteSpace(d.Unit)) errors.Add(Messages.ResourceUnitRequired);
        if (d.StandardCost < 0) errors.Add(Messages.ResourceCostNegative);
        if (!ResourceCostSources.IsValid(d.CostSource)) errors.Add(Messages.ResourceCostSourceInvalid);
        if (d.ProductId.HasValue && d.ResourceType != BoqResourceTypes.Material) errors.Add(Messages.ResourceProductMaterialOnly);
        if (d.CostSource == ResourceCostSources.Product && !d.ProductId.HasValue) errors.Add(Messages.ResourceProductCostNeedsProduct);
        if (d.CostSource == ResourceCostSources.HrJob)
        {
            if (d.ResourceType != BoqResourceTypes.Labor) errors.Add(Messages.ResourceHrLaborOnly);
            if (string.IsNullOrWhiteSpace(d.HrJobTitle)) errors.Add(Messages.ResourceHrJobRequired);
            if (d.HoursPerUnit <= 0) errors.Add(Messages.ResourceHoursPerUnitPositive);
        }
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        d.Code = await ResolveCodeAsync(d, existing, ct);
        if (await Db.Set<ConstructionResource>().AnyAsync(r => r.Code == d.Code && (existing == null || r.Id != existing.Id), ct))
            throw new ConflictException(string.Format(Messages.ResourceCodeExists, d.Code));

        var units = await BoqUnits.LoadAsync(Db, ct);
        var unit = units.Find(d.Unit) ?? throw new ValidationFailedException(string.Format(Messages.ResourceUnitUnknown, d.Unit));
        d.Unit = unit.Code;

        if (d.ProductId is Guid productId)
        {
            var product = (await BoqProducts.LoadAsync(Db, new Guid?[] { productId }, ct)).Find(productId)
                ?? throw new ValidationFailedException(Messages.ResourceProductNotFound);
            var perProductUnit = ConversionToProduct(units, d.Unit, product);
            if (perProductUnit == null && units.Find(product.Unit) != null)
                throw new ValidationFailedException(string.Format(Messages.ResourceUnitIncompatible, d.Unit, product.Unit));
            if (d.CostSource == ResourceCostSources.Product) d.StandardCost = ProductCost(product, perProductUnit);
        }

        if (d.CostSource == ResourceCostSources.Composite) await ValidateCompositeAsync(d, existing, ct);

        if (d.CostSource == ResourceCostSources.HrJob)
        {
            if (!string.Equals(unit.UnitType, UnitTypes.Time, StringComparison.OrdinalIgnoreCase))
                throw new ValidationFailedException(string.Format(Messages.ResourceHrUnitNotTime, d.Unit));
            await RequireHrAccessAsync(ct);
            if (d.HrDepartmentId is Guid dept && !await Db.Set<Department>().AnyAsync(x => x.Id == dept, ct))
                throw new ValidationFailedException(Messages.ResourceHrDepartmentNotFound);
            var rate = await HrLaborCost.ForJobAsync(Db, d.HrJobTitle!, d.HrDepartmentId, ct);
            if (rate.EmployeeCount == 0) throw new ValidationFailedException(string.Format(Messages.ResourceHrNoEmployees, d.HrJobTitle));
            d.StandardCost = Math.Round(rate.HourlyCost * d.HoursPerUnit, CostDecimals);
            _hrEmployeeCount = rate.EmployeeCount;
        }
    }

    /// <summary>عدد موظفي المهنة المحسوب في التحقق (يُكتب على الكيان بعد النسخ من الطلب).</summary>
    private int _hrEmployeeCount;

    protected override Task OnCreatingAsync(ConstructionResource e, CreateConstructionResourceDto d, CancellationToken ct)
    {
        e.HrEmployeeCount = e.CostSource == ResourceCostSources.HrJob ? _hrEmployeeCount : 0;
        e.CostUpdatedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    protected override Task OnUpdatingAsync(ConstructionResource e, UpdateConstructionResourceDto d, CancellationToken ct)
    {
        e.HrEmployeeCount = e.CostSource == ResourceCostSources.HrJob ? _hrEmployeeCount : 0;
        if (Db.Entry(e).Property(r => r.StandardCost).IsModified) e.CostUpdatedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    /// <summary>المورد وكل مركّب يعتمد عليه تُعاد تكلفتهم بعد الحفظ (في المعاملة نفسها).</summary>
    protected override bool Transactional => true;

    protected override async Task OnCreatedAsync(ConstructionResource e, CancellationToken ct)
    {
        if (await RecomputeCompositesAsync(ct) > 0) await SaveAsync(ct);
    }

    protected override async Task OnUpdatedAsync(ConstructionResource e, CancellationToken ct)
    {
        if (await RecomputeCompositesAsync(ct) > 0) await SaveAsync(ct);
    }

    /// <summary>المورد الداخل في مركّب لا يُحذف (يُوقف بدلاً من ذلك)، فلا تنكسر تكلفة المركّب.</summary>
    protected override async Task OnDeletingAsync(ConstructionResource e, CancellationToken ct)
    {
        var parents = await Db.Set<ConstructionResourceComponent>().AsNoTracking().Where(c => c.ComponentResourceId == e.Id)
            .Join(Db.Set<ConstructionResource>(), c => c.ResourceId, r => r.Id, (c, r) => r.Code + " " + r.NameAr)
            .Distinct().Take(5).ToListAsync(ct);
        if (parents.Count > 0) throw new ConflictException(string.Format(Messages.ResourceUsedInComposite, string.Join("، ", parents)));
        var items = await Db.Set<WorkItemComponent>().AsNoTracking().Where(c => c.ResourceId == e.Id)
            .Join(Db.Set<WorkItem>(), c => c.WorkItemId, w => w.Id, (c, w) => w.Code + " " + w.NameAr)
            .Distinct().Take(5).ToListAsync(ct);
        if (items.Count > 0) throw new ConflictException(string.Format(Messages.ResourceUsedInWorkItems, string.Join("، ", items)));
        // مادة عليها احتياج مشروع أو استهلاك موقع تبقى مرجعاً لهما
        if (await Db.Set<ProjectMaterialRequirement>().AnyAsync(r => r.ResourceId == e.Id, ct)
            || await Db.Set<SiteMaterialUsage>().AnyAsync(m => m.ResourceId == e.Id, ct))
            throw new ConflictException(Messages.ResourceUsedInProjects);
    }

    /// <summary>
    /// مكونات المركّب: غير فارغة، والكمية أكبر من صفر والهدر 0–100، ولا تكرار، والمكوّن موجود وليس المورد نفسه،
    /// ولا دورة (مكوّن يحوي المورد في شجرته).
    /// </summary>
    private async Task ValidateCompositeAsync(CreateConstructionResourceDto d, ConstructionResource? existing, CancellationToken ct)
    {
        // تكلفة المركّب لا تُقبل من الطلب: تبقى المحفوظة ثم تُعاد بعد الحفظ من مكوناته
        d.StandardCost = existing?.CostSource == ResourceCostSources.Composite ? existing.StandardCost : 0;
        var errors = new List<string>();
        if (d.Components.Count == 0) errors.Add(Messages.ResourceCompositeEmpty);
        if (d.Components.Any(c => c.Quantity <= 0 || c.WastePercent is < 0 or > 100)) errors.Add(Messages.ResourceComponentInvalid);
        if (d.Components.GroupBy(c => c.ComponentResourceId).Any(g => g.Count() > 1)) errors.Add(Messages.ResourceComponentDuplicate);
        if (existing != null && d.Components.Any(c => c.ComponentResourceId == existing.Id)) errors.Add(Messages.ResourceComponentSelf);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        var ids = d.Components.Select(c => c.ComponentResourceId).ToList();
        var found = await Db.Set<ConstructionResource>().AsNoTracking().CountAsync(r => ids.Contains(r.Id), ct);
        if (found != ids.Count) throw new ValidationFailedException(Messages.ResourceComponentNotFound);

        if (existing == null) return; // مورد جديد لا يحويه أحد بعد، فلا دورة
        var edges = (await Db.Set<ConstructionResourceComponent>().AsNoTracking().Where(c => c.ResourceId != existing.Id)
                .Select(c => new { c.ResourceId, c.ComponentResourceId }).ToListAsync(ct))
            .ToLookup(c => c.ResourceId, c => c.ComponentResourceId);
        var seen = new HashSet<Guid>();
        var stack = new Stack<Guid>(ids);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (id == existing.Id) throw new ValidationFailedException(Messages.ResourceComponentCycle);
            if (!seen.Add(id)) continue;
            foreach (var child in edges[id]) stack.Push(child);
        }
    }

    private Task<int> RecomputeCompositesAsync(CancellationToken ct) => ResourceCostRules.RecomputeCompositesAsync(Db, ct);

    public async Task<ResourceCostRefreshDto> RefreshCostsAsync(CancellationToken ct = default)
    {
        var resources = await Db.Set<ConstructionResource>()
            .Where(r => r.CostSource == ResourceCostSources.Product || r.CostSource == ResourceCostSources.HrJob).ToListAsync(ct);
        var result = new ResourceCostRefreshDto();
        if (resources.Count == 0) return result;

        var units = await BoqUnits.LoadAsync(Db, ct);
        var products = await BoqProducts.LoadAsync(Db, resources.Select(r => r.ProductId), ct);
        var hrAllowed = await _permissions.HasPermissionAsync(HrScreen, ScreenAction.View, ct);
        var now = DateTime.UtcNow;
        foreach (var r in resources)
        {
            decimal? cost = null;
            if (r.CostSource == ResourceCostSources.Product && r.ProductId is Guid pid && products.Find(pid) is { } product)
            {
                var factor = ConversionToProduct(units, r.Unit, product);
                if (product.Cost > 0 && (factor != null || units.Find(product.Unit) == null)) cost = ProductCost(product, factor);
            }
            else if (r.CostSource == ResourceCostSources.HrJob && hrAllowed && !string.IsNullOrWhiteSpace(r.HrJobTitle))
            {
                var rate = await HrLaborCost.ForJobAsync(Db, r.HrJobTitle, r.HrDepartmentId, ct);
                if (rate.EmployeeCount > 0)
                {
                    cost = Math.Round(rate.HourlyCost * r.HoursPerUnit, CostDecimals);
                    r.HrEmployeeCount = rate.EmployeeCount;
                }
            }
            if (cost is not decimal value)
            {
                result.Skipped.Add($"{r.Code} {r.NameAr}");
                continue;
            }
            if (value == r.StandardCost) continue;
            r.StandardCost = value;
            r.CostUpdatedAt = now;
            result.Updated++;
        }
        // المركّبات تتبع مكوناتها بعد تحديثها
        result.Updated += await RecomputeCompositesAsync(ct);
        await SaveAsync(ct);
        return result;
    }

    public async Task<HrLaborOptionsDto> HrLaborOptionsAsync(CancellationToken ct = default)
    {
        await RequireHrAccessAsync(ct);
        var active = Db.Set<Employee>().AsNoTracking().Where(e => e.Status == EmployeeStatuses.Active);
        var titles = await active.Where(e => e.JobTitle != null && e.JobTitle.Trim() != "")
            .GroupBy(e => e.JobTitle!.Trim())
            .Select(g => new HrJobTitleOptionDto { JobTitle = g.Key, EmployeeCount = g.Count() })
            .OrderBy(t => t.JobTitle).ToListAsync(ct);
        var counts = await active.Where(e => e.DepartmentId != null).GroupBy(e => e.DepartmentId!.Value)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var departments = await Db.Set<Department>().AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.NameAr)
            .Select(x => new HrDepartmentOptionDto { Id = x.Id, NameAr = x.NameAr, NameEn = x.NameEn }).ToListAsync(ct);
        foreach (var dep in departments) dep.EmployeeCount = counts.GetValueOrDefault(dep.Id);
        return new HrLaborOptionsDto { JobTitles = titles, Departments = departments };
    }

    // ---------- مساعدات ----------

    private static void Normalize(CreateConstructionResourceDto d)
    {
        d.Code = d.Code?.Trim() ?? string.Empty;
        d.NameAr = d.NameAr?.Trim() ?? string.Empty;
        d.NameEn = d.NameEn?.Trim() ?? string.Empty;
        d.ResourceType = d.ResourceType?.Trim().ToLowerInvariant() ?? string.Empty;
        d.CostSource = string.IsNullOrWhiteSpace(d.CostSource) ? ResourceCostSources.Manual : d.CostSource.Trim().ToLowerInvariant();
        d.Notes = string.IsNullOrWhiteSpace(d.Notes) ? null : d.Notes.Trim();
        // حقول مصدر لا يخص المورد لا تُحفظ
        if (d.ResourceType != BoqResourceTypes.Material) d.ProductId = null;
        if (d.CostSource != ResourceCostSources.HrJob)
        {
            d.HrJobTitle = null; d.HrDepartmentId = null; d.HoursPerUnit = 1m;
        }
        else d.HrJobTitle = d.HrJobTitle?.Trim();
        d.Components ??= new();
        if (d.CostSource != ResourceCostSources.Composite) d.Components.Clear();
        else
        {
            for (var i = 0; i < d.Components.Count; i++) d.Components[i].SortOrder = i + 1;
        }
    }

    private async Task RequireHrAccessAsync(CancellationToken ct)
    {
        if (!await _permissions.HasPermissionAsync(HrScreen, ScreenAction.View, ct)) throw new ForbiddenException(Messages.ResourceHrForbidden);
    }

    private static decimal ProductCost(BoqProducts.Info product, decimal? perProductUnit)
        => perProductUnit is decimal f ? Math.Round(product.Cost * f, CostDecimals) : product.Cost;

    /// <summary>كم من وحدة الصنف في وحدة واحدة من المورد؛ null إن تعذّر التحويل أو لم تُعرّف وحدة الصنف.</summary>
    private static decimal? ConversionToProduct(BoqUnits units, string resourceUnit, BoqProducts.Info product)
        => string.IsNullOrWhiteSpace(product.Unit) ? null : units.Convert(1m, resourceUnit, product.Unit);
}
