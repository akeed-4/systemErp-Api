using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// انعكاس ملف الموظف وقسمه على السجلات المرتبطة بهما بالمعرّف. أعضاء المشاريع يقرؤون الموظف والقسم مباشرة عند العرض؛
/// وهنا تُحدَّث النصوص المنسوخة للبحث والطباعة والقوائم القديمة: قسم المستخدم المربوط ومسماه، واسم مقدّم طلب الشراء وقسمه.
/// </summary>
public static class HrLinks
{
    public static async Task SyncEmployeeAsync(ErpDbContext db, Guid employeeId, CancellationToken ct)
    {
        var e = await (from x in db.Set<Employee>().AsNoTracking()
                       join d in db.Set<Department>().AsNoTracking() on x.DepartmentId equals (Guid?)d.Id into ds
                       from d in ds.DefaultIfEmpty()
                       where x.Id == employeeId
                       select new { x.UserId, x.NameAr, x.JobTitle, x.DepartmentId, Department = d != null ? d.NameAr : null }).FirstOrDefaultAsync(ct);
        if (e == null) return;
        if (e.UserId is Guid userId)
            await db.Set<User>().Where(u => u.Id == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Department, e.Department).SetProperty(u => u.JobTitle, e.JobTitle), ct);
        await db.Set<MaterialRequisition>().Where(r => r.RequestedByEmployeeId == employeeId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RequestedBy, e.NameAr), ct);
    }

    public static async Task SyncDepartmentAsync(ErpDbContext db, Guid departmentId, CancellationToken ct)
    {
        var name = await db.Set<Department>().AsNoTracking().Where(d => d.Id == departmentId).Select(d => d.NameAr).FirstOrDefaultAsync(ct);
        if (name == null) return;
        await db.Set<MaterialRequisition>().Where(r => r.DepartmentId == departmentId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Department, name), ct);
        var users = db.Set<Employee>().Where(e => e.DepartmentId == departmentId && e.UserId != null).Select(e => e.UserId);
        await db.Set<User>().Where(u => users.Contains(u.Id)).ExecuteUpdateAsync(s => s.SetProperty(u => u.Department, name), ct);
    }
}
