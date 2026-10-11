using ERP.Core.DTOs.Accounting;

namespace ERP.Core.Contracts.Accounting;

/// <summary>التسوية البنكية: مطابقة حركات حساب نقدي/بنكي مع كشف البنك واعتمادها حين يتطابق الرصيدان.</summary>
public interface IBankReconciliationService
{
    Task<BankReconciliationWorksheetDto> GetWorksheetAsync(string accountCode, DateTime statementDate, CancellationToken ct = default);
    Task<BankReconciliationDto> CreateAsync(CreateBankReconciliationDto request, CancellationToken ct = default);
    Task<List<BankReconciliationDto>> ListAsync(string? accountCode, CancellationToken ct = default);
    /// <summary>يلغي آخر تسوية للحساب فقط (تعود حركاتها غير مطابَقة).</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
