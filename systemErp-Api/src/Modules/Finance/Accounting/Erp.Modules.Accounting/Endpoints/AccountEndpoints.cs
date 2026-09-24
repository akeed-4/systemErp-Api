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

internal sealed record CreateAccountRequest(
    string Code,
    string NameAr,
    string? NameEn,
    string? ParentCode,
    AccountType? Type,
    bool? IsDebitNature,
    string? Currency,
    string? Notes);

internal sealed record UpdateAccountRequest(string NameAr, string? NameEn, string? Currency, string? Notes, bool? IsActive);

/// <summary>Chart of accounts (accounts-tree screen).</summary>
internal static class AccountEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var accounts = app.MapGroup("/api/v1/accounts").WithTags("Accounting");
        accounts.MapGet(string.Empty, ListAsync).RequireScreen(ScreenIds.Accounts, ScreenAction.View);
        accounts.MapGet("{id:guid}", GetAsync).RequireScreen(ScreenIds.Accounts, ScreenAction.View);
        accounts.MapGet("{id:guid}/statement", StatementAsync).RequireScreen(ScreenIds.Accounts, ScreenAction.View);
        accounts.MapPost(string.Empty, CreateAsync).RequireScreen(ScreenIds.Accounts, ScreenAction.Create);
        accounts.MapPut("{id:guid}", UpdateAsync).RequireScreen(ScreenIds.Accounts, ScreenAction.Edit);
        accounts.MapDelete("{id:guid}", DeleteAsync).RequireScreen(ScreenIds.Accounts, ScreenAction.Delete);
    }

    /// <summary>Flat list by default; ?view=tree returns the nested tree. Balances are natural-signed and rolled up.</summary>
    private static async Task<IResult> ListAsync(string? view, AccountingQueries queries, CancellationToken ct) =>
        ErpResults.Ok(await queries.GetAccountsAsync(string.Equals(view, "tree", StringComparison.OrdinalIgnoreCase), ct));

    private static async Task<IResult> GetAsync(Guid id, AccountingQueries queries, CancellationToken ct) =>
        ErpResults.Ok((await queries.GetAccountsAsync(tree: false, ct)).SingleOrDefault(a => a.Id == id) ?? throw NotFound());

    private static async Task<IResult> StatementAsync(Guid id, DateOnly? from, DateOnly? to, AccountingQueries queries, TimeProvider clock, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return ErpResults.Ok(await queries.StatementAsync(id, from ?? new DateOnly(today.Year, 1, 1), to ?? today, ct));
    }

    private static async Task<IResult> CreateAsync(CreateAccountRequest request, AccountingDbContext db, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 30 || !request.Code.Trim().All(char.IsAsciiDigit))
        {
            throw ErpException.Validation("The account code must be digits only (max 30).", "رمز الحساب يجب أن يتكون من أرقام فقط.");
        }

        if (string.IsNullOrWhiteSpace(request.NameAr))
        {
            throw ErpException.Validation("The Arabic name is required.", "اسم الحساب بالعربي مطلوب.");
        }

        var code = request.Code.Trim();
        if (await db.Accounts.AnyAsync(a => a.Code == code, ct))
        {
            throw ErpException.Conflict("account_code_taken", $"Account code {code} already exists.", $"رمز الحساب {code} مستخدم مسبقاً.");
        }

        Account? parent = null;
        if (!string.IsNullOrWhiteSpace(request.ParentCode))
        {
            parent = await db.Accounts.SingleOrDefaultAsync(a => a.Code == request.ParentCode.Trim(), ct)
                ?? throw ErpException.Validation("The parent account does not exist.", "الحساب الرئيسي غير موجود.");
            if (!code.StartsWith(parent.Code, StringComparison.Ordinal) || code.Length <= parent.Code.Length)
            {
                throw ErpException.Validation($"A sub-account code must start with {parent.Code}.", $"رمز الحساب الفرعي يجب أن يبدأ بـ {parent.Code}.");
            }

            if (parent.IsPostable && await db.JournalEntryLines.AnyAsync(l => l.AccountId == parent.Id, ct))
            {
                throw ErpException.Conflict("parent_has_postings", $"Account {parent.Code} has postings and cannot get sub-accounts.", $"الحساب {parent.Code} عليه حركات ولا يمكن إضافة حسابات فرعية تحته.");
            }
        }
        else if (request.Type is null)
        {
            throw ErpException.Validation("A root account needs a type.", "الحساب الرئيسي يحتاج إلى تحديد النوع.");
        }

        var type = parent?.Type ?? request.Type!.Value;
        var debitNature = request.IsDebitNature ?? parent?.IsDebitNature ?? type is AccountType.Asset or AccountType.Expense;
        var account = new Account(code, request.NameAr, request.NameEn ?? string.Empty, type, parent, debitNature, isSystem: false);
        account.Update(account.NameAr, account.NameEn, request.Notes, request.Currency, isActive: true);
        db.Accounts.Add(account);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Created($"/api/v1/accounts/{account.Id}", AccountingQueries.ToDto(account, parent?.Code, 0), "تم إضافة الحساب");
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateAccountRequest request, AccountingDbContext db, IUnitOfWork unitOfWork, AccountingQueries queries, CancellationToken ct)
    {
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw NotFound();
        if (string.IsNullOrWhiteSpace(request.NameAr))
        {
            throw ErpException.Validation("The Arabic name is required.", "اسم الحساب بالعربي مطلوب.");
        }

        account.Update(request.NameAr, request.NameEn ?? string.Empty, request.Notes, request.Currency, request.IsActive ?? account.IsActive);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Ok((await queries.GetAccountsAsync(tree: false, ct)).Single(a => a.Id == id), "تم تحديث الحساب");
    }

    private static async Task<IResult> DeleteAsync(Guid id, AccountingDbContext db, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw NotFound();
        var blocked =
            account.IsSystem ? "system" :
            account.LinkedEntityId is not null ? "linked" :
            await db.Accounts.AnyAsync(a => a.ParentId == id, ct) ? "children" :
            await db.JournalEntryLines.AnyAsync(l => l.AccountId == id, ct) ? "postings" :
            await db.PostingMappings.AnyAsync(m => m.AccountId == id, ct) ? "mapped" : null;
        if (blocked is not null)
        {
            throw ErpException.Conflict(
                $"account_{blocked}",
                "The account is a system/linked/mapped account or has sub-accounts or postings; deactivate it instead.",
                "لا يمكن حذف الحساب لأنه نظامي أو مرتبط أو له حسابات فرعية أو حركات؛ يمكنك إيقافه بدلاً من ذلك.");
        }

        db.Accounts.Remove(account);
        await unitOfWork.SaveChangesAsync(ct);

        // The parent may be a leaf again.
        if (account.ParentId is { } parentId && !await db.Accounts.AnyAsync(a => a.ParentId == parentId, ct))
        {
            await db.Accounts.Where(a => a.Id == parentId).ExecuteUpdateAsync(s => s.SetProperty(a => a.IsPostable, true), ct);
        }

        return ErpResults.Ok(new { id }, "تم حذف الحساب");
    }

    private static ErpException NotFound() => ErpException.NotFound("Account", "الحساب");
}
