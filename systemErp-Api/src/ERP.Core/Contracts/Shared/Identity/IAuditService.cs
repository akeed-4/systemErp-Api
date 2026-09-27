using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>سجل تدقيق عام لأي كيان (EntityName/EntityId) — قراءة عبر ListAsync الموروثة، والتسجيل حصراً عبر LogAsync من داخل الخدمات (لا يوجد Endpoint عام للإنشاء).</summary>
public interface IAuditService : ICrudService<AuditLogDto, CreateAuditLogDto, UpdateAuditLogDto>
{
    Task LogAsync(string action, string entityName, string entityId, string details, CancellationToken ct = default);
}
