using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>شروط السطر التي يُسعَّر بها الكشف (لقطة السطر).</summary>
public sealed record RentalTimesheetTerms(
    string Basis, decimal Rate, bool WithOperator, decimal OperatorRate, decimal IncludedHours, decimal OvertimeRate,
    decimal DailyMinimumHours, decimal WorkingDaysPerPeriod, decimal StandbyPercent);

/// <summary>يوم الكشف كما يدخل التسعير.</summary>
public sealed record RentalTimesheetDayInput(DateTime Date, string Kind, decimal WorkedHours, decimal StandbyHours, decimal BreakdownHours);

public sealed record RentalTimesheetTotals(
    decimal WorkDays, decimal StandbyDays, decimal BreakdownDays, decimal WorkedHours, decimal StandbyHours, decimal BreakdownHours,
    decimal BilledQuantity, decimal OvertimeHours, decimal RentAmount, decimal StandbyAmount, decimal OvertimeAmount, decimal OperatorAmount)
{
    public decimal Amount => RentAmount + StandbyAmount + OvertimeAmount + OperatorAmount;
}

/// <summary>
/// تسعير كشف الساعات بممارسة السوق السعودي (T1). قواعد صرفة بلا قاعدة بيانات:
/// <list type="bullet">
/// <item><b>أساس الساعة:</b> كل يوم تشغيل يُحاسب بالأكبر من ساعاته والحد الأدنى اليومي؛ يوم الانتظار بالأكبر من ساعات انتظاره والحد الأدنى،
/// بنسبة الانتظار. لا إضافي (كل ساعة محسوبة).</item>
/// <item><b>اليوم/الأسبوع/الشهر:</b> سعر اليوم = السعر ÷ أيام العمل في الفترة (يوم 1، أسبوع 6، شهر 26 ما لم يُحدَّد)؛ أيام التشغيل بسعر اليوم،
/// وأيام الانتظار بنسبة الانتظار منه. الإضافي = ساعات التشغيل فوق (المشمول لليوم × أيام التشغيل) مجمّعة على الكشف، بسعر الإضافي؛ المشمول
/// لليوم = الساعات المشمولة ÷ أيام العمل (وفي أساس اليوم المشمول نفسه).</item>
/// <item>العطل والإجازة لا يُحاسبان (العطل علينا)، وتشغيل يوم الإجازة يُسجَّل يوم تشغيل فيُحاسب إضافياً على أيام الفترة.</item>
/// <item>المشغّل (إن وُجد): بسعره لكل وحدة محاسبة كالمعدة (ساعات محسوبة أو أيام تشغيل وانتظار كاملة).</item>
/// </list>
/// </summary>
public static class RentalTimesheetPricing
{
    public const decimal DefaultWeekDays = 6m;
    public const decimal DefaultMonthDays = 26m;

    /// <summary>أيام العمل التي يغطيها سعر الفترة (يوم = 1).</summary>
    public static decimal DaysPerPeriod(string basis, decimal configured) => basis switch
    {
        RentalRateBases.Day => 1m,
        RentalRateBases.Week => configured > 0 ? configured : DefaultWeekDays,
        RentalRateBases.Month => configured > 0 ? configured : DefaultMonthDays,
        _ => 1m,
    };

    /// <summary>الساعات المشمولة في يوم العمل (لتعبئة الكشف الجديد ولحساب الإضافي).</summary>
    public static decimal IncludedPerDay(RentalTimesheetTerms t)
        => t.Basis == RentalRateBases.Hour ? t.DailyMinimumHours : t.IncludedHours > 0 ? t.IncludedHours / DaysPerPeriod(t.Basis, t.WorkingDaysPerPeriod) : 0m;

    public static RentalTimesheetTotals Compute(RentalTimesheetTerms t, IReadOnlyCollection<RentalTimesheetDayInput> days)
    {
        var work = days.Where(d => d.Kind == RentalTimesheetDayKinds.Work).ToList();
        var standby = days.Where(d => d.Kind == RentalTimesheetDayKinds.Standby).ToList();
        var breakdown = days.Where(d => d.Kind == RentalTimesheetDayKinds.Breakdown).ToList();
        var worked = work.Sum(d => d.WorkedHours);
        var standbyHours = days.Sum(d => d.StandbyHours);
        var breakdownHours = days.Sum(d => d.BreakdownHours);
        var standbyFactor = Math.Max(0m, t.StandbyPercent) / 100m;
        var operatorRate = t.WithOperator ? t.OperatorRate : 0m;

        decimal billed, rent, standbyAmount, overtimeHours = 0m, overtimeAmount = 0m, operatorAmount;
        if (t.Basis == RentalRateBases.Hour)
        {
            var min = Math.Max(0m, t.DailyMinimumHours);
            billed = work.Sum(d => Math.Max(d.WorkedHours, min));
            var standbyBilled = standby.Sum(d => Math.Max(d.StandbyHours, min));
            rent = billed * t.Rate;
            standbyAmount = standbyBilled * t.Rate * standbyFactor;
            operatorAmount = (billed + standbyBilled) * operatorRate;
        }
        else
        {
            var perPeriod = DaysPerPeriod(t.Basis, t.WorkingDaysPerPeriod);
            var dayRate = t.Rate / perPeriod;
            billed = work.Count;
            rent = billed * dayRate;
            standbyAmount = standby.Count * dayRate * standbyFactor;
            var included = IncludedPerDay(t);
            if (included > 0 && t.OvertimeRate > 0)
            {
                overtimeHours = Math.Max(0m, decimal.Round(worked - included * work.Count, 2));
                overtimeAmount = overtimeHours * t.OvertimeRate;
            }
            operatorAmount = (work.Count + standby.Count) * (operatorRate / perPeriod);
        }

        return new RentalTimesheetTotals(
            work.Count, standby.Count, breakdown.Count, decimal.Round(worked, 2), decimal.Round(standbyHours, 2), decimal.Round(breakdownHours, 2),
            decimal.Round(billed, 2), overtimeHours, DocumentPricing.Round(rent), DocumentPricing.Round(standbyAmount), DocumentPricing.Round(overtimeAmount),
            DocumentPricing.Round(operatorAmount));
    }

    public static RentalTimesheetTerms TermsOf(RentalContractLine l)
        => new(l.Basis, l.Rate, l.WithOperator, l.OperatorRate, l.IncludedHours, l.OvertimeRate, l.DailyMinimumHours, l.WorkingDaysPerPeriod, l.StandbyPercent);
}
