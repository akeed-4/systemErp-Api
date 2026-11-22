using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// صور أمر العمل ومستنداته (الاستقبال، الفحص، موافقة العميل، التسليم والتوقيع) على شاشة أوامر العمل. تُضاف ما لم يُغلق الأمر أو يُلغَ،
/// وتُحذف قبل الفوترة؛ حذف الأمر «المستلم» يحذف مرفقاته.
/// </summary>
public sealed class WorkOrderAttachmentOwner : IAttachmentOwner
{
    private readonly ErpDbContext _db;
    private readonly IPermissionService _permissions;

    public WorkOrderAttachmentOwner(ErpDbContext db, IPermissionService permissions) { _db = db; _permissions = permissions; }

    public string OwnerType => WorkshopAttachmentOwnerTypes.WorkOrder;
    public string Module => PlatformModules.Workshop;
    public string ScreenId => PermissionCatalog.WorkshopOrdersScreenId;
    public IReadOnlyCollection<string> Categories { get; } = DocumentAttachmentCategories.All;
    public int MaxPerOwner => 100;
    public long MaxBytes => DocumentAttachmentService.MaxUploadBytes;

    public async Task<AttachmentOwnerAccess> ResolveAsync(Guid ownerId, CancellationToken ct = default)
    {
        var o = await _db.Set<WorkOrder>().AsNoTracking().Where(x => x.Id == ownerId).Select(x => new { x.OrderNumber, x.Status }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.WorkOrderNotFound);
        var open = o.Status is not (WorkOrderStatuses.Closed or WorkOrderStatuses.Cancelled);
        return new AttachmentOwnerAccess(nameof(WorkOrder), o.OrderNumber,
            open && await _permissions.HasPermissionAsync(ScreenId, ScreenAction.Edit, ct),
            WorkOrderStatuses.Editable.Contains(o.Status) && await _permissions.HasPermissionAsync(ScreenId, ScreenAction.Delete, ct));
    }
}

/// <summary>صور ضرر قائم عند الاستقبال (مالكها علامة الضرر)، تُضاف وتُحذف ما دام أمر العمل قبل الفوترة.</summary>
public sealed class WorkOrderDamageAttachmentOwner : IAttachmentOwner
{
    private readonly ErpDbContext _db;
    private readonly IPermissionService _permissions;

    public WorkOrderDamageAttachmentOwner(ErpDbContext db, IPermissionService permissions) { _db = db; _permissions = permissions; }

    public string OwnerType => WorkshopAttachmentOwnerTypes.Damage;
    public string Module => PlatformModules.Workshop;
    public string ScreenId => PermissionCatalog.WorkshopOrdersScreenId;
    public IReadOnlyCollection<string> Categories { get; } = new[] { DocumentAttachmentCategories.Damage, DocumentAttachmentCategories.Photo, DocumentAttachmentCategories.Other };
    public int MaxPerOwner => 12;
    public long MaxBytes => DocumentAttachmentService.MaxUploadBytes;

    public async Task<AttachmentOwnerAccess> ResolveAsync(Guid ownerId, CancellationToken ct = default)
    {
        var d = await (from m in _db.Set<WorkOrderDamageMark>().AsNoTracking()
                       join o in _db.Set<WorkOrder>().AsNoTracking() on m.WorkOrderId equals o.Id
                       where m.Id == ownerId
                       select new { o.OrderNumber, o.Status, m.Area }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.WorkOrderDamageNotFound);
        var editable = WorkOrderStatuses.Editable.Contains(d.Status);
        return new AttachmentOwnerAccess(nameof(WorkOrderDamageMark), $"{d.OrderNumber} • {d.Area}",
            editable && await _permissions.HasPermissionAsync(ScreenId, ScreenAction.Edit, ct),
            editable && await _permissions.HasPermissionAsync(ScreenId, ScreenAction.Delete, ct));
    }
}

/// <summary>صور نتيجة بند فحص (إلزامية للفاشل إن اشترطها القالب)، تُضاف وتُحذف ما دام الفحص مسودة والأمر قبل الفوترة.</summary>
public sealed class WorkOrderInspectionResultAttachmentOwner : IAttachmentOwner
{
    private readonly ErpDbContext _db;
    private readonly IPermissionService _permissions;

    public WorkOrderInspectionResultAttachmentOwner(ErpDbContext db, IPermissionService permissions) { _db = db; _permissions = permissions; }

    public string OwnerType => WorkshopAttachmentOwnerTypes.InspectionResult;
    public string Module => PlatformModules.Workshop;
    public string ScreenId => PermissionCatalog.WorkshopOrdersScreenId;
    public IReadOnlyCollection<string> Categories { get; } = new[] { DocumentAttachmentCategories.Inspection, DocumentAttachmentCategories.Photo, DocumentAttachmentCategories.Other };
    public int MaxPerOwner => 12;
    public long MaxBytes => DocumentAttachmentService.MaxUploadBytes;

    public async Task<AttachmentOwnerAccess> ResolveAsync(Guid ownerId, CancellationToken ct = default)
    {
        var r = await (from res in _db.Set<WorkOrderInspectionResult>().AsNoTracking()
                       join i in _db.Set<WorkOrderInspection>().AsNoTracking() on res.InspectionId equals i.Id
                       join o in _db.Set<WorkOrder>().AsNoTracking() on i.WorkOrderId equals o.Id
                       where res.Id == ownerId
                       select new { o.OrderNumber, OrderStatus = o.Status, i.Status, res.TextAr }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.InspectionResultNotFound);
        var open = r.Status == InspectionStatuses.Draft && WorkOrderStatuses.Editable.Contains(r.OrderStatus);
        return new AttachmentOwnerAccess(nameof(WorkOrderInspectionResult), $"{r.OrderNumber} • {r.TextAr}",
            open && await _permissions.HasPermissionAsync(ScreenId, ScreenAction.Edit, ct),
            open && await _permissions.HasPermissionAsync(ScreenId, ScreenAction.Delete, ct));
    }
}
