using Erp.Modules.Organization.Contracts;
using Erp.SharedKernel.Domain;

namespace Erp.Modules.Organization.Domain;

/// <summary>org.Tenants: the company profile of one tenant (Id = TenantId = catalog.Tenants.Id).</summary>
internal sealed class CompanyProfile : TenantEntity
{
    private CompanyProfile()
    {
    }

    public CompanyProfile(Guid tenantId, string baseCurrencyCode)
    {
        Id = tenantId;
        BaseCurrencyCode = baseCurrencyCode;
        var year = DateTime.UtcNow.Year;
        FinancialYearStart = new DateOnly(year, 1, 1);
        FinancialYearEnd = new DateOnly(year, 12, 31);
    }

    public string NameAr { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    public string VatNumber { get; private set; } = string.Empty;

    public string CrNumber { get; private set; } = string.Empty;

    public string Address { get; private set; } = string.Empty;

    public string City { get; private set; } = string.Empty;

    public string Country { get; private set; } = string.Empty;

    public string Phone { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string BaseCurrencyCode { get; private set; } = "SAR";

    public string? LogoUrl { get; private set; }

    public string? Industry { get; private set; }

    public DateOnly FinancialYearStart { get; private set; }

    public DateOnly FinancialYearEnd { get; private set; }

    public void Update(
        string nameAr,
        string nameEn,
        string vatNumber,
        string crNumber,
        string address,
        string city,
        string country,
        string phone,
        string email,
        string? logoUrl,
        string? industry,
        DateOnly? financialYearStart,
        DateOnly? financialYearEnd)
    {
        NameAr = nameAr.Trim();
        NameEn = nameEn.Trim();
        VatNumber = vatNumber.Trim();
        CrNumber = crNumber.Trim();
        Address = address.Trim();
        City = city.Trim();
        Country = country.Trim();
        Phone = phone.Trim();
        Email = email.Trim();
        LogoUrl = logoUrl;
        Industry = industry;
        FinancialYearStart = financialYearStart ?? FinancialYearStart;
        FinancialYearEnd = financialYearEnd ?? FinancialYearEnd;
    }
}

/// <summary>org.Branches: head office, showrooms and warehouses (A2).</summary>
internal sealed class Branch : TenantEntity, ISoftDeletable
{
    private Branch()
    {
    }

    public Branch(string code, string nameAr, string nameEn, BranchType type)
    {
        Code = code.Trim().ToUpperInvariant();
        Rename(nameAr, nameEn);
        Type = type;
        IsActive = true;
    }

    public const string HeadOfficeCode = "HO";

    public string Code { get; private set; } = string.Empty;

    public string NameAr { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    public BranchType Type { get; private set; }

    public string? City { get; private set; }

    public string? Address { get; private set; }

    public string? Phone { get; private set; }

    public bool IsActive { get; private set; }

    public bool IsDeleted { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public void Update(string nameAr, string nameEn, BranchType type, string? city, string? address, string? phone, bool isActive)
    {
        Rename(nameAr, nameEn);
        Type = type;
        City = city?.Trim();
        Address = address?.Trim();
        Phone = phone?.Trim();
        IsActive = isActive;
    }

    public void Delete()
    {
        IsDeleted = true;
        IsActive = false;
    }

    private void Rename(string nameAr, string nameEn)
    {
        NameAr = nameAr.Trim();
        NameEn = string.IsNullOrWhiteSpace(nameEn) ? NameAr : nameEn.Trim();
    }
}
