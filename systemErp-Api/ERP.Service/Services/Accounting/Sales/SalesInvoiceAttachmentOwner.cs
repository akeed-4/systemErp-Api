using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// مستندات أرشفة فاتورة البيع على شاشة المبيعات: تُرفق بأي حالة غير ملغاة ولو بعد الترحيل والإرسال للهيئة (لا تمس الفاتورة الصادرة)،
/// وتُحذف بصلاحية الحذف.
/// </summary>
public sealed class SalesInvoiceAttachmentOwner : IAttachmentOwner
{
    public const string Type = "sales_invoice";
    private readonly ErpDbContext _db;
    private readonly IPermissionService _permissions;

    public SalesInvoiceAttachmentOwner(ErpDbContext db, IPermissionService permissions) { _db = db; _permissions = permissions; }

    public string OwnerType => Type;
    public string Module => PlatformModules.Accounting;
    public string ScreenId => "sales";
    public IReadOnlyCollection<string> Categories { get; } = new[] { DocumentAttachmentCategories.Document, DocumentAttachmentCategories.Photo, DocumentAttachmentCategories.Signature, DocumentAttachmentCategories.Other };
    public int MaxPerOwner => 30;
    public long MaxBytes => DocumentAttachmentService.MaxUploadBytes;

    public async Task<AttachmentOwnerAccess> ResolveAsync(Guid ownerId, CancellationToken ct = default)
    {
        var i = await _db.Set<SalesInvoice>().AsNoTracking().Where(x => x.Id == ownerId).Select(x => new { x.InvoiceNumber, x.Status }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.InvoiceNotFound);
        return new AttachmentOwnerAccess(nameof(SalesInvoice), i.InvoiceNumber,
            i.Status != "cancelled" && await _permissions.HasPermissionAsync(ScreenId, ScreenAction.Edit, ct),
            await _permissions.HasPermissionAsync(ScreenId, ScreenAction.Delete, ct));
    }
}
