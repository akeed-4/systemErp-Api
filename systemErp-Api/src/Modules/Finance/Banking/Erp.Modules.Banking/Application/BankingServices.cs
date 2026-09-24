using Erp.BuildingBlocks.Infrastructure.Modules;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.Accounting.Contracts;
using Erp.Modules.Banking.Contracts;
using Erp.Modules.Banking.Domain;
using Erp.Modules.Banking.Persistence;
using Erp.Modules.Settings.Contracts;
using Erp.SharedKernel.Errors;
using Erp.SharedKernel.Text;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Banking.Application;

/// <summary>The frontend BankEntity shape (a company bank account) plus its bank institution.</summary>
internal sealed record BankAccountDto(
    Guid Id,
    Guid TenantId,
    string Code,
    Guid BankId,
    string BankNameAr,
    string BankNameEn,
    string NameAr,
    string NameEn,
    string AccountNumber,
    string? Iban,
    string? SwiftCode,
    string? Branch,
    string Currency,
    decimal OpeningBalance,
    decimal CurrentBalance,
    Guid? AccountId,
    string? AccountCode,
    string Status,
    string? Notes);

internal sealed record SaveBankAccountRequest(
    string? Code,
    Guid BankId,
    string NameAr,
    string? NameEn,
    string AccountNumber,
    string? Iban,
    string? Branch,
    string? Currency,
    decimal? OpeningBalance,
    string? AccountCode,
    string? Status,
    string? Notes);

internal sealed class BankingSeeder(BankingDbContext db) : IModuleSeeder
{
    private static readonly (string Code, string Ar, string En, string Swift, bool Financing)[] Defaults =
    [
        ("RJHI", "مصرف الراجحي", "Al Rajhi Bank", "RJHISARI", true),
        ("SNB", "البنك الأهلي السعودي", "Saudi National Bank", "NCBKSAJE", true),
        ("RIBL", "بنك الرياض", "Riyad Bank", "RIBLSARI", true),
        ("SABB", "البنك السعودي الأول", "Saudi Awwal Bank", "SABBSARI", true),
        ("INMA", "مصرف الإنماء", "Alinma Bank", "INMASARI", true),
        ("BSFR", "البنك السعودي الفرنسي", "Banque Saudi Fransi", "BSFRSARI", true),
        ("ARNB", "البنك العربي الوطني", "Arab National Bank", "ARNBSARI", true),
        ("ALBI", "بنك البلاد", "Bank Albilad", "ALBISARI", true),
        ("BJAZ", "بنك الجزيرة", "Bank AlJazira", "BJAZSAJE", true),
        ("ALJF", "شركة عبد اللطيف جميل المتحدة للتمويل", "Abdul Latif Jameel United Finance", string.Empty, true),
    ];

    public int Order => 60;

    public async Task SeedTenantAsync(TenantSeedContext context, CancellationToken cancellationToken)
    {
        if (await db.Banks.AnyAsync(cancellationToken))
        {
            return;
        }

        foreach (var (code, ar, en, swift, financing) in Defaults)
        {
            db.Banks.Add(new Bank(code, ar, en, swift, financing));
        }
    }
}

