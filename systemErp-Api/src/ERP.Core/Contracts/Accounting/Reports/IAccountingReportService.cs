using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Shared;

using DevExtreme.AspNet.Data.ResponseModel;

namespace ERP.Core.Contracts.Accounting;

public interface IAccountingReportService
{
    Task<List<TrialBalanceItemDto>> GetTrialBalanceAsync(DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<AccountStatementDto> GetAccountStatementAsync(string accountCode, DateTime? from, DateTime? to, CancellationToken ct = default);
    Task<FinancialSummaryDto> GetFinancialSummaryAsync(CancellationToken ct = default);
    Task<FinancialStatsDto> GetFinancialStatsAsync(CancellationToken ct = default);
    Task<VatReturnDto> GetVatReturnAsync(DateTime from, DateTime to, CancellationToken ct = default);
    /// <summary>قائمة المركز المالي حتى تاريخ (فارغ = اليوم) من سطور القيود.</summary>
    Task<BalanceSheetDto> GetBalanceSheetAsync(DateTime? asOf, CancellationToken ct = default);
    // ---- نسخ DevExtreme (filter / sort / group / summary / paging) ----
    Task<LoadResult> LoadTrialBalanceAsync(DateTime? from, DateTime? to, DataSourceLoadOptions options, CancellationToken ct = default);
    /// <summary>حركات الحساب (الصفوف) مع تحميل DevExtreme؛ ملخص الرصيد يأتي من GetAccountStatementAsync.</summary>
    Task<LoadResult> LoadAccountStatementEntriesAsync(string accountCode, DateTime? from, DateTime? to, DataSourceLoadOptions options, CancellationToken ct = default);
    /// <summary>الأرصدة الحالية للحسابات (مدين/دائن حسب طبيعة الحساب)؛ الخيارات تُنفَّذ في قاعدة البيانات.</summary>
    Task<LoadResult> LoadAccountBalancesAsync(DataSourceLoadOptions options, CancellationToken ct = default);
    /// <summary>دفتر اليومية سطراً سطراً (طرف مدين/دائن مع بيانات قيده)؛ الخيارات تُنفَّذ في قاعدة البيانات.</summary>
    Task<LoadResult> LoadJournalLedgerAsync(DataSourceLoadOptions options, CancellationToken ct = default);
}
