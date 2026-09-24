using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.BuildingBlocks.Web;
using Erp.Modules.Accounting.Application;
using Erp.Modules.Accounting.Contracts;
using Erp.Modules.Accounting.Domain;
using Erp.Modules.Accounting.Persistence;
using Erp.SharedKernel.Errors;
using Erp.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Accounting.Endpoints;

internal sealed record CostCenterDto(Guid Id, Guid TenantId, string Code, string NameAr, string NameEn, string? Description, bool IsActive);

internal sealed record SaveCostCenterRequest(string? Code, string NameAr, string? NameEn, string? Description, bool? IsActive);

internal sealed record PostingMappingDto(PostingPurpose Purpose, Guid AccountId, string AccountCode, string AccountNameAr);

internal sealed record SavePostingMappingsRequest(IReadOnlyList<PostingMappingItem> Mappings);

internal sealed record PostingMappingItem(PostingPurpose Purpose, Guid AccountId);

internal sealed record FiscalPeriodDto(Guid Id, int Year, int Month, DateOnly StartDate, DateOnly EndDate, bool IsClosed, DateTimeOffset? ClosedAt);

/// <summary>Cost centers, posting-account mapping (accounting settings) and fiscal periods.</summary>
internal static class AccountingSetupEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var costCenters = app.MapGroup("/api/v1/cost-centers").WithTags("Accounting");
        costCenters.MapGet(string.Empty, ListCostCentersAsync).RequireAuthorization();
        costCenters.MapPost(string.Empty, CreateCostCenterAsync).RequireScreen(ScreenIds.Accounts, ScreenAction.Create);
        costCenters.MapPut("{id:guid}", UpdateCostCenterAsync).RequireScreen(ScreenIds.Accounts, ScreenAction.Edit);
        costCenters.MapDelete("{id:guid}", DeleteCostCenterAsync).RequireScreen(ScreenIds.Accounts, ScreenAction.Delete);

        var mappings = app.MapGroup("/api/v1/accounting/posting-mappings").WithTags("Accounting");
        mappings.MapGet(string.Empty, ListMappingsAsync).RequireScreen(ScreenIds.Accounts, ScreenAction.View);
        mappings.MapPut(string.Empty, SaveMappingsAsync).RequireRoles(SystemRoles.Owner, SystemRoles.Admin, SystemRoles.ChiefAccountant);

        var periods = app.MapGroup("/api/v1/fiscal-periods").WithTags("Accounting");
        periods.MapGet(string.Empty, ListPeriodsAsync).RequireScreen(ScreenIds.Accounts, ScreenAction.View);
        periods.MapPost("{id:guid}/close", ClosePeriodAsync).RequireRoles(SystemRoles.Owner, SystemRoles.Admin, SystemRoles.ChiefAccountant);
        periods.MapPost("{id:guid}/reopen", ReopenPeriodAsync).RequireRoles(SystemRoles.Owner, SystemRoles.ChiefAccountant);
    }

    private static async Task<IResult> ListCostCentersAsync(AccountingDbContext db, CancellationToken ct) =>
        ErpResults.Ok(await db.CostCenters.AsNoTracking().OrderBy(c => c.Code).Select(c => ToDto(c)).ToListAsync(ct));

    private static async Task<IResult> CreateCostCenterAsync(SaveCostCenterRequest request, AccountingDbContext db, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.NameAr))
        {
            throw ErpException.Validation("Code and Arabic name are required.", "الرمز والاسم بالعربي مطلوبان.");
        }

        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.CostCenters.AnyAsync(c => c.Code == code, ct))
        {
            throw ErpException.Conflict("cost_center_code_taken", $"Cost center {code} already exists.", $"مركز التكلفة {code} موجود مسبقاً.");
        }

        var costCenter = new CostCenter(code, request.NameAr, request.NameEn ?? string.Empty, request.Description);
        db.CostCenters.Add(costCenter);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Created($"/api/v1/cost-centers/{costCenter.Id}", ToDto(costCenter), "تم إضافة مركز التكلفة");
    }

    private static async Task<IResult> UpdateCostCenterAsync(Guid id, SaveCostCenterRequest request, AccountingDbContext db, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var costCenter = await db.CostCenters.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw ErpException.NotFound("Cost center", "مركز التكلفة");
        costCenter.Update(request.NameAr, request.NameEn ?? string.Empty, request.Description, request.IsActive ?? costCenter.IsActive);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Ok(ToDto(costCenter), "تم تحديث مركز التكلفة");
    }

    private static async Task<IResult> DeleteCostCenterAsync(Guid id, AccountingDbContext db, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var costCenter = await db.CostCenters.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw ErpException.NotFound("Cost center", "مركز التكلفة");
        if (await db.JournalEntryLines.AnyAsync(l => l.CostCenterId == id, ct))
        {
            throw ErpException.Conflict("cost_center_used", "The cost center has postings; deactivate it instead.", "مركز التكلفة عليه حركات؛ يمكنك إيقافه بدلاً من حذفه.");
        }

        db.CostCenters.Remove(costCenter);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Ok(new { id }, "تم حذف مركز التكلفة");
    }

    private static async Task<IResult> ListMappingsAsync(AccountingDbContext db, CancellationToken ct) =>
        ErpResults.Ok(await (
            from m in db.PostingMappings.AsNoTracking()
            join a in db.Accounts.AsNoTracking() on m.AccountId equals a.Id
            orderby m.Purpose
            select new PostingMappingDto(m.Purpose, a.Id, a.Code, a.NameAr)).ToListAsync(ct));

    private static async Task<IResult> SaveMappingsAsync(SavePostingMappingsRequest request, AccountingDbContext db, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var accountIds = request.Mappings.Select(m => m.AccountId).Distinct().ToList();
        var accounts = await db.Accounts.AsNoTracking().Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        foreach (var item in request.Mappings)
        {
            if (!accounts.TryGetValue(item.AccountId, out var account) || !account.IsActive)
            {
                throw ErpException.Validation($"Unknown or inactive account for {item.Purpose}.", "الحساب المختار غير موجود أو غير نشط.");
            }

            var mapping = await db.PostingMappings.SingleOrDefaultAsync(m => m.Purpose == item.Purpose, ct);
            if (mapping is null)
            {
                db.PostingMappings.Add(new PostingAccountMapping(item.Purpose, item.AccountId));
            }
            else
            {
                mapping.Remap(item.AccountId);
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
        return await ListMappingsAsync(db, ct);
    }

    private static async Task<IResult> ListPeriodsAsync(int? year, AccountingDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var y = year ?? clock.GetUtcNow().Year;
        return ErpResults.Ok(await db.FiscalPeriods.AsNoTracking().Where(p => p.Year == y).OrderBy(p => p.Month)
            .Select(p => new FiscalPeriodDto(p.Id, p.Year, p.Month, p.StartDate, p.EndDate, p.IsClosed, p.ClosedAt)).ToListAsync(ct));
    }

    private static async Task<IResult> ClosePeriodAsync(Guid id, AccountingDbContext db, IUnitOfWork unitOfWork, ICurrentUser user, TimeProvider clock, CancellationToken ct)
    {
        var period = await db.FiscalPeriods.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw ErpException.NotFound("Period", "الفترة");
        if (await db.JournalEntries.AnyAsync(e => e.Status == JournalEntryStatus.Draft && e.Date >= period.StartDate && e.Date <= period.EndDate, ct))
        {
            throw ErpException.Conflict("period_has_drafts", "Post or delete the draft entries of this period first.", "يرجى ترحيل أو حذف القيود المسودة في هذه الفترة أولاً.");
        }

        period.Close(clock.GetUtcNow(), user.UserId);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Ok(new FiscalPeriodDto(period.Id, period.Year, period.Month, period.StartDate, period.EndDate, period.IsClosed, period.ClosedAt), "تم إقفال الفترة");
    }

    private static async Task<IResult> ReopenPeriodAsync(Guid id, AccountingDbContext db, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var period = await db.FiscalPeriods.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw ErpException.NotFound("Period", "الفترة");
        period.Reopen();
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Ok(new FiscalPeriodDto(period.Id, period.Year, period.Month, period.StartDate, period.EndDate, period.IsClosed, period.ClosedAt), "تم إعادة فتح الفترة");
    }

    private static CostCenterDto ToDto(CostCenter c) => new(c.Id, c.TenantId, c.Code, c.NameAr, c.NameEn, c.Description, c.IsActive);
}
