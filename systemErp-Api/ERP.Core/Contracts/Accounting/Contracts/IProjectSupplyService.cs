using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// توريد مواد المشروع: المتابعة (المطلوب في دراسة الجدوى، المطلوب شراؤه، المشترى، المسلَّم، المتبقي)، ومستندات صرف المواد للمشروع،
/// وحدود طلبات الشراء المرتبطة بالمشروع.
/// </summary>
public interface IProjectSupplyService
{
    /// <param name="exceptRequisitionId">طلب شراء يُعدَّل: لا تُحسب كمياته في «ما طُلب سابقاً».</param>
    Task<ProjectMaterialTrackingDto> TrackingAsync(Guid contractId, Guid? warehouseId = null, Guid? exceptRequisitionId = null, CancellationToken ct = default);
    Task<List<ProjectMaterialIssueDocDto>> ListIssuesAsync(Guid contractId, CancellationToken ct = default);
    /// <summary>
    /// أذونات التسليم لكل المشاريع (شاشة المستندات المستقلة): ترقيم وفلترة وفرز في SQL، والعقود من استعلام عام فيسري عليها نطاق
    /// وصول المستخدم (المشرف يرى أذونات مشاريعه).
    /// </summary>
    Task<DevExtreme.AspNet.Data.ResponseModel.LoadResult> LoadIssuesAsync(DataSourceLoadOptions options, CancellationToken ct = default);
    Task<ProjectMaterialIssueDocDto> GetIssueAsync(Guid contractId, Guid id, CancellationToken ct = default);
    Task<ProjectMaterialIssueDocDto> CreateIssueAsync(Guid contractId, SaveProjectMaterialIssueDto request, CancellationToken ct = default);
    Task<ProjectMaterialIssueDocDto> UpdateIssueAsync(Guid contractId, Guid id, SaveProjectMaterialIssueDto request, CancellationToken ct = default);
    Task DeleteIssueAsync(Guid contractId, Guid id, CancellationToken ct = default);
    Task<ProjectMaterialIssueDocDto> PostIssueAsync(Guid contractId, Guid id, CancellationToken ct = default);
    Task<ProjectMaterialIssueDocDto> CancelIssueAsync(Guid contractId, Guid id, string reason, CancellationToken ct = default);
    /// <summary>مواد كل بند في جدول الكميات لكل صنف: المخطط، والمسلَّم وتكلفته الفعلية، والمختار في المستخلصات والمفوتر، والمتبقي تحت التنفيذ.</summary>
    Task<List<ProjectBoqMaterialLineDto>> BoqMaterialsAsync(Guid contractId, CancellationToken ct = default);

    /// <summary>
    /// حدود طلب شراء مرتبط بالمشروع: الاحتياجات معتمدة، وكل صنف ضمنها، ولا تتجاوز كميته المطلوب ناقص ما طُلب في طلبات المشروع الأخرى.
    /// </summary>
    Task EnsureRequisitionWithinPlanAsync(Guid contractId, Guid? requisitionId, IReadOnlyList<(Guid ItemId, decimal Quantity)> lines, CancellationToken ct = default);

    // ---------- عهدة سند استلام المشروع (يستدعيها سند الاستلام داخل معاملته) ----------
    /// <summary>
    /// عهدة المشروع من سند استلام مشترياته (بلا مستودع عام ولا متوسط تكلفة الصنف): أسطرها بسعر السند على بنود جدول الكميات، وقيدها
    /// مدين المواد تحت التنفيذ بمركز المشروع / دائن بضاعة مستلمة لم تُفوتر. يعيد معرّف العهدة.
    /// </summary>
    Task<Guid> PostPurchaseReceiptAsync(DeliveryNote note, CancellationToken ct = default);
    /// <summary>إرجاع للمورد من عهدة السند (غير المختار في مستخلص ولا المسحوب): قيد مدين بضاعة مستلمة لم تُفوتر / دائن المواد تحت التنفيذ.</summary>
    Task<Guid> PostReceiptReturnAsync(DeliveryNote note, DeliveryReturnNote returnNote, CancellationToken ct = default);
    /// <summary>عكس عهدة السند أو إرجاعها عند حذفه (ما لم تُستخدم في مستخلص أو سحب).</summary>
    Task CancelReceiptCustodyAsync(Guid contractId, Guid custodyIssueId, string reason, CancellationToken ct = default);
}
