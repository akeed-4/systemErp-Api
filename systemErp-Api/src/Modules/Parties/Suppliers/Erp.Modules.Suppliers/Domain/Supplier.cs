using Erp.SharedKernel.Domain;

namespace Erp.Modules.Suppliers.Domain;

/// <summary>The single supplier entity for general purchases, car purchases (car agents are suppliers) and contracts.</summary>
internal sealed class Supplier : TenantEntity, ISoftDeletable
{
    private readonly List<SupplierBankDetail> _bankDetails = [];

    private Supplier()
    {
    }

    public Supplier(string code) => Code = code.Trim().ToUpperInvariant();

    public string Code { get; private set; } = string.Empty;

    public string NameAr { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    public string? VatNumber { get; private set; }

    public string? CrNumber { get; private set; }

    public string? Phone { get; private set; }

    public string? Email { get; private set; }

    public string? ContactPerson { get; private set; }

    public string? City { get; private set; }

    public string? Address { get; private set; }

    public int PaymentTermsDays { get; private set; }

    public decimal OpeningBalance { get; private set; }

    /// <summary>GL sub-account (accounting.Accounts) under 211.</summary>
    public Guid? AccountId { get; private set; }

    public string CurrencyCode { get; private set; } = "SAR";

    public bool IsActive { get; private set; } = true;

    public string? Notes { get; private set; }

    public bool IsDeleted { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyList<SupplierBankDetail> BankDetails => _bankDetails;

    public SupplierBankDetail? PrimaryBank => _bankDetails.FirstOrDefault(b => b.IsPrimary) ?? _bankDetails.FirstOrDefault();

    public void Update(SupplierData data)
    {
        NameAr = data.NameAr.Trim();
        NameEn = string.IsNullOrWhiteSpace(data.NameEn) ? NameAr : data.NameEn.Trim();
        VatNumber = Clean(data.VatNumber);
        CrNumber = Clean(data.CrNumber);
        Phone = Clean(data.Phone);
        Email = Clean(data.Email);
        ContactPerson = Clean(data.ContactPerson);
        City = Clean(data.City);
        Address = Clean(data.Address);
        PaymentTermsDays = data.PaymentTermsDays;
        OpeningBalance = data.OpeningBalance;
        CurrencyCode = data.CurrencyCode.Trim().ToUpperInvariant();
        IsActive = data.IsActive;
        Notes = data.Notes;
    }

    /// <summary>The frontend edits one bank (bankName/iban/swiftCode); it is kept as the primary bank detail.</summary>
    public SupplierBankDetail? SetPrimaryBank(Guid? bankId, string? bankName, string? iban, string? swiftCode)
    {
        if (bankId is null && string.IsNullOrWhiteSpace(bankName) && string.IsNullOrWhiteSpace(iban))
        {
            return null;
        }

        var primary = PrimaryBank;
        if (primary is null)
        {
            primary = new SupplierBankDetail(Id);
            _bankDetails.Add(primary);
        }

        primary.Update(bankId, bankName, iban, swiftCode);
        return primary;
    }

    public void LinkAccount(Guid accountId) => AccountId = accountId;

    public void Delete()
    {
        IsDeleted = true;
        IsActive = false;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class SupplierBankDetail : TenantEntity
{
    private SupplierBankDetail()
    {
    }

    public SupplierBankDetail(Guid supplierId)
    {
        SupplierId = supplierId;
        IsPrimary = true;
    }

    public Guid SupplierId { get; private set; }

    /// <summary>Optional link to banking.Banks (the institution).</summary>
    public Guid? BankId { get; private set; }

    public string? BankName { get; private set; }

    public string? Iban { get; private set; }

    public string? SwiftCode { get; private set; }

    public bool IsPrimary { get; private set; }

    public void Update(Guid? bankId, string? bankName, string? iban, string? swiftCode)
    {
        BankId = bankId;
        BankName = string.IsNullOrWhiteSpace(bankName) ? null : bankName.Trim();
        Iban = string.IsNullOrWhiteSpace(iban) ? null : iban.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        SwiftCode = string.IsNullOrWhiteSpace(swiftCode) ? null : swiftCode.Trim().ToUpperInvariant();
    }
}

internal sealed record SupplierData(
    string NameAr,
    string? NameEn,
    string? VatNumber,
    string? CrNumber,
    string? Phone,
    string? Email,
    string? ContactPerson,
    string? City,
    string? Address,
    int PaymentTermsDays,
    decimal OpeningBalance,
    string CurrencyCode,
    bool IsActive,
    string? Notes);