internal sealed class BankDirectory(BankingDbContext db) : IBankDirectory
{
    public Task<BankSummary?> FindBankAsync(Guid bankId, CancellationToken cancellationToken) =>
        db.Banks.AsNoTracking().Where(b => b.Id == bankId)
            .Select(b => new BankSummary(b.Id, b.Code, b.NameAr, b.NameEn, b.SwiftCode, b.IsFinancingInstitution, b.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<BankAccountSummary?> FindBankAccountAsync(Guid bankAccountId, CancellationToken cancellationToken) =>
        db.BankAccounts.AsNoTracking().Where(a => a.Id == bankAccountId)
            .Select(a => new BankAccountSummary(a.Id, a.Code, a.NameAr, a.NameEn, a.BankId, a.Iban, a.CurrencyCode, a.AccountId, a.IsActive))
            .SingleOrDefaultAsync(cancellationToken);
}

/// <summary>Bank-account use cases: every account gets a GL sub-account under 111 and an opening-balance entry.</summary>
internal sealed class BankAccountService(
    BankingDbContext db,
    IUnitOfWork unitOfWork,
    INumberSequenceService numbers,
    IAccountProvisioningService accounts,
    IAccountingPostingService posting,
    IAccountLookup accountLookup,
    IAccountBalanceQueries balances,
    TimeProvider clock)
{
    public const string Module = "banking";
    public const string DocumentType = "bank_account";

    public async Task<List<BankAccountDto>> ListAsync(string? search, CancellationToken ct)
    {
        var query = db.BankAccounts.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(a => a.Code.Contains(term) || a.NameAr.Contains(term) || a.NameEn.Contains(term) || (a.Iban != null && a.Iban.Contains(term)));
        }

        return await ToDtosAsync(await query.OrderBy(a => a.Code).ToListAsync(ct), ct);
    }

    public async Task<BankAccountDto> GetAsync(Guid id, CancellationToken ct) =>
        (await ToDtosAsync([await FindAsync(id, tracking: false, ct)], ct))[0];

    public async Task<BankAccountDto> CreateAsync(SaveBankAccountRequest request, CancellationToken ct)
    {
        var id = await unitOfWork.ExecuteAsync(
            async innerCt =>
            {
                var bank = await ValidateAsync(request, null, innerCt);
                var code = string.IsNullOrWhiteSpace(request.Code) ? await numbers.NextCodeAsync("bank_account", "BNK-", 3, innerCt) : request.Code.Trim().ToUpperInvariant();
                if (await db.BankAccounts.AnyAsync(a => a.Code == code, innerCt))
                {
                    throw ErpException.Conflict("bank_account_code_taken", $"Bank account code {code} already exists.", $"رمز الحساب البنكي {code} مستخدم مسبقاً.");
                }

                var account = new BankAccount(code, bank.Id);
                Apply(account, request);
                db.BankAccounts.Add(account);
                await LinkGlAccountAsync(account, request.AccountCode, innerCt);
                await PostOpeningBalanceAsync(account, innerCt);
                return account.Id;
            },
            ct);
        return await GetAsync(id, ct);
    }

    public async Task<BankAccountDto> UpdateAsync(Guid id, SaveBankAccountRequest request, CancellationToken ct)
    {
        await unitOfWork.ExecuteAsync(
            async innerCt =>
            {
                var account = await FindAsync(id, tracking: true, innerCt);
                var openingChanged = account.OpeningBalance != (request.OpeningBalance ?? account.OpeningBalance);
                await ValidateAsync(request, id, innerCt);
                Apply(account, request with { OpeningBalance = request.OpeningBalance ?? account.OpeningBalance });
                if (account.AccountId is { } glAccount)
                {
                    await accounts.RenameAsync(glAccount, account.NameAr, account.NameEn, innerCt);
                }

                if (openingChanged)
                {
                    await PostOpeningBalanceAsync(account, innerCt);
                }
            },
            ct);
        return await GetAsync(id, ct);
    }

    /// <summary>POST bank-accounts/{id}/account: creates the GL sub-account if it is missing (frontend createAccountForBank).</summary>
    public async Task<BankAccountDto> EnsureGlAccountAsync(Guid id, string? customCode, CancellationToken ct)
    {
        await unitOfWork.ExecuteAsync(
            async innerCt =>
            {
                var account = await FindAsync(id, tracking: true, innerCt);
                if (account.AccountId is null)
                {
                    await LinkGlAccountAsync(account, customCode, innerCt);
                    await PostOpeningBalanceAsync(account, innerCt);
                }
            },
            ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var account = await FindAsync(id, tracking: true, ct);
        if (account.AccountId is { } glAccount && await balances.GetBalanceAsync(glAccount, null, ct) != 0)
        {
            throw ErpException.Conflict("bank_account_has_balance", "A bank account with a balance cannot be deleted.", "لا يمكن حذف حساب بنكي عليه رصيد.");
        }

        account.Delete();
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task LinkGlAccountAsync(BankAccount account, string? preferredCode, CancellationToken ct)
    {
        var gl = await accounts.CreateSubAccountAsync(
            new SubAccountRequest(PostingPurpose.BankControl, account.NameAr, account.NameEn, "bank", account.Id, preferredCode, account.CurrencyCode),
            ct);
        account.LinkAccount(gl.Id);

        // The new GL account must exist before an entry can be posted to it.
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task PostOpeningBalanceAsync(BankAccount account, CancellationToken ct) =>
        await posting.RepostAsync(
            OpeningBalancePosting.Build(
                new SourceRef(Module, DocumentType, account.Id, account.Code),
                DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
                account.AccountId!.Value,
                account.OpeningBalance,
                debitNature: true,
                $"رصيد افتتاحي للحساب البنكي {account.NameAr}"),
            ct);

    private async Task<Bank> ValidateAsync(SaveBankAccountRequest request, Guid? id, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.NameAr))
        {
            errors["nameAr"] = ["required"];
        }

        if (string.IsNullOrWhiteSpace(request.AccountNumber))
        {
            errors["accountNumber"] = ["required"];
        }

        var iban = request.Iban?.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (iban is { Length: > 0 } && !SaudiIdentifiers.IsIban(iban))
        {
            errors["iban"] = ["invalid (Saudi IBANs are SA + 22 digits)"];
        }

        if (errors.Count > 0)
        {
            throw ErpException.Validation("The bank account is not valid.", "بيانات الحساب البنكي غير مكتملة أو غير صحيحة.", errors);
        }

        if (iban is { Length: > 0 } && await db.BankAccounts.AnyAsync(a => a.Iban == iban && a.Id != id, ct))
        {
            throw ErpException.Conflict("iban_taken", "Another bank account already uses this IBAN.", "رقم الآيبان مستخدم لحساب بنكي آخر.");
        }

        return await db.Banks.SingleOrDefaultAsync(b => b.Id == request.BankId && b.IsActive, ct)
            ?? throw ErpException.Validation("Unknown or inactive bank.", "البنك غير موجود أو غير نشط.");
    }

    private static void Apply(BankAccount account, SaveBankAccountRequest request) =>
        account.Update(
            request.BankId,
            request.NameAr,
            request.NameEn ?? string.Empty,
            request.AccountNumber,
            request.Iban,
            request.Branch,
            request.Currency ?? "SAR",
            request.OpeningBalance ?? 0,
            !string.Equals(request.Status, "inactive", StringComparison.OrdinalIgnoreCase),
            request.Notes);

    private async Task<BankAccount> FindAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var query = tracking ? db.BankAccounts : db.BankAccounts.AsNoTracking();
        return await query.SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw ErpException.NotFound("Bank account", "الحساب البنكي");
    }

    private async Task<List<BankAccountDto>> ToDtosAsync(List<BankAccount> list, CancellationToken ct)
    {
        var bankIds = list.Select(a => a.BankId).Distinct().ToList();
        var banks = await db.Banks.AsNoTracking().Where(b => bankIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
        var glIds = list.Where(a => a.AccountId is not null).Select(a => a.AccountId!.Value).ToList();
        var glBalances = await balances.GetBalancesAsync(glIds, null, ct);
        var codes = (await accountLookup.FindManyAsync(glIds, ct)).ToDictionary(p => p.Key, p => p.Value.Code);

        return list.Select(a =>
        {
            var bank = banks[a.BankId];
            return new BankAccountDto(
                a.Id, a.TenantId, a.Code, a.BankId, bank.NameAr, bank.NameEn, a.NameAr, a.NameEn, a.AccountNumber, a.Iban, bank.SwiftCode, a.Branch,
                a.CurrencyCode, a.OpeningBalance, a.AccountId is { } gl ? glBalances.GetValueOrDefault(gl) : 0,
                a.AccountId, a.AccountId is { } g ? codes.GetValueOrDefault(g) : null, a.IsActive ? "active" : "inactive", a.Notes);
        }).ToList();
    }
}
