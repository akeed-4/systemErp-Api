using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>الإدارة: سجل التدقيق العام لمنشأة المستخدم (قراءة فقط) بشبكة السرفر وتصفيتها.</summary>
public interface IManagementService
{
    Task<DevExtreme.AspNet.Data.ResponseModel.LoadResult> AuditLoadAsync(DataSourceLoadOptions options, CancellationToken ct = default);
}
