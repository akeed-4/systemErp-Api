using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.Accounting.Contracts;
using Erp.Modules.Accounting.Domain;
using Erp.Modules.Accounting.Persistence;
using Erp.Modules.Settings.Contracts;
using Erp.SharedKernel.Errors;
using Erp.SharedKernel.Security;
using Erp.SharedKernel.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Accounting.Application;

/// <summary>
/// Validates and posts a draft journal entry inside the scope's unit of work: balanced, non-empty, leaf and active
/// accounts, open period; assigns the gap-free number and increments the per-period account balances atomically.
/// </summary>
internal sealed class PostingEngine(
    AccountingDbContext db,
    IUnitOfWork unitOfWork,
    INumberSequenceService numbers,
    ITenantContext tenant,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public const string SequenceType = "je";

    public async Task PostAsync(JournalEntry entry, CancellationToken ct)
    {
        entry.EnsureDraft();
        await ValidateAsync(entry, ct);
        await unitOfWork.EnsureTransactionAsync(ct);

        var period = await OpenPeriodForAsync(entry.Date, ct);
        entry.MarkPosted(await numbers.NextAsync(SequenceType, entry.Date, null, ct), clock.GetUtcNow(), currentUser.UserId);
        await db.SaveChangesAsync(ct);

        foreach (var perAccount in entry.Lines.GroupBy(l => l.AccountId))
        {
            await IncrementBalanceAsync(perAccount.Key, period.Id, perAccount.Sum(l => l.Debit), perAccount.Sum(l => l.Credit), ct);
        }
    }

    public async Task<FiscalPeriod> OpenPeriodForAsync(DateOnly date, CancellationToken ct)
    {
        var period = await db.FiscalPeriods.SingleOrDefaultAsync(p => p.Year == date.Year && p.Month == date.Month, ct);
        if (period is null)
        {
            // Periods of a new year are opened on first use (all 12 months at once).
            for (var month = 1; month <= 12; month++)
            {
                if (!await db.FiscalPeriods.AnyAsync(p => p.Year == date.Year && p.Month == month, ct))
                {
                    db.FiscalPeriods.Add(new FiscalPeriod(date.Year, month));
                }
            }

            await db.SaveChangesAsync(ct);
            period = await db.FiscalPeriods.SingleAsync(p => p.Year == date.Year && p.Month == date.Month, ct);
        }

        if (period.IsClosed)
        {
            throw ErpException.Conflict(
                "period_closed",
                $"The accounting period {date:yyyy-MM} is closed.",
                $"الفترة المحاسبية {date:yyyy-MM} مقفلة ولا يمكن الترحيل إليها.");
        }

        return period;
    }

    private async Task ValidateAsync(JournalEntry entry, CancellationToken ct)
    {
        if (entry.Lines.Count < 2)
        {
            throw ErpException.Validation("A journal entry needs at least two lines.", "القيد يجب أن يحتوي على سطرين على الأقل.");
        }

        if (entry.Lines.Any(l => l.Debit < 0 || l.Credit < 0 || (l.Debit > 0 && l.Credit > 0) || l.Debit + l.Credit == 0))
        {
            throw ErpException.Validation(
                "Each line must have either a positive debit or a positive credit.",
                "كل سطر يجب أن يحتوي على مبلغ مدين أو دائن موجب (وليس كليهما).");
        }

        if (entry.TotalDebit != entry.TotalCredit)
        {
            throw ErpException.Validation(
                $"The entry is not balanced (debit {entry.TotalDebit:0.00}, credit {entry.TotalCredit:0.00}).",
                $"القيد غير متوازن (مدين {entry.TotalDebit:0.00}، دائن {entry.TotalCredit:0.00}).");
        }

        var accountIds = entry.Lines.Select(l => l.AccountId).Distinct().ToList();
        var accounts = await db.Accounts.AsNoTracking().Where(a => accountIds.Contains(a.Id)).ToListAsync(ct);
        var invalid = accountIds.Except(accounts.Where(a => a.IsPostable && a.IsActive).Select(a => a.Id)).ToList();
        if (invalid.Count > 0)
        {
            var codes = string.Join(", ", accounts.Where(a => invalid.Contains(a.Id)).Select(a => a.Code));
            throw ErpException.Validation(
                $"Postings are allowed only on active leaf accounts ({codes}).",
                $"الترحيل مسموح فقط على الحسابات الفرعية النشطة ({codes}).");
        }

        var costCenters = entry.Lines.Where(l => l.CostCenterId is not null).Select(l => l.CostCenterId!.Value).Distinct().ToList();
        if (costCenters.Count > 0 && await db.CostCenters.CountAsync(c => costCenters.Contains(c.Id) && c.IsActive, ct) != costCenters.Count)
        {
            throw ErpException.Validation("Unknown or inactive cost center.", "مركز التكلفة غير موجود أو غير نشط.");
        }
    }

    private async Task IncrementBalanceAsync(Guid accountId, Guid periodId, decimal debit, decimal credit, CancellationToken ct)
    {
        var tenantId = tenant.TenantId;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var updated = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE [accounting].[AccountBalances] WITH (UPDLOCK, ROWLOCK)
                SET [Debit] = [Debit] + {debit}, [Credit] = [Credit] + {credit}
                WHERE [TenantId] = {tenantId} AND [AccountId] = {accountId} AND [FiscalPeriodId] = {periodId}
                """,
                ct);
            if (updated == 1)
            {
                return;
            }

            try
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO [accounting].[AccountBalances] ([Id], [TenantId], [AccountId], [FiscalPeriodId], [Debit], [Credit], [CreatedAt])
                    VALUES ({Guid.CreateVersion7()}, {tenantId}, {accountId}, {periodId}, {debit}, {credit}, {clock.GetUtcNow()})
                    """,
                    ct);
                return;
            }
            catch (SqlException ex) when (attempt == 0 && ex.Number is 2601 or 2627)
            {
                // Another transaction created the row first; increment it instead.
            }
        }
    }
}
