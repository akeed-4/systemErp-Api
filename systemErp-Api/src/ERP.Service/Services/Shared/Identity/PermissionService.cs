using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// نظام الصلاحيات الموحّد للنظم الثلاثة (محاسبة، معرض سيارات، POS). يطابق منطق PermissionService في الواجهة:
/// المالك والمدير صلاحيات كاملة، شاشة الصلاحيات مفتوحة لأربعة أدوار، وشاشة غير مسجّلة = مسموحة.
/// </summary>
public class PermissionService : IPermissionService
{
    public static readonly List<ScreenDefinitionDto> Screens = new()
    {
        new() { Id = "dashboard", NameAr = "لوحة المؤشرات والملخص المالي", NameEn = "Dashboard & Summary" },
        new() { Id = "master-data", NameAr = "البيانات الرئيسية (العملاء/الموردين/الأصناف)", NameEn = "Master Data & Entities" },
        new() { Id = "sales", NameAr = "فواتير المبيعات ونقاط البيع (POS)", NameEn = "Sales Invoices & POS" },
        new() { Id = "sales-returns", NameAr = "مرتجع المبيعات والإشعارات الدائنة", NameEn = "Sales Returns & Credit Notes" },
        new() { Id = "purchases", NameAr = "فواتير المشتريات وتكلفة المخزون", NameEn = "Purchase Invoices" },
        new() { Id = "vouchers", NameAr = "سندات القبض والصرف", NameEn = "Receipt & Payment Vouchers" },
        new() { Id = "accounts", NameAr = "دليل الحسابات والقيود اليومية", NameEn = "Chart of Accounts" },
        new() { Id = "zatca", NameAr = "الربط الإلكتروني والفوترة (ZATCA)", NameEn = "ZATCA Integration" },
        new() { Id = "car-showroom", NameAr = "إدارة وتعاريف معارض السيارات والمبايعات", NameEn = "Car Showroom & Automotive" },
        new() { Id = "reports", NameAr = "التقارير المالية والإقرار الضريبي", NameEn = "Financial Reports" },
        new() { Id = "approval-policies", NameAr = "سياسات الموافقات والطلبات", NameEn = "Approval Policies & Requests" },
        new() { Id = "user-permissions", NameAr = "إدارة صلاحيات المستخدمين والشاشات", NameEn = "User Roles & Screen Permissions" },
    };

    private static readonly HashSet<string> PermissionAdminRoles = new() { "owner", "admin", "chief_accountant", "general_manager" };

    private readonly ErpDbContext _db;
    private readonly ICurrentUser _user;

    public PermissionService(ErpDbContext db, ICurrentUser user)
    {
        _db = db; _user = user;
    }

    public List<ScreenDefinitionDto> GetScreens() => Screens;

    /// <summary>الصلاحيات الافتراضية للدور (نسخة طبق الأصل من getDefaultPermissionsForRole).</summary>
    public static List<ScreenPermissionDto> DefaultsFor(string role)
        => Screens.Select(s =>
        {
            bool canView = true, canCreate = true, canEdit = true, canDelete = true, canApprove = true;
            if (role == "sales_rep")
            {
                canApprove = false; canDelete = false;
                if (s.Id is "accounts" or "approval-policies" or "user-permissions" or "reports")
                    canView = canCreate = canEdit = false;
            }
            else if (role is "chief_accountant" or "general_manager")
            {
                if (s.Id == "user-permissions") { canView = canCreate = canEdit = canApprove = true; canDelete = false; }
            }
            else if (role is not ("owner" or "admin"))
            {
                if (s.Id == "user-permissions") canView = canCreate = canEdit = canDelete = canApprove = false;
            }
            return new ScreenPermissionDto
            {
                ScreenId = s.Id, ScreenNameAr = s.NameAr, ScreenNameEn = s.NameEn,
                CanView = canView, CanCreate = canCreate, CanEdit = canEdit, CanDelete = canDelete, CanApprove = canApprove,
            };
        }).ToList();

