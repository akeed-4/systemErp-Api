using Erp.SharedKernel.Domain;

namespace Erp.Modules.Banking.Domain;

/// <summary>banking.Banks: an institution (bank or finance company), not a company account.</summary>
internal sealed class Bank : TenantEntity
{
    private Bank()
    {
    }

    public Bank(string code, string nameAr, string nameEn, string? swiftCode, bool isFinancingInstitution)
    {
        Code = code.Trim().ToUpperInvariant();
        Update(nameAr, nameEn, swiftCode, isFinancingInstitution, true);
    }

    public string Code { get; private set; } = string.Empty;

    public string NameAr { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    public string? SwiftCode { get; private set; }

    public bool IsFinancingInstitution { get; private set; }

    public bool IsActive { get; private set; }

    public void Update(string nameAr, string nameEn, string? swiftCode, bool isFinancingInstitution, bool isActive)
    {
        NameAr = nameAr.Trim();
        NameEn = string.IsNullOrWhiteSpace(nameEn) ? NameAr : nameEn.Trim();
        SwiftCode = string.IsNullOrWhiteSpace(swiftCode) ? null : swiftCode.Trim().ToUpperInvariant();
        IsFinancingInstitution = isFinancingInstitution;
        IsActive = isActive;
    }
}

/// <summary>banking.BankAccounts: a company account at a bank, with its own GL sub-account (the frontend's BankEntity).</summary>
internal sealed class BankAccount : TenantEntity, ISoftDeletable
{
    private BankAccount()
    {
    }

    public BankAccount(string code, Guid bankId)
    {
        Code = code.Trim().ToUpperInvariant();
        BankId = bankId;
        IsActive = true;
    }

    public string Code { get; private set; } = string.Empty;

    public Guid BankId { get; private set; }

    public string NameAr { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    public string AccountNumber { get; private set; } = string.Empty;

    public string? Iban { get; private set; }

    public string? Branch { get; private set; }

    public string CurrencyCode { get; private set; } = "SAR";

    public decimal OpeningBalance { get; private set; }

    /// <summary>GL sub-account (accounting.Accounts) under 111.</summary>
    public Guid? AccountId { get; private set; }

    public bool IsActive { get; private set; }

    public string? Notes { get; private set; }

    public bool IsDeleted { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public void Update(Guid bankId, string nameAr, string nameEn, string accountNumber, string? iban, string? branch, string currencyCode, decimal openingBalance, bool isActive, string? notes)
    {
        BankId = bankId;
        NameAr = nameAr.Trim();
        NameEn = string.IsNullOrWhiteSpace(nameEn) ? NameAr : nameEn.Trim();
        AccountNumber = accountNumber.Trim();
        Iban = string.IsNullOrWhiteSpace(iban) ? null : iban.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        Branch = branch;
        CurrencyCode = currencyCode.Trim().ToUpperInvariant();
        OpeningBalance = openingBalance;
        IsActive = isActive;
        Notes = notes;
    }

    public void LinkAccount(Guid accountId) => AccountId = accountId;

    public void Delete()
    {
        IsDeleted = true;
        IsActive = false;
    }
}
