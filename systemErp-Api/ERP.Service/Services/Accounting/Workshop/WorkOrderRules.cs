using ERP.Service.Data;
using A = ERP.Core.Models.Accounting.LineApprovalStatuses;
using St = ERP.Core.Models.Accounting.WorkOrderStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// قواعد الورشة المشتركة (منطق صرف فوق البيانات): من يُنفَّذ من الأسطر، وشروط الانتقالات المرتبطة بالفحص والموافقة، وحساب مبالغ السطر.
/// كل مستند تنفيذ (ساعات الفنيين، صرف القطع، الخدمة الخارجية) يسأل <see cref="EnsureExecutableAsync"/> قبل أثره.
/// </summary>
internal static class WorkOrderRules
{
    /// <summary>صافي السطر قبل الضريبة وضريبته (مقرّبان لهللتين).</summary>
    public static (decimal Net, decimal Vat) Amounts(decimal quantity, decimal unitPrice, decimal discount, decimal vatRate)
    {
        var net = decimal.Round(quantity * unitPrice - discount, 2);
        return (net, decimal.Round(net * vatRate / 100m, 2));
    }

    /// <summary>الأسطر القابلة للتنفيذ: موافق عليها، أو لها استثناء موثق.</summary>
    public static async Task<HashSet<Guid>> ExecutableAsync(ErpDbContext db, Guid workOrderId, CancellationToken ct)
    {
        var approved = await db.Set<WorkOrderLine>().AsNoTracking().Where(l => l.WorkOrderId == workOrderId && l.ApprovalStatus == A.Approved)
            .Select(l => l.Id).ToListAsync(ct);
        var excepted = await db.Set<WorkOrderExecutionException>().AsNoTracking().Where(x => x.WorkOrderId == workOrderId).Select(x => x.LineId).ToListAsync(ct);
        return approved.Concat(excepted).ToHashSet();
    }

    /// <summary>يرفض التنفيذ على سطر غير موافق بلا استثناء موثق.</summary>
    public static async Task EnsureExecutableAsync(ErpDbContext db, Guid workOrderId, Guid lineId, CancellationToken ct)
    {
        if (!(await ExecutableAsync(db, workOrderId, ct)).Contains(lineId))
            throw new ConflictException(Messages.WorkOrderLineNotApproved);
    }

    /// <summary>شروط الانتقال المرتبطة بالفحص والموافقة (فوق قائمة الانتقالات المسموحة).</summary>
    public static async Task EnsureTransitionAsync(ErpDbContext db, WorkOrder o, string from, string target, CancellationToken ct)
    {
        if (target == St.AwaitingApproval && from == St.Received
            && (await db.Set<WorkshopSettings>().AsNoTracking().FirstOrDefaultAsync(ct))?.RequireReceptionInspection == true
            && !await db.Set<WorkOrderInspection>().AnyAsync(i => i.WorkOrderId == o.Id && i.Usage == InspectionUsages.Reception
                                                                  && i.Status == InspectionStatuses.Completed, ct))
            throw new ConflictException(Messages.WorkOrderInspectionRequired);
        if (target == St.Approved && !await db.Set<WorkOrderLine>().AnyAsync(l => l.WorkOrderId == o.Id && l.ApprovalStatus == A.Approved, ct))
            throw new ConflictException(Messages.WorkOrderNoApprovedLines);
        if (target == St.InProgress && (await ExecutableAsync(db, o.Id, ct)).Count == 0)
            throw new ConflictException(Messages.WorkOrderNoApprovedLines);

        // الجودة: كل سطر قابل للتنفيذ منجز، ولا سجل وقت مفتوح
        if (target == St.Quality)
        {
            var executable = await ExecutableAsync(db, o.Id, ct);
            var pending = await db.Set<WorkOrderLine>().AsNoTracking()
                .Where(l => l.WorkOrderId == o.Id && executable.Contains(l.Id) && l.ExecutionStatus != LineExecutionStatuses.Done)
                .Select(l => l.Description).ToListAsync(ct);
            if (pending.Count > 0) throw new ConflictException(string.Format(Messages.WorkOrderLinesNotDone, string.Join("، ", pending.Take(5))));
            if (await db.Set<WorkOrderTimeLog>().AnyAsync(l => l.WorkOrderId == o.Id && l.EndedAt == null, ct))
                throw new ConflictException(Messages.WorkOrderHasOpenLogs);
        }
        // جاهز: إن كان للمنشأة قالب جودة نشط فآخر فحص جودة مكتمل بلا بند فاشل، ولا فحص جودة مسودة
        if (target == St.Ready && from == St.Quality
            && await db.Set<InspectionTemplate>().AnyAsync(t => t.Usage == InspectionUsages.Quality && t.IsActive, ct))
        {
            var quality = await db.Set<WorkOrderInspection>().AsNoTracking().Include(i => i.Results)
                .Where(i => i.WorkOrderId == o.Id && i.Usage == InspectionUsages.Quality).OrderByDescending(i => i.CreatedAt).FirstOrDefaultAsync(ct);
            if (quality == null || quality.Status != InspectionStatuses.Completed) throw new ConflictException(Messages.WorkOrderQualityRequired);
            if (quality.Results.Any(r => r.Condition == InspectionConditions.Fail)) throw new ConflictException(Messages.WorkOrderQualityFailed);
        }
    }
}
