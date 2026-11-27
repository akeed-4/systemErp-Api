using ERP.Service.Data;
using G = ERP.Core.Models.Accounting.TailoringGarmentStatuses;
using St = ERP.Core.Models.Accounting.TailoringOrderStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>قواعد الحالة المشتركة بين الإنتاج والتسليم: سجل انتقال القطعة، واشتقاق حالة الأمر من قطعه.</summary>
public static class TailoringFlow
{
    /// <summary>يسجل انتقال القطعة (المرحلة أو الحالة) بمدة المرحلة السابقة ومنفّذها، ويبدأ عدّاد المرحلة الجديدة.</summary>
    public static void Record(ErpDbContext db, TailoringGarment g, Guid? toStage, string toStatus, string? note, string? by, Guid? userId)
    {
        var now = SaudiTime.Now;
        if (note is { Length: > 1000 }) note = note[..1000];
        db.Add(new TailoringStageHistory
        {
            OrderId = g.OrderId, GarmentId = g.Id, FromStageId = g.CurrentStageId, ToStageId = toStage, FromStatus = g.Status, ToStatus = toStatus,
            EmployeeId = g.AssignedEmployeeId, DurationMinutes = g.StageStartedAt is DateTime s ? Math.Max(0, (int)(now - s).TotalMinutes) : 0,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(), ChangedBy = by, ChangedByUserId = userId, ChangedAt = now,
        });
        if (toStage != g.CurrentStageId || g.StageStartedAt == null) g.StageStartedAt = now;
        g.Status = toStatus;
    }

    /// <summary>
    /// حالة الأمر من قطعه غير الملغاة: كلها مُسلَّمة = «مُسلَّم»، وبعضها = «مُسلَّم جزئياً»؛ وإلا بالأولوية: تعديل، بروفة، جودة، إنتاج، جاهز.
    /// يُستدعى بعد حفظ تغييرات القطع. المغلق والملغى لا يتغيران.
    /// </summary>
    public static async Task DeriveOrderAsync(ErpDbContext db, TailoringOrder o, string? by, Guid? userId, CancellationToken ct)
    {
        if (o.Status is St.Draft or St.Closed or St.Cancelled) return;
        var statuses = await db.Set<TailoringGarment>().Where(g => g.OrderId == o.Id && g.Status != G.Cancelled).Select(g => g.Status).ToListAsync(ct);
        if (statuses.Count == 0) return;
        var delivered = statuses.Count(s => s == G.Delivered);
        var open = statuses.Where(s => s != G.Delivered).ToList();
        var to = delivered == statuses.Count ? St.Delivered
            : delivered > 0 ? St.PartiallyDelivered
            : open.Contains(G.Modification) ? St.ModificationRequired
            : open.Contains(G.AwaitingFitting) ? St.AwaitingFitting
            : open.Contains(G.Quality) ? St.QualityInspection
            : open.Contains(G.InProduction) ? St.InProduction
            : open.All(s => s == G.Ready) ? St.ReadyForDelivery
            : open.Contains(G.Ready) ? St.InProduction
            : St.Confirmed;
        if (to == o.Status) return;
        db.Add(new TailoringOrderStatusHistory
        {
            OrderId = o.Id, FromStatus = o.Status, ToStatus = to, ChangedBy = by, ChangedByUserId = userId, ChangedAt = SaudiTime.Now,
        });
        o.Status = to;
    }
}
