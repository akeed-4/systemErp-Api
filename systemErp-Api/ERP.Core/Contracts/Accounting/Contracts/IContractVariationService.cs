using ERP.Core.DTOs.Accounting.Contracts;

namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// أوامر التغيير على عقود جدول الكميات السارية (عقد العميل وعقد مقاول الباطن): مسودة ← مقدَّم ← معتمد، أو إعادة/رفض/إلغاء.
/// الاعتماد وحده يغيّر كميات العقد المعدلة وبنوده وقيمته وتكلفته التقديرية ونهايته. الصلاحيات ونطاق السجل يفحصها المتحكم على العقد.
/// </summary>
public interface IContractVariationService
{
    /// <summary>موقف قيمة العقد وأوامر تغييره (الأحدث أولاً).</summary>
    Task<ContractVariationSummaryDto> SummaryAsync(Guid contractId, CancellationToken ct = default);
    Task<ContractVariationDto> GetAsync(Guid contractId, Guid variationId, CancellationToken ct = default);
    /// <summary>بالمعرّف وحده (لسياسة الاعتماد).</summary>
    Task<ContractVariationDto> GetAsync(Guid variationId, CancellationToken ct = default);
    /// <summary>العقد الذي يتبعه الأمر (لفحص الصلاحية والنطاق).</summary>
    Task<Guid> ContractIdOfAsync(Guid variationId, CancellationToken ct = default);

    Task<ContractVariationDto> CreateAsync(Guid contractId, SaveContractVariationDto request, CancellationToken ct = default);
    Task<ContractVariationDto> UpdateAsync(Guid contractId, Guid variationId, SaveContractVariationDto request, CancellationToken ct = default);
    Task DeleteAsync(Guid contractId, Guid variationId, CancellationToken ct = default);
    Task<ContractVariationDto> SubmitAsync(Guid contractId, Guid variationId, CancellationToken ct = default);
    /// <summary>الاعتماد بالمعرّف (يمر بسياسة الاعتماد إن وُجدت).</summary>
    Task<ContractVariationDto> ApproveAsync(Guid variationId, CancellationToken ct = default);
    Task<ContractVariationDto> ReturnAsync(Guid contractId, Guid variationId, ContractVariationActionDto request, CancellationToken ct = default);
    Task<ContractVariationDto> RejectAsync(Guid contractId, Guid variationId, ContractVariationActionDto request, CancellationToken ct = default);
    Task<ContractVariationDto> CancelAsync(Guid contractId, Guid variationId, ContractVariationActionDto request, CancellationToken ct = default);
}
