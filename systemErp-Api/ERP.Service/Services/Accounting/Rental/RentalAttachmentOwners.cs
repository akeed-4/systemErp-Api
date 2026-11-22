using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// صور محضر التسليم أو الاستلام (حالة المعدة والعدّاد والوقود والضرر) وتوقيع ممثل العميل، على شاشة المحاضر. تُضاف وتُحذف ما دام المحضر
/// غير ملغى (الملغى يبقى بمرفقاته دليلاً).
/// </summary>
public sealed class RentalHandoverAttachmentOwner : IAttachmentOwner
{
    private readonly ErpDbContext _db;
    private readonly IPermissionService _permissions;

    public RentalHandoverAttachmentOwner(ErpDbContext db, IPermissionService permissions) { _db = db; _permissions = permissions; }

    public string OwnerType => RentalAttachmentOwnerTypes.Handover;
    public string Module => PlatformModules.EquipmentRental;
    public string ScreenId => PermissionCatalog.RentalHandoversScreenId;
    public IReadOnlyCollection<string> Categories { get; } = new[]
    {
        DocumentAttachmentCategories.Photo, DocumentAttachmentCategories.Damage, DocumentAttachmentCategories.Inspection,
        DocumentAttachmentCategories.Signature, DocumentAttachmentCategories.Document, DocumentAttachmentCategories.Other,
    };
    public int MaxPerOwner => 60;
    public long MaxBytes => DocumentAttachmentService.MaxUploadBytes;

    public async Task<AttachmentOwnerAccess> ResolveAsync(Guid ownerId, CancellationToken ct = default)
    {
        var h = await _db.Set<RentalHandover>().AsNoTracking().Where(x => x.Id == ownerId).Select(x => new { x.HandoverNumber, x.Status }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.RentalHandoverNotFound);
        var open = h.Status == RentalHandoverStatuses.Posted;
        return new AttachmentOwnerAccess(nameof(RentalHandover), h.HandoverNumber,
            open && await _permissions.HasPermissionAsync(ScreenId, ScreenAction.Create, ct),
            open && await _permissions.HasPermissionAsync(ScreenId, ScreenAction.Delete, ct));
    }
}

/// <summary>
/// مستندات عقد التأجير (العقد الموقّع، هوية المفوَّض، خطاب الضمان...) على شاشة العقود. تُضاف للمسودة والساري، وتُحذف بصلاحية الحذف ما لم
/// يُغلق العقد أو يُلغَ؛ حذف المسودة يحذف مرفقاتها.
/// </summary>
public sealed class RentalContractAttachmentOwner : IAttachmentOwner
{
    private readonly ErpDbContext _db;
    private readonly IPermissionService _permissions;

    public RentalContractAttachmentOwner(ErpDbContext db, IPermissionService permissions) { _db = db; _permissions = permissions; }

    public string OwnerType => RentalAttachmentOwnerTypes.Contract;
    public string Module => PlatformModules.EquipmentRental;
    public string ScreenId => PermissionCatalog.RentalContractsScreenId;
    public IReadOnlyCollection<string> Categories { get; } = new[]
    {
        DocumentAttachmentCategories.Document, DocumentAttachmentCategories.Signature, DocumentAttachmentCategories.Photo, DocumentAttachmentCategories.Other,
    };
    public int MaxPerOwner => 30;
    public long MaxBytes => DocumentAttachmentService.MaxUploadBytes;

    public async Task<AttachmentOwnerAccess> ResolveAsync(Guid ownerId, CancellationToken ct = default)
    {
        var c = await _db.Set<RentalContract>().AsNoTracking().Where(x => x.Id == ownerId).Select(x => new { x.ContractNumber, x.Status }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.RentalContractNotFound);
        var open = RentalContractStatuses.Blocking.Contains(c.Status);
        return new AttachmentOwnerAccess(nameof(RentalContract), c.ContractNumber,
            open && await _permissions.HasPermissionAsync(ScreenId, ScreenAction.Edit, ct),
            open && await _permissions.HasPermissionAsync(ScreenId, ScreenAction.Delete, ct));
    }
}

/// <summary>
/// صورة كشف الساعات الموقّع من العميل (Q2: شرط الاعتماد بالإعداد) وتوقيعه، على شاشة كشوف الساعات. تُضاف وتُحذف قبل الاعتماد فقط:
/// المعتمد والمفوتر يبقى بمرفقاته دليلاً للفاتورة.
/// </summary>
public sealed class RentalTimesheetAttachmentOwner : IAttachmentOwner
{
    private readonly ErpDbContext _db;
    private readonly IPermissionService _permissions;

    public RentalTimesheetAttachmentOwner(ErpDbContext db, IPermissionService permissions) { _db = db; _permissions = permissions; }

    public string OwnerType => RentalAttachmentOwnerTypes.Timesheet;
    public string Module => PlatformModules.EquipmentRental;
    public string ScreenId => PermissionCatalog.RentalTimesheetsScreenId;
    public IReadOnlyCollection<string> Categories { get; } = new[]
    {
        DocumentAttachmentCategories.Document, DocumentAttachmentCategories.Signature, DocumentAttachmentCategories.Photo, DocumentAttachmentCategories.Other,
    };
    public int MaxPerOwner => 20;
    public long MaxBytes => DocumentAttachmentService.MaxUploadBytes;

    public async Task<AttachmentOwnerAccess> ResolveAsync(Guid ownerId, CancellationToken ct = default)
    {
        var t = await _db.Set<RentalTimesheet>().AsNoTracking().Where(x => x.Id == ownerId).Select(x => new { x.TimesheetNumber, x.Status }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.RentalTimesheetNotFound);
        var open = t.Status is RentalTimesheetStatuses.Draft or RentalTimesheetStatuses.Submitted or RentalTimesheetStatuses.Rejected;
        return new AttachmentOwnerAccess(nameof(RentalTimesheet), t.TimesheetNumber,
            open && await _permissions.HasPermissionAsync(ScreenId, ScreenAction.Edit, ct),
            open && await _permissions.HasPermissionAsync(ScreenId, ScreenAction.Delete, ct));
    }
}
