using ERP.Core.DTOs.Accounting.Contracts;

namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// مستخلصات عقود جدول الكميات (فوترة boq_certificate): القياس السابق/الحالي/التراكمي لكل بند، ودورة المستخلص
/// (مسودة ← مقدَّم ← معتمد، أو إعادة/إلغاء). الكميات السابقة والحدود والقيم يحسبها الخادم وحده.
/// </summary>
public interface IContractCertificateService
{
    Task<List<ContractCertificateDto>> ListAsync(Guid contractId, CancellationToken ct = default);
    Task<ContractCertificateDto> GetAsync(Guid contractId, Guid certificateId, CancellationToken ct = default);
    /// <summary>قالب المستخلص التالي (لا يُحفظ): كل بنود جدول الكميات بكمياتها السابقة المعتمدة، والحالية صفر.</summary>
    Task<ContractCertificateDto> TemplateAsync(Guid contractId, CancellationToken ct = default);
    Task<BoqProgressDto> ProgressAsync(Guid contractId, CancellationToken ct = default);

    Task<ContractCertificateDto> CreateAsync(Guid contractId, SaveContractCertificateDto request, CancellationToken ct = default);
    Task<ContractCertificateDto> UpdateAsync(Guid contractId, Guid certificateId, SaveContractCertificateDto request, CancellationToken ct = default);
    Task<ContractCertificateDto> SubmitAsync(Guid contractId, Guid certificateId, CancellationToken ct = default);
    Task<ContractCertificateDto> ApproveAsync(Guid contractId, Guid certificateId, CancellationToken ct = default);
    /// <summary>إعادة المقدَّم للمسودة بسبب إلزامي.</summary>
    Task<ContractCertificateDto> ReturnAsync(Guid contractId, Guid certificateId, ContractCertificateActionDto request, CancellationToken ct = default);
    /// <summary>إلغاء مسودة أو مقدَّم بسبب إلزامي (المعتمد لا يُلغى؛ يُصحَّح لاحقاً بإشعار دائن).</summary>
    Task<ContractCertificateDto> CancelAsync(Guid contractId, Guid certificateId, ContractCertificateActionDto request, CancellationToken ct = default);
    /// <summary>أسطر أذونات تسليم المواد المرحّلة المؤهَّلة للاختيار في المستخلص (بالمتبقي والمختار في <paramref name="certificateId"/>).</summary>
    Task<List<CertificateMaterialOptionDto>> MaterialOptionsAsync(Guid contractId, Guid? certificateId, CancellationToken ct = default);
}
