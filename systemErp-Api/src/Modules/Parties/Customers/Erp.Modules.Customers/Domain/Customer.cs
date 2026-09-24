using Erp.Modules.Customers.Contracts;
using Erp.SharedKernel.Domain;

namespace Erp.Modules.Customers.Domain;

/// <summary>
/// The single customer entity shared by general sales, POS, car sales and contracts. Car buyers are customers too
/// (CustomerType + NationalId + IdExpiryDate come from the car-sales buyer fields).
/// </summary>
internal sealed class Customer : TenantEntity, ISoftDeletable
{
    private Customer()
    {
    }

    public Customer(string code) => Code = code.Trim().ToUpperInvariant();

    public string Code { get; private set; } = string.Empty;

    public CustomerType Type { get; private set; }

    public string NameAr { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    public string? VatNumber { get; private set; }

    public string? CrNumber { get; private set; }

    public string? NationalId { get; private set; }

    public DateOnly? IdExpiryDate { get; private set; }

    public string? Phone { get; private set; }

    public string? AltPhone { get; private set; }

    public string? Email { get; private set; }

    public string? ContactPerson { get; private set; }

    // Saudi national address (needed on B2B tax invoices).
    public string? City { get; private set; }

    public string? District { get; private set; }

    public string? Street { get; private set; }

    public string? BuildingNo { get; private set; }

    public string? PostalCode { get; private set; }

    public string? AdditionalNo { get; private set; }

    public decimal CreditLimit { get; private set; }

    public int CreditPeriodDays { get; private set; }

    public decimal OpeningBalance { get; private set; }

    /// <summary>GL sub-account (accounting.Accounts) under 112.</summary>
    public Guid? AccountId { get; private set; }

    public string CurrencyCode { get; private set; } = "SAR";

    public bool IsActive { get; private set; } = true;

    public string? Notes { get; private set; }

    public bool IsDeleted { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public void Update(CustomerData data)
    {
        Type = data.Type;
        NameAr = data.NameAr.Trim();
        NameEn = string.IsNullOrWhiteSpace(data.NameEn) ? NameAr : data.NameEn.Trim();
        VatNumber = Clean(data.VatNumber);
        CrNumber = Clean(data.CrNumber);
        NationalId = Clean(data.NationalId);
        IdExpiryDate = data.IdExpiryDate;
        Phone = Clean(data.Phone);
        AltPhone = Clean(data.AltPhone);
        Email = Clean(data.Email);
        ContactPerson = Clean(data.ContactPerson);
        City = Clean(data.City);
        District = Clean(data.District);
        Street = Clean(data.Street);
        BuildingNo = Clean(data.BuildingNo);
        PostalCode = Clean(data.PostalCode);
        AdditionalNo = Clean(data.AdditionalNo);
        CreditLimit = data.CreditLimit;
        CreditPeriodDays = data.CreditPeriodDays;
        OpeningBalance = data.OpeningBalance;
        CurrencyCode = data.CurrencyCode.Trim().ToUpperInvariant();
        IsActive = data.IsActive;
        Notes = data.Notes;
    }

    public void LinkAccount(Guid accountId) => AccountId = accountId;

    public void Delete()
    {
        IsDeleted = true;
        IsActive = false;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed record CustomerData(
    CustomerType Type,
    string NameAr,
    string? NameEn,
    string? VatNumber,
    string? CrNumber,
    string? NationalId,
    DateOnly? IdExpiryDate,
    string? Phone,
    string? AltPhone,
    string? Email,
    string? ContactPerson,
    string? City,
    string? District,
    string? Street,
    string? BuildingNo,
    string? PostalCode,
    string? AdditionalNo,
    decimal CreditLimit,
    int CreditPeriodDays,
    decimal OpeningBalance,
    string CurrencyCode,
    bool IsActive,
    string? Notes);
