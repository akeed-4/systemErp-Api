namespace Erp.Modules.Organization.Contracts;

/// <summary>The frontend Tenant shape (company profile of the current tenant).</summary>
public sealed record CompanyProfileDto(
    Guid Id,
    string Code,
    string NameAr,
    string NameEn,
    string VatNumber,
    string TaxNumber,
    string CrNumber,
    string Address,
    string City,
    string Country,
    string Phone,
    string Email,
    string Currency,
    string? LogoUrl,
    string? Industry,
    DateOnly FinancialYearStart,
    DateOnly FinancialYearEnd);

public interface ICompanyProfileReader
{
    /// <summary>The profile of the tenant bound to the current scope, or null before it is seeded.</summary>
    Task<CompanyProfileDto?> GetCurrentAsync(CancellationToken cancellationToken);
}

public enum BranchType
{
    HeadOffice,
    Showroom,
    Warehouse,
}

public sealed record BranchSummary(Guid Id, string Code, string NameAr, string NameEn, BranchType Type, bool IsActive);

/// <summary>Branch lookup for modules that reference a branch (warehouses, POS terminals, vehicles, contracts).</summary>
public interface IBranchDirectory
{
    Task<BranchSummary?> FindAsync(Guid branchId, CancellationToken cancellationToken);
}
