using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.BuildingBlocks.Web;
using Erp.Modules.Banking.Application;
using Erp.Modules.Banking.Domain;
using Erp.Modules.Banking.Persistence;
using Erp.SharedKernel.Errors;
using Erp.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Banking.Endpoints;

internal sealed record BankDto(Guid Id, string Code, string NameAr, string NameEn, string? SwiftCode, bool IsFinancingInstitution, bool IsActive);

internal sealed record SaveBankRequest(string? Code, string NameAr, string? NameEn, string? SwiftCode, bool? IsFinancingInstitution, bool? IsActive);

internal sealed record EnsureAccountRequest(string? AccountCode);

/// <summary>banks (institutions) and bank-accounts (the frontend "banks" screen) + POST bank-accounts/{id}/account.</summary>
internal static class BankingEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var banks = app.MapGroup("/api/v1/banks").WithTags("Banking");
        banks.MapGet(string.Empty, ListBanksAsync).RequireAuthorization();
        banks.MapPost(string.Empty, CreateBankAsync).RequireScreen(ScreenIds.MasterData, ScreenAction.Create);
        banks.MapPut("{id:guid}", UpdateBankAsync).RequireScreen(ScreenIds.MasterData, ScreenAction.Edit);

        var accounts = app.MapGroup("/api/v1/bank-accounts").WithTags("Banking");
        accounts.MapGet(string.Empty, async (string? searchTerm, BankAccountService service, CancellationToken ct) =>
            ErpResults.Ok(await service.ListAsync(searchTerm, ct))).RequireScreen(ScreenIds.MasterData, ScreenAction.View);
        accounts.MapGet("{id:guid}", async (Guid id, BankAccountService service, CancellationToken ct) =>
            ErpResults.Ok(await service.GetAsync(id, ct))).RequireScreen(ScreenIds.MasterData, ScreenAction.View);
        accounts.MapPost(string.Empty, async (SaveBankAccountRequest request, BankAccountService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(request, ct);
            return ErpResults.Created($"/api/v1/bank-accounts/{created.Id}", created, "تم إضافة الحساب البنكي");
        }).RequireScreen(ScreenIds.MasterData, ScreenAction.Create);
        accounts.MapPut("{id:guid}", async (Guid id, SaveBankAccountRequest request, BankAccountService service, CancellationToken ct) =>
            ErpResults.Ok(await service.UpdateAsync(id, request, ct), "تم تحديث الحساب البنكي")).RequireScreen(ScreenIds.MasterData, ScreenAction.Edit);
        accounts.MapDelete("{id:guid}", async (Guid id, BankAccountService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return ErpResults.Ok(new { id }, "تم حذف الحساب البنكي");
        }).RequireScreen(ScreenIds.MasterData, ScreenAction.Delete);
        accounts.MapPost("{id:guid}/account", async (Guid id, EnsureAccountRequest? request, BankAccountService service, CancellationToken ct) =>
            ErpResults.Ok(await service.EnsureGlAccountAsync(id, request?.AccountCode, ct), "تم ربط الحساب المحاسبي")).RequireScreen(ScreenIds.Accounts, ScreenAction.Create);
    }

    private static async Task<IResult> ListBanksAsync(bool? financingOnly, BankingDbContext db, CancellationToken ct)
    {
        var query = db.Banks.AsNoTracking();
        if (financingOnly == true)
        {
            query = query.Where(b => b.IsFinancingInstitution && b.IsActive);
        }

        return ErpResults.Ok(await query.OrderBy(b => b.NameAr).Select(b => ToDto(b)).ToListAsync(ct));
    }

    private static async Task<IResult> CreateBankAsync(SaveBankRequest request, BankingDbContext db, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.NameAr))
        {
            throw ErpException.Validation("Code and Arabic name are required.", "الرمز والاسم بالعربي مطلوبان.");
        }

        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Banks.AnyAsync(b => b.Code == code, ct))
        {
            throw ErpException.Conflict("bank_code_taken", $"Bank {code} already exists.", $"البنك {code} موجود مسبقاً.");
        }

        var bank = new Bank(code, request.NameAr, request.NameEn ?? string.Empty, request.SwiftCode, request.IsFinancingInstitution ?? false);
        db.Banks.Add(bank);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Created($"/api/v1/banks/{bank.Id}", ToDto(bank), "تم إضافة البنك");
    }

    private static async Task<IResult> UpdateBankAsync(Guid id, SaveBankRequest request, BankingDbContext db, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var bank = await db.Banks.SingleOrDefaultAsync(b => b.Id == id, ct) ?? throw ErpException.NotFound("Bank", "البنك");
        bank.Update(request.NameAr, request.NameEn ?? string.Empty, request.SwiftCode, request.IsFinancingInstitution ?? bank.IsFinancingInstitution, request.IsActive ?? bank.IsActive);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Ok(ToDto(bank), "تم تحديث البنك");
    }

    private static BankDto ToDto(Bank b) => new(b.Id, b.Code, b.NameAr, b.NameEn, b.SwiftCode, b.IsFinancingInstitution, b.IsActive);
}
