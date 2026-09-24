using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.BuildingBlocks.Web;
using Erp.Catalog.Contracts;
using Erp.Modules.Organization.Application;
using Erp.Modules.Organization.Contracts;
using Erp.Modules.Organization.Domain;
using Erp.Modules.Organization.Persistence;
using Erp.SharedKernel.Errors;
using Erp.SharedKernel.Security;
using Erp.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Organization.Endpoints;

internal sealed record UpdateCompanyRequest(
    string NameAr,
    string NameEn,
    string VatNumber,
    string CrNumber,
    string Address,
    string City,
    string Country,
    string Phone,
    string Email,
    string? LogoUrl,
    string? Industry,
    DateOnly? FinancialYearStart,
    DateOnly? FinancialYearEnd);

internal sealed record BranchDto(
    Guid Id,
    string Code,
    string NameAr,
    string NameEn,
    BranchType Type,
    string? City,
    string? Address,
    string? Phone,
    bool IsActive,
    DateTimeOffset CreatedAt);

internal sealed record SaveBranchRequest(
    string? Code,
    string NameAr,
    string? NameEn,
    BranchType Type,
    string? City,
    string? Address,
    string? Phone,
    bool? IsActive);

internal sealed record UpgradeSubscriptionRequest(string PlanId, string? BillingCycle, string? PaymentMethod);

internal sealed record TenantMembershipDto(Guid Id, string Code, string NameAr, string NameEn, TenantStatus Status, bool IsCurrent);

