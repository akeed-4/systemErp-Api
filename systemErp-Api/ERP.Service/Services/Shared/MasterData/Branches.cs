using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// فرع الموقع: تحديده عند إنشاء الموقع أو نقله، والفرع الافتراضي للنشاط (يُنشأ «الفرع الرئيسي» إن لم يوجد أي فرع).
/// الاستعلامات تحت مرشّح المنشأة، فمعرّف فرع من نشاط آخر لا يُعثر عليه.
/// </summary>
public static class Branches
{
    public const string DefaultCode = "BR-001";
    public const string DefaultNameAr = "الفرع الرئيسي";
    public const string DefaultNameEn = "Main Branch";

    /// <summary>الفرع الافتراضي للنشاط، أو أقدم فرع، أو «الفرع الرئيسي» جديداً (يُضاف للسياق، والحفظ على المستدعي).</summary>
    public static async Task<Branch> DefaultAsync(ErpDbContext db, CancellationToken ct)
    {
        var branch = db.Set<Branch>().Local.FirstOrDefault(b => b.IsDefault)
            ?? await db.Set<Branch>().OrderByDescending(b => b.IsDefault).ThenBy(b => b.CreatedAt).FirstOrDefaultAsync(ct);
        if (branch != null) return branch;
        branch = new Branch { Id = Guid.NewGuid(), Code = DefaultCode, NameAr = DefaultNameAr, NameEn = DefaultNameEn, IsDefault = true };
        db.Add(branch);
        return branch;
    }

    /// <summary>
    /// فرع الموقع عند الحفظ: المرسَل يجب أن يكون فرعاً فعّالاً من النشاط نفسه (الإبقاء على فرع الموقع الحالي لا يُشترط له التفعيل)؛
    /// غير المرسَل عند التعديل يبقى، وعند الإنشاء يُلحق بالفرع الوحيد للنشاط، ومع تعدد الفروع يُطلب تحديده.
    /// </summary>
    public static async Task<Guid> ResolveForLocationAsync(ErpDbContext db, Guid? requested, Warehouse? existing, CancellationToken ct)
    {
        if (requested is not { } id || id == Guid.Empty)
        {
            if (existing != null && existing.BranchId != Guid.Empty) return existing.BranchId;
            var count = await db.Set<Branch>().CountAsync(ct);
            if (count > 1) throw new ValidationFailedException(Messages.LocationBranchRequired);
            return (await DefaultAsync(db, ct)).Id;
        }
        if (existing != null && existing.BranchId == id) return id;
        var branch = await db.Set<Branch>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct)
            ?? throw new ValidationFailedException(Messages.BranchNotFound);
        if (branch.Status != "active") throw new ValidationFailedException(Messages.BranchInactive);
        return id;
    }
}
