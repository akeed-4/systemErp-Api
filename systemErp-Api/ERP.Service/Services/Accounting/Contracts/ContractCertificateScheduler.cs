using ERP.Core.DTOs.Accounting.Contracts;
using Kinds = ERP.Core.Models.Accounting.ContractCertificateKinds;
using Schedules = ERP.Core.Models.Accounting.ContractCertificateSchedules;
using St = ERP.Core.Models.Accounting.ContractCertificateStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>مستخلص غير ملغى كما تحتاجه الجدولة (بترتيب رقمه داخل العقد).</summary>
internal sealed record ScheduledCertificate(Guid Id, string Number, string Status, string Kind,
    DateTime? PeriodFrom, DateTime? PeriodTo, decimal CurrentWorkValue, decimal NetPayable, bool CompletesContract = false)
{
    /// <summary>ينهي الجدولة: ختامي، أو مستخلص أكمل كل البنود 100% (فلا تُفرض بقية الفترات حتى نهاية العقد).</summary>
    public bool Closes => Kind == Kinds.Final || CompletesContract;
}

/// <summary>
/// جدولة مستخلصات عقد المقاولات حسب اتفاق العقد:
/// <list type="bullet">
/// <item>عدد دفعات محدد: N خانة بمواعيد موزّعة بالتساوي على مدة العقد، والأخيرة ختامية، ولا مستخلص بعدها.</item>
/// <item>دوري كل N شهر: الفترة الأولى من بداية العقد حتى نهاية الشهر (N−1 بعده)، وكل فترة تبدأ بعد نهاية سابقتها
/// وآخرها يُقص عند نهاية العقد. المستخلص المحفوظ يحتفظ بفترته فلا يتغير الجدول بأثر رجعي.</item>
/// </list>
/// المستخلصات غير الملغاة تشغل الخانات بترتيبها، والختامي (أو مستخلص أكمل كل البنود 100%) يُغلق ما بعده. الفترة موعد استحقاق
/// لا قفل: المستخلص المقدَّم قبل نهايتها تُقص فترته بتاريخ تقديمه.
/// </summary>
internal static class ContractCertificateScheduler
{
    public static CertificateScheduleDto Build(CommercialContract c, IReadOnlyList<ScheduledCertificate> certs, DateTime today)
    {
        var dto = new CertificateScheduleDto
        {
            Mode = c.CertificateSchedule, Count = c.CertificateCount, PeriodMonths = c.CertificatePeriodMonths, Used = certs.Count,
        };
        var finalAt = IndexOf(certs, x => x.Closes);
        var finalApproved = certs.Any(x => x.Closes && x.Status is St.Approved or St.Invoiced);
        var count = c.CertificateCount ?? 0;
        dto.Complete = finalApproved || (c.CertificateSchedule == Schedules.Installments && count > 0 && certs.Count >= count);

        var periods = new List<(DateTime? From, DateTime? To, DateTime? Due)>();
        switch (c.CertificateSchedule)
        {
            case Schedules.Installments:
                for (var k = 1; k <= Math.Max(count, certs.Count); k++)
                    periods.Add((certs.ElementAtOrDefault(k - 1)?.PeriodFrom, certs.ElementAtOrDefault(k - 1)?.PeriodTo, PlannedDate(c, k, count)));
                break;
            case Schedules.Periodic:
                var months = c.CertificatePeriodMonths ?? 1;
                var from = (c.StartDate ?? certs.FirstOrDefault()?.PeriodFrom ?? today).Date;
                for (var k = 1; k <= Schedules.MaxCount; k++)
                {
                    var cert = certs.ElementAtOrDefault(k - 1);
                    var (pf, pt) = cert is { PeriodFrom: { } f, PeriodTo: { } t } ? (f.Date, t.Date) : PeriodFrom(from, months, c.EndDate);
                    periods.Add((pf, pt, pt));
                    from = pt.AddDays(1);
                    if (k < certs.Count) continue;
                    // بعد آخر مستخلص: حتى نهاية العقد، أو حتى الفترة الجارية لعقد بلا نهاية؛ والختامي يُنهي الجدول
                    if (finalAt >= 0 && k > finalAt) break;
                    if (c.EndDate is { } end ? from > end.Date : pt >= today) break;
                }
                break;
            default:
                periods.AddRange(certs.Select(x => (x.PeriodFrom, x.PeriodTo, (DateTime?)null)));
                break;
        }

        for (var i = 0; i < periods.Count; i++)
        {
            var cert = certs.ElementAtOrDefault(i);
            var slot = new CertificateScheduleSlotDto
            {
                Number = i + 1, PeriodFrom = periods[i].From, PeriodTo = periods[i].To, DueDate = periods[i].Due,
                CertificateId = cert?.Id, CertificateNumber = cert?.Number, CertificateStatus = cert?.Status, Kind = cert?.Kind,
                CurrentWorkValue = cert?.CurrentWorkValue, NetPayable = cert?.NetPayable,
            };
            slot.State = cert switch
            {
                { Status: St.Approved or St.Invoiced } => "done",
                not null => "open",
                _ when dto.Complete || (finalAt >= 0 && i > finalAt) => "closed",
                _ when slot.DueDate is { } due && due.Date <= today => "due",
                _ => "upcoming",
            };
            dto.Slots.Add(slot);
        }

        if (dto.Complete) return dto;
        dto.NextNumber = certs.Count + 1;
        dto.NextMustBeFinal = c.CertificateSchedule == Schedules.Installments && certs.Count + 1 == count;
        var next = dto.Slots.ElementAtOrDefault(certs.Count);
        if (c.CertificateSchedule == Schedules.Periodic)
        {
            // تجاوز نهاية العقد (تمديد فعلي): الفترة التالية تبدأ بعد آخر مستخلص بلا قص
            var (nf, nt) = next is { PeriodFrom: { } f, PeriodTo: { } t } ? (f, t)
                : PeriodFrom((certs.LastOrDefault()?.PeriodTo?.AddDays(1) ?? c.StartDate ?? today).Date, c.CertificatePeriodMonths ?? 1, null);
            dto.NextPeriodFrom = nf; dto.NextPeriodTo = nt; dto.NextDueDate = nt;
        }
        else dto.NextDueDate = next?.DueDate;
        return dto;
    }

    /// <summary>فترة تبدأ في <paramref name="from"/> وتنتهي بنهاية الشهر بعد (الأشهر − 1)، مقصوصة عند نهاية العقد.</summary>
    private static (DateTime From, DateTime To) PeriodFrom(DateTime from, int months, DateTime? contractEnd)
    {
        var last = from.AddMonths(Math.Max(1, months) - 1);
        var to = new DateTime(last.Year, last.Month, DateTime.DaysInMonth(last.Year, last.Month));
        if (contractEnd is { } end && end.Date >= from && to > end.Date) to = end.Date;
        return (from, to);
    }

    /// <summary>موعد الدفعة k من N موزّعاً بالتساوي على مدة العقد؛ بلا مدة محددة لا موعد.</summary>
    private static DateTime? PlannedDate(CommercialContract c, int k, int count)
    {
        if (c.StartDate is not { } start || c.EndDate is not { } end || count <= 0 || k > count) return null;
        var days = (end.Date - start.Date).TotalDays;
        return start.Date.AddDays(Math.Round(days * k / count));
    }

    private static int IndexOf(IReadOnlyList<ScheduledCertificate> list, Func<ScheduledCertificate, bool> match)
    {
        for (var i = 0; i < list.Count; i++) if (match(list[i])) return i;
        return -1;
    }
}
