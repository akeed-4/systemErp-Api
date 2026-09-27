using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Core.Models.Shared;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>سجل التدقيق العام. القراءة عبر ListAsync (يُصفَّى بـ EntityName عبر Status وEntityId عبر SearchTerm)،
/// والتسجيل حصراً عبر LogAsync من داخل الخدمات الأخرى — لا Endpoint عام للإنشاء.</summary>
public class AuditService : CrudService<AuditLog, AuditLogDto, CreateAuditLogDto, UpdateAuditLogDto>, IAuditService
{
    private readonly ICurrentUser _user;
    public AuditService(ErpDbContext db, ICurrentUser user) : base(db) => _user = user;
    protected override string Label => "سجل التدقيق";

    protected override IQueryable<AuditLog> ApplyFilters(IQueryable<AuditLog> q, PaginationParams p)
        => string.IsNullOrWhiteSpace(p.Status) ? q : q.Where(a => a.EntityName == p.Status);

    protected override IQueryable<AuditLog> ApplySearch(IQueryable<AuditLog> q, string term)
        => q.Where(a => a.EntityId == term || a.Action.Contains(term) || a.Details.Contains(term));

    public async Task LogAsync(string action, string entityName, string entityId, string details, CancellationToken ct = default)
    {
        await CreateAsync(new CreateAuditLogDto
        {
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            PerformedBy = _user.Name ?? "system",
            Details = details,
            Timestamp = DateTime.UtcNow,
        }, ct);
    }
}
