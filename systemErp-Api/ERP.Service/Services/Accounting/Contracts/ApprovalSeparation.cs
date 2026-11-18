using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// فصل المُعِد عن المعتمِد في المقاولات (خيار <see cref="AccountingSettings.ConstructionSeparateApprover"/>): من أعدّ المستند أو قدّمه
/// لا يعتمده. معطّل افتراضياً، فالمنشأة التي يعمل فيها شخص واحد لا تتوقف. الطلب بلا مستخدم معروف (مهمة نظام) لا يُقيَّد.
/// </summary>
internal static class ApprovalSeparation
{
    public static async Task EnsureAsync(ErpDbContext db, Guid? approverUserId, CancellationToken ct, params Guid?[] preparedBy)
    {
        if (approverUserId is not Guid approver || !preparedBy.Any(p => p == approver)) return;
        var required = await db.Set<AccountingSettings>().AsNoTracking().Select(s => s.ConstructionSeparateApprover).FirstOrDefaultAsync(ct);
        if (required) throw new ConflictException(Messages.ConstructionApproverMustDiffer);
    }
}
