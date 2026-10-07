using ERP.Core.DTOs.Accounting;

namespace ERP.Core.Contracts.Accounting;

/// <summary>استبعاد الأصل الثابت (بيع أو شطب): قيد يخرجه من الدفاتر بتكلفته ومجمع إهلاكه ويثبت ربح أو خسارة الاستبعاد.</summary>
public interface IFixedAssetDisposalService
{
    Task<FixedAssetDto> DisposeAsync(Guid assetId, DisposeFixedAssetDto request, CancellationToken ct = default);
}
