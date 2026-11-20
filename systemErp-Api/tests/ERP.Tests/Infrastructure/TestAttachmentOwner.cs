using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Core.Models.Shared;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using Microsoft.EntityFrameworkCore;

namespace ERP.Tests.Infrastructure;

/// <summary>
/// معالج مالك للمرفقات في الاختبارات وحدها (لا مالك حقيقي قبل مستندات الورش والتأجير): المالك عميل في المنشأة، بوحدة الورش وشاشة
/// أوامر العمل. الإضافة بصلاحية الإضافة، والحذف بصلاحية الحذف، والعميل غير النشط يقفل المرفقات (مستند في حالة لا تقبل التعديل).
/// </summary>
public sealed class TestAttachmentOwner : IAttachmentOwner
{
    public const string Type = "test_customer";
    public const int Max = 3;

    private readonly ErpDbContext _db;
    private readonly IPermissionService _permissions;

    public TestAttachmentOwner(ErpDbContext db, IPermissionService permissions) { _db = db; _permissions = permissions; }

    public string OwnerType => Type;
    public string Module => PlatformModules.Workshop;
    public string ScreenId => PermissionCatalog.WorkshopOrdersScreenId;
    public IReadOnlyCollection<string> Categories { get; } = new[] { DocumentAttachmentCategories.Photo, DocumentAttachmentCategories.Damage, DocumentAttachmentCategories.Other };
    public int MaxPerOwner => Max;
    public long MaxBytes => 64 * 1024;

    public async Task<AttachmentOwnerAccess> ResolveAsync(Guid ownerId, CancellationToken ct = default)
    {
        var customer = await _db.Set<Customer>().AsNoTracking().Where(c => c.Id == ownerId).Select(c => new { c.Code, c.Status }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("العميل غير موجود.");
        var open = customer.Status == "active";
        return new AttachmentOwnerAccess(nameof(Customer), customer.Code,
            open && await _permissions.HasPermissionAsync(ScreenId, ScreenAction.Create, ct),
            open && await _permissions.HasPermissionAsync(ScreenId, ScreenAction.Delete, ct));
    }
}
