using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// أثر الحضور على الراتب. أجر اليوم = الراتب الثابت ÷ 30، وأجر الساعة = أجر اليوم ÷ 8.
/// الغياب يُخصم بأجر يومه والتأخير بأجر دقائقه. ساعة العمل الإضافي = أجر الساعة + 50% من أجر الساعة الأساسي
/// (نظام العمل، المادة 107).
/// </summary>
public static class AttendancePay
{
    public const int WorkHoursPerDay = 8;
    public const decimal OvertimeBasicPremium = 0.5m;
    private const decimal MinutesPerHour = 60m;

    public static decimal Package(Employee e) => e.BasicSalary + e.HousingAllowance + e.TransportAllowance + e.OtherAllowances;

    private static decimal HourlyWage(decimal monthly) => monthly / LeaveBalances.PayrollMonthDays / WorkHoursPerDay;

    public static decimal Deduction(Employee e, decimal absentDays, int lateMinutes)
        => DocumentPricing.Round(Package(e) / LeaveBalances.PayrollMonthDays * absentDays + HourlyWage(Package(e)) * lateMinutes / MinutesPerHour);

    public static decimal Overtime(Employee e, decimal hours)
        => DocumentPricing.Round(hours * (HourlyWage(Package(e)) + OvertimeBasicPremium * HourlyWage(e.BasicSalary)));
}
