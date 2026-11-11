using ERP.Core.DTOs.Accounting.Contracts;

namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// استحقاق إيجار المعدات المستأجرة من التقارير اليومية المعتمدة وتسويته بفواتير المؤجر، داخل معاملة المستدعي:
/// <list type="bullet">
/// <item>اعتماد التقرير (وإعادة اعتماده): قيد الاستحقاق أو فرقه فقط عن المرحّل للمرّة نفسها (لا يتكرر).</item>
/// <item>ترحيل فاتورة المؤجر أو مرتجعها أو تعديلها أو حذفها: إعادة موازنة المسوّى لكل مؤجر ومركز تكلفة =
/// أقل (صافي فواتيره المرحّلة على المركز، المستحق غير المقفل)، ويُرحَّل الفرق عن المسوّى فقط.</item>
/// </list>
/// الفاتورة نفسها تبقى فاتورة ضريبية كما هي؛ قيود الاستحقاق والتسوية والإقفال بلا ضريبة.
/// </summary>
public interface IRentalAccrualService
{
    /// <summary>استحقاق التقرير أو فرقه للمرّة <paramref name="revision"/>، ثم موازنة تسوية مؤجريه.</summary>
    Task PostReportAsync(SiteDailyReport report, CommercialContract contract, int revision, CancellationToken ct = default);

    /// <summary>بعد ترحيل مستند شراء (فاتورة أو مرتجع) أو عكس قيده عند حذفه: موازنة تسوية مؤجره على مراكز المشاريع.</summary>
    Task OnPurchaseDocumentChangedAsync(InvoiceBase document, CancellationToken ct = default);

    Task<List<SiteRentalAccrualDto>> ListAsync(Guid contractId, bool openOnly, CancellationToken ct = default);

    /// <summary>إقفال المتبقي من استحقاق مؤجر في المشروع حتى تاريخ؛ يُرجع المبلغ المقفل.</summary>
    Task<decimal> CloseAsync(Guid contractId, CloseRentalAccrualDto request, CancellationToken ct = default);
}
