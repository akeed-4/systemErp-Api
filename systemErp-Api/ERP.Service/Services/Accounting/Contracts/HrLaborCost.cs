using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// تكلفة ساعة المهنة من شؤون الموظفين (للتخطيط في مكتبة المقاولات، لا للرواتب): متوسط التكلفة الشهرية المحمّلة لموظفي
/// المسمى الوظيفي الفعّالين (وقسمه إن حُدد) ÷ ساعات العمل المنتجة في الشهر. التكلفة المحمّلة للموظف =
/// الراتب والبدلات + حصة المنشأة في التأمينات من الأساسي والسكن + مخصص نهاية الخدمة الشهري (نصف شهر عن كل سنة من
/// الخمس الأولى وشهر عما بعدها، بأجر الراتب الكامل كما في <see cref="EndOfServiceService"/>).
/// </summary>
internal static class HrLaborCost
{
    /// <summary>ساعات العمل المنتجة شهرياً: 26 يوم عمل × 8 ساعات (التخطيط يحمّل الساعة المنتجة، لا أيام الشهر الثلاثين).</summary>
    public const decimal ProductiveHoursPerMonth = 26m * AttendancePay.WorkHoursPerDay;
    private const decimal HalfRateYears = 5m;
    private const decimal DaysPerYear = 365.25m;

    public sealed record Rate(decimal HourlyCost, int EmployeeCount);

    public static async Task<Rate> ForJobAsync(ErpDbContext db, string jobTitle, Guid? departmentId, CancellationToken ct)
    {
        var title = jobTitle.Trim();
        var q = db.Set<Employee>().AsNoTracking()
            .Where(e => e.Status == EmployeeStatuses.Active && e.JobTitle != null && e.JobTitle.Trim() == title);
        if (departmentId.HasValue) q = q.Where(e => e.DepartmentId == departmentId);
        var staff = await q.Select(e => new { e.BasicSalary, e.HousingAllowance, e.TransportAllowance, e.OtherAllowances, e.EmployerGosiRate, e.HireDate })
            .ToListAsync(ct);
        if (staff.Count == 0) return new Rate(0, 0);

        var today = DateTime.UtcNow.Date;
        var monthly = staff.Select(e => Monthly(e.BasicSalary, e.HousingAllowance, e.TransportAllowance, e.OtherAllowances, e.EmployerGosiRate, e.HireDate, today)).Average();
        return new Rate(monthly / ProductiveHoursPerMonth, staff.Count);
    }

    /// <summary>
    /// التكلفة الشهرية المحمّلة لموظف بعينه (لمورد مشروع مربوط بموظف: مهندس أو عامل) بالقاعدة نفسها أعلاه.
    /// </summary>
    public static decimal MonthlyCost(Employee e, DateTime today)
        => Math.Round(Monthly(e.BasicSalary, e.HousingAllowance, e.TransportAllowance, e.OtherAllowances, e.EmployerGosiRate, e.HireDate, today), 2);

    private static decimal Monthly(decimal basic, decimal housing, decimal transport, decimal other, decimal employerGosiRate, DateTime hireDate, DateTime today)
    {
        var wage = basic + housing + transport + other;
        var gosi = (basic + housing) * employerGosiRate / 100m;
        var years = Math.Max(0, (decimal)(today - hireDate.Date).TotalDays / DaysPerYear);
        var endOfService = years < HalfRateYears ? wage / 24m : wage / 12m;
        return wage + gosi + endOfService;
    }
}
