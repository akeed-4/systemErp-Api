using Erp.BuildingBlocks.Infrastructure.Modules;
using Erp.Modules.Organization.Contracts;
using Erp.Modules.Organization.Domain;
using Erp.Modules.Organization.Persistence;
using Erp.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Organization.Application;

internal sealed class OrganizationSeeder(OrganizationDbContext db) : IModuleSeeder
{
    public int Order => 10;

    public async Task SeedTenantAsync(TenantSeedContext context, CancellationToken cancellationToken)
    {
        if (!await db.Companies.AnyAsync(cancellationToken))
        {
            var company = new CompanyProfile(context.TenantId, context.BaseCurrencyCode);
            var seed = context.Company;
            company.Update(
                seed.NameAr,
                seed.NameEn,
                seed.VatNumber,
                seed.CrNumber,
                seed.Address,
                seed.City,
                seed.Country,
                seed.Phone,
                seed.Email,
                logoUrl: null,
                seed.Industry,
                financialYearStart: null,
                financialYearEnd: null);
            db.Companies.Add(company);
        }

        if (!await db.Branches.AnyAsync(b => b.Code == Branch.HeadOfficeCode, cancellationToken))
        {
            var headOffice = new Branch(Branch.HeadOfficeCode, "المركز الرئيسي", "Head Office", BranchType.HeadOffice);
            headOffice.Update(headOffice.NameAr, headOffice.NameEn, BranchType.HeadOffice, context.Company.City, context.Company.Address, context.Company.Phone, isActive: true);
            db.Branches.Add(headOffice);
        }
    }
}

internal sealed class BranchDirectory(OrganizationDbContext db) : IBranchDirectory
{
    public Task<BranchSummary?> FindAsync(Guid branchId, CancellationToken cancellationToken) =>
        db.Branches.AsNoTracking().Where(b => b.Id == branchId)
            .Select(b => new BranchSummary(b.Id, b.Code, b.NameAr, b.NameEn, b.Type, b.IsActive))
            .SingleOrDefaultAsync(cancellationToken);
}

internal sealed class CompanyProfileReader(OrganizationDbContext db, ITenantContext tenant) : ICompanyProfileReader
{
    public async Task<CompanyProfileDto?> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var company = await db.Companies.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        return company is null ? null : ToDto(company, tenant.TenantCode);
    }

    public static CompanyProfileDto ToDto(CompanyProfile c, string code) =>
        new(
            c.Id,
            code,
            c.NameAr,
            c.NameEn,
            c.VatNumber,
            c.VatNumber,
            c.CrNumber,
            c.Address,
            c.City,
            c.Country,
            c.Phone,
            c.Email,
            c.BaseCurrencyCode,
            c.LogoUrl,
            c.Industry,
            c.FinancialYearStart,
            c.FinancialYearEnd);
}
