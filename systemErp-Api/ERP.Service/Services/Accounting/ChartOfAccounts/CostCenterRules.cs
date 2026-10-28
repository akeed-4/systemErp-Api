using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// قواعد مراكز التكلفة المشتركة بين المحرك المحاسبي والمستندات والتقارير. البحث عبر مرشّح المنشأة،
/// فمركز منشأة أخرى «غير موجود» هنا ولا يمكن الإشارة إليه.
/// </summary>
internal static class CostCenterRules
{
    /// <summary>كل مركز مذكور موجود في المنشأة الحالية ونشط (المراكز الفارغة متجاهَلة).</summary>
    public static async Task EnsureUsableAsync(ErpDbContext db, IEnumerable<Guid?> ids, CancellationToken ct, bool requireActive = true)
    {
        var wanted = ids.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        if (wanted.Count == 0) return;
        var found = await db.Set<CostCenter>().AsNoTracking().Where(c => wanted.Contains(c.Id))
            .Select(c => new { c.Id, c.Code, c.IsActive }).ToListAsync(ct);
        if (found.Count != wanted.Count) throw new ValidationFailedException(string.Format(Messages.EntityNotFound, Messages.LabelCostCenter));
        if (!requireActive) return;
        var inactive = found.FirstOrDefault(c => !c.IsActive);
        if (inactive != null) throw new ValidationFailedException(string.Format(Messages.CostCenterInactiveNamed, inactive.Code));
    }

    /// <summary>
    /// تعيين مركز تكلفة أو تغييره على مستند يُصدر قيداً (فاتورة، عقد بيع سيارة، أمر شراء سيارات) يتطلب صلاحية
    /// <see cref="PermissionService.InvoiceCostCenterScreenId"/>؛ حفظ المستند بمراكزه المحفوظة كما هي لا يحتاجها.
    /// </summary>
    public static async Task EnsureCanAssignAsync(IPermissionService permissions, bool changed, CancellationToken ct)
    {
        if (!changed) return;
        if (!await permissions.HasPermissionAsync(PermissionService.InvoiceCostCenterScreenId, ScreenAction.Edit, ct))
            throw new ForbiddenException(Messages.InvoiceCostCenterForbidden);
    }

    /// <summary>
    /// نطاق مرشّح التقرير: المركز ومراكزه الفرعية كلها. فارغ = بلا مرشّح. المركز غير الموجود في المنشأة يُرفض
    /// بدل أن يعيد تقريراً فارغاً يوحي بأنه بلا حركة.
    /// </summary>
    public static async Task<List<Guid>?> ScopeAsync(ErpDbContext db, Guid? costCenterId, CancellationToken ct)
    {
        if (costCenterId is not { } root) return null;
        var all = await db.Set<CostCenter>().AsNoTracking().Select(c => new { c.Id, c.ParentId }).ToListAsync(ct);
        if (all.All(c => c.Id != root)) throw new NotFoundException(string.Format(Messages.EntityNotFound, Messages.LabelCostCenter));
        var children = all.Where(c => c.ParentId.HasValue).ToLookup(c => c.ParentId!.Value, c => c.Id);
        var scope = new List<Guid>();
        var pending = new Stack<Guid>(new[] { root });
        while (pending.Count > 0)
        {
            var id = pending.Pop();
            if (scope.Contains(id)) continue; // حماية من بيانات دائرية قديمة
            scope.Add(id);
            foreach (var child in children[id]) pending.Push(child);
        }
        return scope;
    }
}
