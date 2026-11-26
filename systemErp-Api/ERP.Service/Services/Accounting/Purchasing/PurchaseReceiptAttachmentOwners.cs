using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// مستندات متابعة توريد المشتريات على شاشة <c>purchase-receipt-tracking</c>: تُضاف وتُحذف بصلاحية التعديل ما لم تُرفض المتابعة أو تُلغَ
/// (المغلقة تبقى بمرفقاتها دليلاً).
/// </summary>
public sealed class PurchaseSupplyTrackingAttachmentOwner : IAttachmentOwner
{
    private readonly ErpDbContext _db;
    private readonly IPermissionService _permissions;

    public PurchaseSupplyTrackingAttachmentOwner(ErpDbContext db, IPermissionService permissions) { _db = db; _permissions = permissions; }

    public string OwnerType => PurchaseReceiptAttachmentOwnerTypes.Tracking;
    public string Module => PlatformModules.Accounting;
    public string ScreenId => PermissionCatalog.PurchaseReceiptTrackingScreenId;
    public IReadOnlyCollection<string> Categories { get; } = new[]
    {
        DocumentAttachmentCategories.Document, DocumentAttachmentCategories.Photo, DocumentAttachmentCategories.Signature, DocumentAttachmentCategories.Other,
    };
    public int MaxPerOwner => 30;
    public long MaxBytes => DocumentAttachmentService.MaxUploadBytes;

    public async Task<AttachmentOwnerAccess> ResolveAsync(Guid ownerId, CancellationToken ct = default)
    {
        var t = await _db.Set<PurchaseSupplyTracking>().AsNoTracking().Where(x => x.Id == ownerId).Select(x => new { x.SourceNumber, x.Status }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.PurchaseSupplyTrackingNotFound);
        var open = !PurchaseSupplyStatuses.IsClosedManually(t.Status);
        var canEdit = open && await _permissions.HasPermissionAsync(ScreenId, ScreenAction.Edit, ct);
        return new AttachmentOwnerAccess(nameof(PurchaseSupplyTracking), t.SourceNumber, canEdit, canEdit);
    }
}

/// <summary>
/// صور ومستندات سند استلام المشتريات (المستلم والمرفوض) على شاشة متابعة التوريد: تُضاف بصلاحية الإضافة، وتُحذف بصلاحية التعديل
/// ما لم يُفوتر السند؛ حذف السند يحذف مرفقاته.
/// </summary>
public sealed class PurchaseReceiptAttachmentOwner : IAttachmentOwner
{
    private readonly ErpDbContext _db;
    private readonly IPermissionService _permissions;

    public PurchaseReceiptAttachmentOwner(ErpDbContext db, IPermissionService permissions) { _db = db; _permissions = permissions; }

    public string OwnerType => PurchaseReceiptAttachmentOwnerTypes.Receipt;
    public string Module => PlatformModules.Accounting;
    public string ScreenId => PermissionCatalog.PurchaseReceiptTrackingScreenId;
    public IReadOnlyCollection<string> Categories { get; } = new[]
    {
        DocumentAttachmentCategories.Photo, DocumentAttachmentCategories.Damage, DocumentAttachmentCategories.Inspection,
        DocumentAttachmentCategories.Document, DocumentAttachmentCategories.Signature, DocumentAttachmentCategories.Other,
    };
    public int MaxPerOwner => 40;
    public long MaxBytes => DocumentAttachmentService.MaxUploadBytes;

    public async Task<AttachmentOwnerAccess> ResolveAsync(Guid ownerId, CancellationToken ct = default)
    {
        var n = await _db.Set<DeliveryNote>().AsNoTracking().Where(x => x.Id == ownerId && x.Type == "purchase_delivery")
                .Select(x => new { x.DeliveryNumber, x.InvoiceId, x.Status }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.DeliveryNoteNotFound);
        var open = n.Status != DeliveryNoteStatus.Cancelled;
        return new AttachmentOwnerAccess(nameof(DeliveryNote), n.DeliveryNumber,
            open && await _permissions.HasPermissionAsync(ScreenId, ScreenAction.Create, ct),
            open && n.InvoiceId == null && await _permissions.HasPermissionAsync(ScreenId, ScreenAction.Edit, ct));
    }
}
