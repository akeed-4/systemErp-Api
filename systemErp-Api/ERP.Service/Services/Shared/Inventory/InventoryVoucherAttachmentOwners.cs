using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// مرفقات فواتير الصرف والاستلام المخزني على شاشة كل نوع: تُرفق بأي حالة غير ملغاة (بعد الترحيل أيضاً، لا تمس المستند)،
/// والإضافة بصلاحية التعديل والحذف بصلاحية الحذف. النوع جزء من التحقق: معرّف مستند من النوع الآخر غير موجود هنا.
/// </summary>
public abstract class InventoryVoucherAttachmentOwnerBase : IAttachmentOwner
{
    private readonly ErpDbContext _db;
    private readonly IPermissionService _permissions;
    private readonly string _kind;

    protected InventoryVoucherAttachmentOwnerBase(ErpDbContext db, IPermissionService permissions, string kind)
    {
        _db = db; _permissions = permissions; _kind = kind;
    }

    public string OwnerType => InventoryVoucherKinds.SourceOf(_kind);
    public string Module => PlatformModules.Accounting;
    public string ScreenId => InventoryVoucherKinds.ScreenOf(_kind);
    public IReadOnlyCollection<string> Categories { get; } = new[]
    {
        DocumentAttachmentCategories.Document, DocumentAttachmentCategories.Photo, DocumentAttachmentCategories.Signature, DocumentAttachmentCategories.Other,
    };
    public int MaxPerOwner => 30;
    public long MaxBytes => DocumentAttachmentService.MaxUploadBytes;

    public async Task<AttachmentOwnerAccess> ResolveAsync(Guid ownerId, CancellationToken ct = default)
    {
        var v = await _db.Set<InventoryVoucher>().AsNoTracking().Where(x => x.Id == ownerId && x.Kind == _kind)
                    .Select(x => new { x.VoucherNumber, x.Status }).FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException(Messages.InventoryVoucherNotFound);
        return new AttachmentOwnerAccess(nameof(InventoryVoucher), v.VoucherNumber,
            v.Status != InventoryVoucherStatuses.Cancelled && await _permissions.HasPermissionAsync(ScreenId, ScreenAction.Edit, ct),
            await _permissions.HasPermissionAsync(ScreenId, ScreenAction.Delete, ct));
    }
}

public sealed class InventoryIssueAttachmentOwner : InventoryVoucherAttachmentOwnerBase
{
    public InventoryIssueAttachmentOwner(ErpDbContext db, IPermissionService permissions) : base(db, permissions, InventoryVoucherKinds.Issue) { }
}

public sealed class InventoryReceiptAttachmentOwner : InventoryVoucherAttachmentOwnerBase
{
    public InventoryReceiptAttachmentOwner(ErpDbContext db, IPermissionService permissions) : base(db, permissions, InventoryVoucherKinds.Receipt) { }
}
