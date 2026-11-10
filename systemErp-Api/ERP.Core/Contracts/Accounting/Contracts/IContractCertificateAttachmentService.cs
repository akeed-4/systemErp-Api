using ERP.Core.DTOs.Accounting.Contracts;

namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// أرشفة المستندات المؤيدة لكل بند في المستخلص (فواتير مواد، محاضر إنجاز، تقارير فحص...). مستقلة عن دورة المستخلص وقيوده:
/// الإضافة لمن يُعدّ المستخلص (الاستلام) في أي حالة غير الإلغاء، والحذف في المسودة لمن يُعدّه وبعدها بصلاحية الحذف.
/// </summary>
public interface IContractCertificateAttachmentService
{
    Task<CertificateAttachmentSummaryDto> SummaryAsync(Guid contractId, Guid certificateId, CancellationToken ct = default);
    Task<List<CertificateItemAttachmentDto>> ListAsync(Guid contractId, Guid certificateId, Guid? contractItemId, CancellationToken ct = default);
    Task<CertificateItemAttachmentDto> AddAsync(Guid contractId, Guid certificateId, Guid contractItemId, string? category, string? note,
        string fileName, byte[] content, CancellationToken ct = default);
    Task<ContractFileDto> GetFileAsync(Guid contractId, Guid certificateId, Guid attachmentId, CancellationToken ct = default);
    Task DeleteAsync(Guid contractId, Guid certificateId, Guid attachmentId, CancellationToken ct = default);
}