    public async Task<List<ScreenPermissionDto>> GetForUserOrRoleAsync(Guid? userId, string? roleId, CancellationToken ct = default)
    {
        UserRolePermission? match = null;
        if (userId.HasValue)
            match = await _db.Set<UserRolePermission>().AsNoTracking().Include(p => p.Permissions)
                .FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (match == null && !string.IsNullOrEmpty(roleId))
            match = await _db.Set<UserRolePermission>().AsNoTracking().Include(p => p.Permissions)
                .FirstOrDefaultAsync(p => p.RoleId == roleId && p.UserId == null, ct);

        if (match == null) return DefaultsFor(string.IsNullOrEmpty(roleId) ? "owner" : roleId);

        var names = Screens.ToDictionary(s => s.Id);
        return match.Permissions.Select(p => new ScreenPermissionDto
        {
            ScreenId = p.ScreenId,
            ScreenNameAr = names.GetValueOrDefault(p.ScreenId)?.NameAr ?? p.ScreenId,
            ScreenNameEn = names.GetValueOrDefault(p.ScreenId)?.NameEn ?? p.ScreenId,
            CanView = p.CanView, CanCreate = p.CanCreate, CanEdit = p.CanEdit, CanDelete = p.CanDelete, CanApprove = p.CanApprove,
        }).ToList();
    }

    public async Task SaveAsync(SavePermissionsRequestDto request, CancellationToken ct = default)
    {
        if (request.UserId == null && string.IsNullOrEmpty(request.RoleId))
            throw new ValidationFailedException("حدّد المستخدم أو الدور.");
        var unknown = request.Permissions.Select(p => p.ScreenId).Except(Screens.Select(s => s.Id)).ToList();
        if (unknown.Count > 0) throw new ValidationFailedException("شاشات غير معروفة: " + string.Join(", ", unknown));
        if (request.UserId.HasValue && !await _db.Set<User>().AnyAsync(u => u.Id == request.UserId, ct))
            throw new NotFoundException("المستخدم غير موجود");

        var existing = await _db.Set<UserRolePermission>().Include(p => p.Permissions)
            .FirstOrDefaultAsync(p => request.UserId != null
                ? p.UserId == request.UserId
                : p.RoleId == request.RoleId && p.UserId == null, ct);

        if (existing == null)
        {
            existing = new UserRolePermission { UserId = request.UserId, RoleId = request.UserId == null ? request.RoleId : null };
            _db.Add(existing);
        }
        else
        {
            _db.RemoveRange(existing.Permissions);
            existing.Permissions.Clear();
        }

        foreach (var p in request.Permissions)
            existing.Permissions.Add(new ScreenPermissionItem
            {
                ScreenId = p.ScreenId, CanView = p.CanView, CanCreate = p.CanCreate,
                CanEdit = p.CanEdit, CanDelete = p.CanDelete, CanApprove = p.CanApprove,
            });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<bool> HasPermissionAsync(string screenId, ScreenAction action, CancellationToken ct = default)
    {
        if (!_user.IsAuthenticated || _user.RoleId == null) return false;
        var role = _user.RoleId;

        if (screenId == "user-permissions" && PermissionAdminRoles.Contains(role)) return true;
        if (role is "owner" or "admin") return true;

        var perms = await GetForUserOrRoleAsync(_user.UserId, role, ct);
        var screen = perms.FirstOrDefault(p => p.ScreenId == screenId);
        if (screen == null) return true; // شاشة غير مقيّدة

        return action switch
        {
            ScreenAction.View => screen.CanView,
            ScreenAction.Create => screen.CanCreate,
            ScreenAction.Edit => screen.CanEdit,
            ScreenAction.Delete => screen.CanDelete,
            ScreenAction.Approve => screen.CanApprove,
            _ => false,
        };
    }
}
