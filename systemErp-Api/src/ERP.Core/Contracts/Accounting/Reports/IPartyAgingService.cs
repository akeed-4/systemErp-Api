using ERP.Core.DTOs.Accounting;

namespace ERP.Core.Contracts.Accounting;

/// <summary>أعمار الديون: المتبقي على الفواتير الآجلة لكل عميل/مورد موزّعاً على شرائح عمر الفاتورة، والدفعات غير الموزّعة.</summary>
public interface IPartyAgingService
{
    /// <param name="receivable">true = ذمم العملاء (فواتير المبيعات)، false = ذمم الموردين (فواتير المشتريات).</param>
    Task<AgingReportDto> GetAsync(bool receivable, DateTime? asOf, CancellationToken ct = default);
    /// <summary>الفواتير الآجلة المرحّلة التي عليها متبقٍّ لطرف بحساب الأستاذ (لاختيارها عند السداد).</summary>
    Task<List<OpenInvoiceDto>> OpenInvoicesAsync(string partyAccountCode, CancellationToken ct = default);
}
