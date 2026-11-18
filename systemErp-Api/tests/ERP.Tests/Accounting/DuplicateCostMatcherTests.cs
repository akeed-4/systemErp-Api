using ERP.Service.Services.Accounting;

namespace ERP.Tests;

/// <summary>
/// استبعاد التكلفة المحمّلة مرتين لكل شهر: راتب أو إهلاك شهر بلا تحميل من التقارير يبقى كاملاً، والمستبعد في الشهر لا يتجاوز المحمّل فيه.
/// </summary>
public class DuplicateCostMatcherTests
{
    private static readonly Guid Engineer = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    [Fact]
    public void Only_months_with_absorbed_hours_are_excluded_and_never_more_than_absorbed()
    {
        // مهندس راتبه 10,000 شهرياً لسنة على مركز المشروع، وحُمّل بساعاته 4,000 في أكتوبر وحده
        var absorbed = new[] { (Engineer, "2026-10", 1500m), (Engineer, "2026-10", 2500m) };
        var payroll = Enumerable.Range(1, 12).Select(m => (Engineer, $"2026-{m:00}", 10000m)).ToList();

        var excluded = DuplicateCostMatcher.Exclusions(absorbed, payroll);

        Assert.Equal(4000m, excluded.Sum()); // لا 120,000
        Assert.Equal(4000m, excluded[9]);
        Assert.All(excluded.Where((_, i) => i != 9), e => Assert.Equal(0m, e));
    }

    [Fact]
    public void Several_postings_in_one_month_share_its_cap_and_other_people_are_untouched()
    {
        var absorbed = new[] { (Engineer, "2026-10", 6000m) };
        var postings = new List<(Guid, string, decimal)>
        {
            (Engineer, "2026-10", 5000m), // مسير الشهر
            (Engineer, "2026-10", 3000m), // مسير إضافي في الشهر نفسه: يأخذ ما بقي (1,000)
            (Other, "2026-10", 7000m),    // موظف آخر بلا تحميل
        };

        Assert.Equal(new[] { 5000m, 1000m, 0m }, DuplicateCostMatcher.Exclusions(absorbed, postings));
    }

    [Fact]
    public void Period_follows_the_payroll_and_depreciation_format()
        => Assert.Equal("2026-03", DuplicateCostMatcher.PeriodOf(new DateTime(2026, 3, 31)));
}
