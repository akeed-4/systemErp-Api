using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// انعكاس ملف الموظف وقسمه على السجلات المرتبطة بهما بالمعرّف. أعضاء المشاريع يقرؤون الموظف والقسم مباشرة عند العرض؛
/// وهنا تُحدَّث النصوص المنسوخة للبحث والطباعة والقوائم القديمة: المستخدم المربوط (اسمه وحروفه وقسمه ومسماه)، واسم مقدّم طلب الشراء وقسمه.
/// المستخدم المربوط (حساب البوابة أو مستخدم النظام) يُعدَّل متتبَّعاً لا بـ ExecuteUpdate: المستخدمون من سجلات الدليل، وناسخ الدليل
/// (<see cref="TenantDirectoryMirror"/>) يلتقط الحفظ وحده، فيصل التعديل للقاعدة المشتركة في المنشأة المستقلة.
/// </summary>
public static class HrLinks
{
    /// <summary>
    /// ملف الموظف هو مصدر اسم المستخدم المربوط به. <paramref name="terminated"/>: انتهت خدمته الآن، فيُعطَّل المستخدم
    /// (المالك لا يُعطَّل). إعادة تنشيط الموظف لا تعيد تنشيط المستخدم: تمرّ بشاشة المستخدمين وحد المقاعد.
    /// </summary>
    public static async Task SyncEmployeeAsync(ErpDbContext db, Guid employeeId, CancellationToken ct, bool terminated = false)
    {
        var e = await (from x in db.Set<Employee>().AsNoTracking()
                       join d in db.Set<Department>().AsNoTracking() on x.DepartmentId equals (Guid?)d.Id into ds
                       from d in ds.DefaultIfEmpty()
                       where x.Id == employeeId
                       select new { x.UserId, x.NameAr, x.JobTitle, x.DepartmentId, Department = d != null ? d.NameAr : null }).FirstOrDefaultAsync(ct);
        if (e == null) return;
        if (e.UserId is Guid userId && await db.Set<User>().FirstOrDefaultAsync(u => u.Id == userId, ct) is { } user)
        {
            var name = e.NameAr.Trim();
            user.Name = name;
            user.AvatarInitials = AuthService.Initials(name);
            user.Department = e.Department;
            user.JobTitle = e.JobTitle;
            if (terminated) Deactivate(user);
            await db.SaveChangesAsync(ct);
        }
        await db.Set<MaterialRequisition>().Where(r => r.RequestedByEmployeeId == employeeId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RequestedBy, e.NameAr), ct);
    }

    /// <summary>
    /// حذف ملف الموظف: حساب البوابة وحده (دور «موظف»، لا يفتح شاشات النظام) يُحذف معه ومع صلاحياته ودعواته، فيختفي من قائمة المستخدمين.
    /// مستخدم النظام يُعطَّل ويُفك ربطه ولا يُحذف: اسمه في المستندات والتدقيق. المالك لا يُمس. الحذف متتبَّع فيلتقطه ناسخ الدليل،
    /// ويُحفظ مع حذف الملف.
    /// </summary>
    public static async Task ReleaseLinkedUserAsync(ErpDbContext db, Employee employee, CancellationToken ct)
    {
        if (employee.UserId is not Guid userId) return;
        employee.UserId = null;
        if (await db.Set<User>().FirstOrDefaultAsync(u => u.Id == userId, ct) is not { } user) return;
        if (user.Role != UserRole.Employee)
        {
            Deactivate(user);
            return;
        }
        db.RemoveRange(await db.Set<UserRolePermission>().Where(p => p.UserId == userId).ToListAsync(ct));
        db.RemoveRange(await db.Set<UserInvite>().Where(i => i.UserId == userId).ToListAsync(ct));
        db.Remove(user);
    }

    /// <summary>المالك لا يُعطَّل أبداً (كما في شاشة المستخدمين).</summary>
    private static void Deactivate(User user)
    {
        if (user.Role != UserRole.Owner) user.IsActive = false;
    }

    public static async Task SyncDepartmentAsync(ErpDbContext db, Guid departmentId, CancellationToken ct)
    {
        var name = await db.Set<Department>().AsNoTracking().Where(d => d.Id == departmentId).Select(d => d.NameAr).FirstOrDefaultAsync(ct);
        if (name == null) return;
        await db.Set<MaterialRequisition>().Where(r => r.DepartmentId == departmentId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Department, name), ct);
        var users = db.Set<Employee>().Where(e => e.DepartmentId == departmentId && e.UserId != null).Select(e => e.UserId);
        var linked = await db.Set<User>().Where(u => users.Contains(u.Id)).ToListAsync(ct);
        if (linked.Count == 0) return;
        foreach (var user in linked) user.Department = name;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>الموظف المربوط بكل مستخدم (اسمه يُدار من ملفه، فلا يُعدَّل من شاشة المستخدمين أو الملف الشخصي).</summary>
    public static async Task<Dictionary<Guid, Guid>> EmployeesOfUsersAsync(ErpDbContext db, IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return new();
        var pairs = await db.Set<Employee>().AsNoTracking().Where(e => e.UserId != null && ids.Contains(e.UserId.Value))
            .Select(e => new { UserId = e.UserId!.Value, e.Id }).ToListAsync(ct);
        return pairs.GroupBy(p => p.UserId).ToDictionary(g => g.Key, g => g.First().Id);
    }
}
