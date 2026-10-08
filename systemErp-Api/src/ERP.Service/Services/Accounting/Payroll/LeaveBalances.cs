using ERP.Core.DTOs.Accounting;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// حساب رصيد الإجازة السنوية: الاستحقاق 21 يوماً في السنة ويصبح 30 بعد خمس سنوات خدمة (نظام العمل، المادة 109)،
/// ما لم يحدَّد للموظف استحقاق في عقده. الرصيد يتراكم يومياً من تاريخ الالتحاق، أو من تاريخ الرصيد الافتتاحي إن وُجد.
/// </summary>
public static class LeaveBalances
{
    public const int StandardDays = 21;
    public const int SeniorDays = 30;
    public const int SeniorAfterYears = 5;
    private const decimal DaysPerYear = 365m;
    /// <summary>الشهر 30 يوماً في حساب أجر اليوم.</summary>
    public const int PayrollMonthDays = 30;

    /// <summary>الاستحقاق السنوي الساري في تاريخ معيّن.</summary>
    public static int EntitlementOn(Employee e, DateTime date)
        => e.AnnualLeaveDays ?? (date.Date >= e.HireDate.Date.AddYears(SeniorAfterYears) ? SeniorDays : StandardDays);

    /// <summary>بداية التراكم: تاريخ الرصيد الافتتاحي إن وُجد، وإلا تاريخ الالتحاق.</summary>
    public static DateTime AccrualStart(Employee e) => (e.OpeningLeaveBalanceDate ?? e.HireDate).Date;

    /// <summary>الأيام المتراكمة من بداية التراكم حتى التاريخ (شاملاً)، وتتوقف عند آخر يوم عمل.</summary>
    public static decimal Accrued(Employee e, DateTime asOf)
    {
        var start = AccrualStart(e);
        var end = asOf.Date;
        if (e.TerminationDate.HasValue && e.TerminationDate.Value.Date < end) end = e.TerminationDate.Value.Date;
        if (end < start) return 0;

        if (e.AnnualLeaveDays.HasValue) return Span(start, end) * e.AnnualLeaveDays.Value / DaysPerYear;

        // قبل إتمام خمس سنوات بالاستحقاق الأساسي، وبعدها بالأعلى
        var seniorFrom = e.HireDate.Date.AddYears(SeniorAfterYears);
        if (end < seniorFrom) return Span(start, end) * StandardDays / DaysPerYear;
        if (start >= seniorFrom) return Span(start, end) * SeniorDays / DaysPerYear;
        return Span(start, seniorFrom.AddDays(-1)) * StandardDays / DaysPerYear + Span(seniorFrom, end) * SeniorDays / DaysPerYear;
    }

    /// <summary>الرصيد من الطلبات السنوية للموظف (المعتمدة والمعلّقة) منذ بداية التراكم.</summary>
    public static LeaveBalanceDto Compute(Employee e, DateTime asOf, IEnumerable<LeaveRequest> annualRequests)
    {
        var start = AccrualStart(e);
        var relevant = annualRequests.Where(r => r.IsAnnual && r.StartDate.Date >= start).ToList();
        var taken = relevant.Where(r => r.Status == LeaveStatuses.Approved).Sum(r => (decimal)r.Days);
        var pending = relevant.Where(r => r.Status == LeaveStatuses.Pending).Sum(r => (decimal)r.Days);
        var accrued = Math.Round(Accrued(e, asOf), 2);
        var balance = e.OpeningLeaveBalance + accrued - taken;
        return new LeaveBalanceDto
        {
            EmployeeId = e.Id, EmployeeCode = e.Code, EmployeeName = e.NameAr, AsOf = asOf.Date,
            AnnualEntitlement = EntitlementOn(e, asOf), Opening = e.OpeningLeaveBalance, Accrued = accrued,
            Taken = taken, Pending = pending, Balance = balance, Available = balance - pending,
        };
    }

    /// <summary>عدد الأيام التقويمية بين تاريخين شاملاً الطرفين.</summary>
    public static int Span(DateTime start, DateTime end) => (end.Date - start.Date).Days + 1;
}
