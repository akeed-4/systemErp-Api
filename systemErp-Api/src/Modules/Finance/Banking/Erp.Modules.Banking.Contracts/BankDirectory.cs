namespace Erp.Modules.Banking.Contracts;

/// <summary>A bank or finance company (institution). Financing institutions appear in car sale/purchase financing.</summary>
public sealed record BankSummary(Guid Id, string Code, string NameAr, string NameEn, string? SwiftCode, bool IsFinancingInstitution, bool IsActive);

/// <summary>A company bank account; <see cref="AccountId"/> is its GL sub-account under 111.</summary>
public sealed record BankAccountSummary(Guid Id, string Code, string NameAr, string NameEn, Guid BankId, string? Iban, string CurrencyCode, Guid? AccountId, bool IsActive);

public interface IBankDirectory
{
    Task<BankSummary?> FindBankAsync(Guid bankId, CancellationToken cancellationToken);

    Task<BankAccountSummary?> FindBankAccountAsync(Guid bankAccountId, CancellationToken cancellationToken);
}
