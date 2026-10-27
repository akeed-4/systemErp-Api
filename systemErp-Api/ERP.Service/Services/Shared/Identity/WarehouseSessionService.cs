using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Service.Data;
using Microsoft.AspNetCore.Http;

namespace ERP.Service.Services.Shared;

/// <summary>
/// موقع الجلسة (المعرض/المستودع): المالك وكل من لا قائمة مواقع له يعمل في كل المواقع، وغيره في قائمته فقط.
/// الموقع المختار عند الدخول يصل في ترويسة X-Warehouse-Id؛ يُقبل إن كان مسموحاً، ومن له موقع واحد يُضبط عليه تلقائياً.
/// </summary>
public class WarehouseSessionService : IWarehouseSessionService
{
    public const string Header = "X-Warehouse-Id";
    public static readonly string[] Visibilities = { "site", "user" };

    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IHttpContextAccessor _http;

    public WarehouseSessionService(ErpDbContext db, ICurrentUser user, IHttpContextAccessor http)
    {
        _db = db; _user = user; _http = http;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_user.UserId is not { } userId) return;
        var user = await _db.Set<User>().AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new { u.Role, u.AllowedWarehouseIds }).FirstOrDefaultAsync(ct);
        if (user == null) return;
        var visibility = await _db.Set<Tenant>().AsNoTracking().Select(t => t.DocumentVisibility).FirstOrDefaultAsync(ct) ?? "site";

        var allowed = user.Role == UserRole.Owner ? null : ParseIds(user.AllowedWarehouseIds);
        Guid? warehouse = null;
        if (Guid.TryParse(_http.HttpContext?.Request.Headers[Header].ToString(), out var requested)
            && (allowed == null || allowed.Contains(requested))
            && await _db.Set<Warehouse>().AsNoTracking().AnyAsync(w => w.Id == requested, ct))
            warehouse = requested;
        if (warehouse == null && allowed is { Count: 1 }) warehouse = allowed[0];

        _db.SetWarehouseSession(warehouse, allowed, Visibilities.Contains(visibility) ? visibility : "site", PermissionStore.IsFullAccess(_user.RoleId), userId);
    }

    public async Task<WarehouseSessionDto> GetAsync(CancellationToken ct = default)
    {
        var allowed = _db.SessionAllowedWarehouseIds;
        var current = _db.SessionWarehouseId;
        var warehouses = await _db.Set<Warehouse>().AsNoTracking()
            .Where(w => w.Status == "active" || w.Id == current)
            .OrderByDescending(w => w.IsDefault).ThenBy(w => w.Code)
            .Select(w => new WarehouseOptionDto { Id = w.Id, Code = w.Code, NameAr = w.NameAr, NameEn = w.NameEn, IsDefault = w.IsDefault })
            .ToListAsync(ct);
        if (allowed != null) warehouses = warehouses.Where(s => allowed.Contains(s.Id)).ToList();
        return new WarehouseSessionDto
        {
            Warehouses = warehouses, AllWarehouses = allowed == null, CurrentWarehouseId = current, DocumentVisibility = _db.SessionDocumentVisibility,
        };
    }

    public async Task<WarehouseSessionDto> SetDocumentVisibilityAsync(string visibility, CancellationToken ct = default)
    {
        visibility = (visibility ?? string.Empty).Trim().ToLowerInvariant();
        if (!Visibilities.Contains(visibility)) throw new ValidationFailedException(Messages.DocumentVisibilityInvalid);
        var tenant = await _db.Set<Tenant>().FirstOrDefaultAsync(ct) ?? throw new NotFoundException(Messages.CompanyNotFound);
        tenant.DocumentVisibility = visibility;
        await _db.SaveChangesAsync(ct);
        _db.SetWarehouseSession(_db.SessionWarehouseId, _db.SessionAllowedWarehouseIds, visibility, _db.SessionFullAccess, _user.UserId);
        return await GetAsync(ct);
    }

    /// <summary>قائمة المواقع المخزّنة (فواصل)؛ فارغة = كل المواقع (null).</summary>
    public static List<Guid>? ParseIds(string? csv)
    {
        var ids = (csv ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).Distinct().ToList();
        return ids.Count == 0 ? null : ids;
    }
}