internal static class OrganizationEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var company = app.MapGroup("/api/v1/company").WithTags("Organization").RequireAuthorization();
        company.MapGet(string.Empty, GetCompanyAsync);
        company.MapPut(string.Empty, UpdateCompanyAsync).RequireRoles(SystemRoles.Owner, SystemRoles.Admin);

        var branches = app.MapGroup("/api/v1/branches").WithTags("Organization");
        branches.MapGet(string.Empty, ListBranchesAsync).RequireScreen(ScreenIds.MasterData, ScreenAction.View);
        branches.MapGet("{id:guid}", GetBranchAsync).RequireScreen(ScreenIds.MasterData, ScreenAction.View);
        branches.MapPost(string.Empty, CreateBranchAsync).RequireScreen(ScreenIds.MasterData, ScreenAction.Create);
        branches.MapPut("{id:guid}", UpdateBranchAsync).RequireScreen(ScreenIds.MasterData, ScreenAction.Edit);
        branches.MapDelete("{id:guid}", DeleteBranchAsync).RequireScreen(ScreenIds.MasterData, ScreenAction.Delete);

        app.MapGet("/api/v1/tenants", ListMyTenantsAsync).WithTags("Organization").RequireAuthorization();

        var subscriptions = app.MapGroup("/api/v1/subscriptions").WithTags("Organization");
        subscriptions.MapGet("plans", GetPlansAsync).AllowAnonymous();
        subscriptions.MapGet("current", GetCurrentSubscriptionAsync).RequireAuthorization();
        subscriptions.MapPost("upgrade", UpgradeSubscriptionAsync).RequireRoles(SystemRoles.Owner, SystemRoles.Admin);
    }

    private static async Task<IResult> UpgradeSubscriptionAsync(UpgradeSubscriptionRequest request, ISubscriptionCatalog subscriptions, ITenantContext tenant, CancellationToken ct) =>
        ErpResults.Ok(
            await subscriptions.UpgradeAsync(tenant.TenantId, request.PlanId, request.BillingCycle ?? "monthly", request.PaymentMethod ?? "mada", ct),
            "تم ترقية الاشتراك بنجاح");

    private static async Task<IResult> GetCompanyAsync(ICompanyProfileReader reader, CancellationToken ct) =>
        ErpResults.Ok(await reader.GetCurrentAsync(ct) ?? throw ErpException.NotFound("Company profile", "ملف المنشأة"));

    private static async Task<IResult> UpdateCompanyAsync(
        UpdateCompanyRequest request,
        OrganizationDbContext db,
        IUnitOfWork unitOfWork,
        ITenantContext tenant,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NameAr))
        {
            throw ErpException.Validation("The Arabic company name is required.", "اسم المنشأة بالعربي مطلوب.");
        }

        var company = await db.Companies.SingleOrDefaultAsync(ct) ?? throw ErpException.NotFound("Company profile", "ملف المنشأة");
        company.Update(
            request.NameAr,
            request.NameEn,
            request.VatNumber,
            request.CrNumber,
            request.Address,
            request.City,
            request.Country,
            request.Phone,
            request.Email,
            request.LogoUrl,
            request.Industry,
            request.FinancialYearStart,
            request.FinancialYearEnd);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Ok(CompanyProfileReader.ToDto(company, tenant.TenantCode), "تم تحديث بيانات المنشأة");
    }

    private static async Task<IResult> ListBranchesAsync([AsParameters] PaginationParams paging, OrganizationDbContext db, CancellationToken ct)
    {
        var query = db.Branches.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(paging.SearchTerm))
        {
            var term = paging.SearchTerm.Trim();
            query = query.Where(b => b.Code.Contains(term) || b.NameAr.Contains(term) || b.NameEn.Contains(term));
        }

        var page = await query.OrderBy(b => b.Code).Select(b => ToDto(b)).ToPagedResultAsync(paging, ct);
        return ErpResults.Ok(page);
    }

    private static async Task<IResult> GetBranchAsync(Guid id, OrganizationDbContext db, CancellationToken ct)
    {
        var branch = await db.Branches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, ct) ?? throw BranchNotFound();
        return ErpResults.Ok(ToDto(branch));
    }

    private static async Task<IResult> CreateBranchAsync(SaveBranchRequest request, OrganizationDbContext db, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        Validate(request, requireCode: true);
        var code = request.Code!.Trim().ToUpperInvariant();
        if (await db.Branches.AnyAsync(b => b.Code == code, ct))
        {
            throw ErpException.Conflict("branch_code_taken", $"Branch code {code} already exists.", $"رمز الفرع {code} مستخدم مسبقاً.");
        }

        var branch = new Branch(code, request.NameAr, request.NameEn ?? string.Empty, request.Type);
        branch.Update(branch.NameAr, branch.NameEn, request.Type, request.City, request.Address, request.Phone, request.IsActive ?? true);
        db.Branches.Add(branch);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Created($"/api/v1/branches/{branch.Id}", ToDto(branch), "تم إضافة الفرع");
    }

    private static async Task<IResult> UpdateBranchAsync(Guid id, SaveBranchRequest request, OrganizationDbContext db, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        Validate(request, requireCode: false);
        var branch = await db.Branches.SingleOrDefaultAsync(b => b.Id == id, ct) ?? throw BranchNotFound();
        branch.Update(request.NameAr, request.NameEn ?? string.Empty, request.Type, request.City, request.Address, request.Phone, request.IsActive ?? branch.IsActive);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Ok(ToDto(branch), "تم تحديث الفرع");
    }

    private static async Task<IResult> DeleteBranchAsync(Guid id, OrganizationDbContext db, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var branch = await db.Branches.SingleOrDefaultAsync(b => b.Id == id, ct) ?? throw BranchNotFound();
        if (branch.Type == BranchType.HeadOffice && branch.Code == Branch.HeadOfficeCode)
        {
            throw ErpException.Conflict("head_office_required", "The head office cannot be deleted.", "لا يمكن حذف المركز الرئيسي.");
        }

        branch.Delete();
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Ok(ToDto(branch), "تم حذف الفرع");
    }

    /// <summary>Companies the signed-in person belongs to (same email in the catalog login index).</summary>
    private static async Task<IResult> ListMyTenantsAsync(ICurrentUser user, ITenantLoginIndex loginIndex, ITenantContext tenant, CancellationToken ct)
    {
        IReadOnlyList<LoginIndexMatch> matches = user.Email is { Length: > 0 } email
            ? await loginIndex.FindAsync(email, ct)
            : user.UserId is { } userId && await loginIndex.FindByUserAsync(userId, ct) is { } own ? [own] : [];

        return ErpResults.Ok(matches
            .Select(m => new TenantMembershipDto(m.TenantId, m.TenantCode, m.TenantNameAr, m.TenantNameEn, m.TenantStatus, m.TenantId == tenant.TenantId))
            .ToList());
    }

    private static async Task<IResult> GetPlansAsync(ISubscriptionCatalog subscriptions, CancellationToken ct) =>
        ErpResults.Ok(await subscriptions.GetPlansAsync(ct));

    private static async Task<IResult> GetCurrentSubscriptionAsync(ISubscriptionCatalog subscriptions, ITenantContext tenant, CancellationToken ct) =>
        ErpResults.Ok(await subscriptions.GetCurrentAsync(tenant.TenantId, ct)
            ?? throw ErpException.NotFound("Subscription", "الاشتراك"));

    private static BranchDto ToDto(Branch b) =>
        new(b.Id, b.Code, b.NameAr, b.NameEn, b.Type, b.City, b.Address, b.Phone, b.IsActive, b.CreatedAt);

    private static ErpException BranchNotFound() => ErpException.NotFound("Branch", "الفرع");

    private static void Validate(SaveBranchRequest request, bool requireCode)
    {
        var errors = new Dictionary<string, string[]>();
        if (requireCode && (string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 20))
        {
            errors["code"] = ["The branch code is required (max 20 characters)."];
        }

        if (string.IsNullOrWhiteSpace(request.NameAr))
        {
            errors["nameAr"] = ["The Arabic name is required."];
        }

        if (!Enum.IsDefined(request.Type))
        {
            errors["type"] = ["The branch type must be head_office, showroom or warehouse."];
        }

        if (errors.Count > 0)
        {
            throw ErpException.Validation("The branch is not valid.", "بيانات الفرع غير مكتملة أو غير صحيحة.", errors);
        }
    }
}
