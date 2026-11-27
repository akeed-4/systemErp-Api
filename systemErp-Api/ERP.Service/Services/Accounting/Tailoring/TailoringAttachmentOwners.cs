using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>صور المرجع والتصميم ومستندات أمر التفصيل (شاشة الأوامر): تُضاف ما لم يُغلق الأمر أو يُلغَ، وتُحذف قبل التسليم.</summary>
public sealed class TailoringOrderAttachmentOwner : IAttachmentOwner
{
    private readonly ErpDbContext _db;
    private readonly IPermissionService _permissions;

    public TailoringOrderAttachmentOwner(ErpDbContext db, IPermissionService permissions) { _db = db; _permissions = permissions; }

    public string OwnerType => TailoringAttachmentOwnerTypes.Order;
    public string Module => PlatformModules.Tailoring;
    public string ScreenId => PermissionCatalog.TailoringOrdersScreenId;
    public IReadOnlyCollection<string> Categories { get; } = DocumentAttachmentCategories.All;
    public int MaxPerOwner => 60;
    public long MaxBytes => DocumentAttachmentService.MaxUploadBytes;

    public async Task<AttachmentOwnerAccess> ResolveAsync(Guid ownerId, CancellationToken ct = default)
    {
        var o = await _db.Set<TailoringOrder>().AsNoTracking().Where(x => x.Id == ownerId).Select(x => new { x.OrderNumber, x.Status }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.TailoringOrderNotFound);
        var open = o.Status is not (TailoringOrderStatuses.Closed or TailoringOrderStatuses.Cancelled);
        var early = o.Status is TailoringOrderStatuses.Draft or TailoringOrderStatuses.Confirmed;
        return new AttachmentOwnerAccess(nameof(TailoringOrder), o.OrderNumber,
            open && await _permissions.HasPermissionAsync(ScreenId, ScreenAction.Edit, ct),
            early && await _permissions.HasPermissionAsync(ScreenId, ScreenAction.Delete, ct));
    }
}
