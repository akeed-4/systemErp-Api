using Erp.BuildingBlocks.Web;
using Erp.Modules.Accounting.Application;
using Erp.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Erp.Modules.Accounting.Endpoints;

/// <summary>Financial statements computed from posted journal lines (§12: accounting/reports/*).</summary>
internal static class AccountingReportEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var reports = app.MapGroup("/api/v1/accounting/reports").WithTags("Accounting").RequireScreen(ScreenIds.Reports, ScreenAction.View);
        reports.MapGet("trial-balance", async (DateOnly? from, DateOnly? to, AccountingQueries q, TimeProvider clock, CancellationToken ct) =>
        {
            var (f, t) = Range(from, to, clock);
            return ErpResults.Ok(await q.TrialBalanceAsync(f, t, ct));
        });
        reports.MapGet("income-statement", async (DateOnly? from, DateOnly? to, AccountingQueries q, TimeProvider clock, CancellationToken ct) =>
        {
            var (f, t) = Range(from, to, clock);
            return ErpResults.Ok(await q.IncomeStatementAsync(f, t, ct));
        });
        reports.MapGet("balance-sheet", async (DateOnly? asOf, AccountingQueries q, TimeProvider clock, CancellationToken ct) =>
            ErpResults.Ok(await q.BalanceSheetAsync(asOf ?? Today(clock), ct)));
        reports.MapGet("vat-return", async (DateOnly? from, DateOnly? to, AccountingQueries q, TimeProvider clock, CancellationToken ct) =>
        {
            var (f, t) = Range(from, to, clock);
            return ErpResults.Ok(await q.VatReturnAsync(f, t, ct));
        });
    }

    private static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    private static (DateOnly From, DateOnly To) Range(DateOnly? from, DateOnly? to, TimeProvider clock)
    {
        var today = Today(clock);
        return (from ?? new DateOnly(today.Year, 1, 1), to ?? today);
    }
}
