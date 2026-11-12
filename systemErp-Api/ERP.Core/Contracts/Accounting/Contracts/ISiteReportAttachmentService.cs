using ERP.Core.DTOs.Accounting.Contracts;

namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// أرشفة المستندات المؤيدة لكل بند في التقرير اليومي (صور الموقع، محاضر الفحص، فواتير المواد...)، كأرشيف بنود المستخلص.
/// مستقلة عن دورة التقرير وتكلفته: الإضافة لمن يُعدّ التقارير في أي حالة، والحذف في المسودة لمن يُعدّها وبعدها بصلاحية الحذف.
/// </summary>
public interface ISiteReportAttachmentService
{
    Task<SiteReportAttachmentSummaryDto> SummaryAsync(Guid contractId, Guid reportId, CancellationToken ct = default);
    Task<List<SiteReportItemAttachmentDto>> ListAsync(Guid contractId, Guid reportId, Guid? contractItemId, CancellationToken ct = default);
    Task<SiteReportItemAttachmentDto> AddAsync(Guid contractId, Guid reportId, Guid contractItemId, string? category, string? note,
        string fileName, byte[] content, CancellationToken ct = default);
    Task<ContractFileDto> GetFileAsync(Guid contractId, Guid reportId, Guid attachmentId, CancellationToken ct = default);
    Task DeleteAsync(Guid contractId, Guid reportId, Guid attachmentId, CancellationToken ct = default);
}
