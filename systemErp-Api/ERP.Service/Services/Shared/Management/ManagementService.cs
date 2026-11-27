using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Core.Models.Shared;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// الإدارة. سجل التدقيق: قراءة فقط لمنشأة المستخدم (مرشّح المنشأة)، بالوقت السعودي (UTC + 3) حتى تصفّي الشبكة بالتاريخ المحلي،
/// والأحدث أولاً. الكتابة تبقى حصراً عبر <see cref="IAuditService.LogAsync"/>.
/// </summary>
public class ManagementService : IManagementService
{
    private readonly ErpDbContext _db;
    public ManagementService(ErpDbContext db) => _db = db;

    public Task<DevExtreme.AspNet.Data.ResponseModel.LoadResult> AuditLoadAsync(DataSourceLoadOptions options, CancellationToken ct = default)
    {
        var rows = _db.Set<AuditLog>().AsNoTracking().Select(a => new AuditLogRowDto
        {
            Id = a.Id, Timestamp = a.Timestamp.AddHours(3), Action = a.Action, EntityName = a.EntityName, EntityId = a.EntityId, PerformedBy = a.PerformedBy,
            UserId = a.UserId, Details = a.Details, ClientIpAddress = a.ClientIpAddress,
        });
        return EntityLoader.LoadRowsAsync(rows, options, nameof(AuditLogRowDto.Id), ct, EntityLoader.Desc(nameof(AuditLogRowDto.Timestamp)));
    }
}
