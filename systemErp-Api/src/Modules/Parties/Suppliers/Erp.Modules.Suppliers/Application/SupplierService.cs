using System.Net.Mail;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.BuildingBlocks.Web;
using Erp.Modules.Accounting.Contracts;
using Erp.Modules.Banking.Contracts;
using Erp.Modules.Settings.Contracts;
using Erp.Modules.Suppliers.Contracts;
using Erp.Modules.Suppliers.Domain;
using Erp.Modules.Suppliers.Persistence;
using Erp.SharedKernel.Errors;
using Erp.SharedKernel.Text;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Suppliers.Application;

/// <summary>The frontend Supplier shape; bankName/iban/swiftCode are the primary bank detail.</summary>
internal sealed record SupplierDto(
    Guid Id,
    Guid TenantId,
    string Code,
    string NameAr,
    string NameEn,
    string? VatNumber,
    string? CrNumber,
    string? Phone,
    string? Email,
    string? ContactPerson,
    string? City,
    string? Address,
    Guid? BankId,
    string? BankName,
    string? Iban,
    string? SwiftCode,
    int PaymentTermsDays,
    decimal OpeningBalance,
    decimal CurrentBalance,
    Guid? AccountId,
    string? AccountCode,
    string Currency,
    string Status,
    string? Notes);

internal sealed record SaveSupplierRequest(
    string? Code,
    string NameAr,
    string? NameEn,
    string? VatNumber,
    string? CrNumber,
    string? Phone,
    string? Email,
    string? ContactPerson,
    string? City,
    string? Address,
    Guid? BankId,
    string? BankName,
    string? Iban,
    string? SwiftCode,
    int? PaymentTermsDays,
    decimal? OpeningBalance,
    string? AccountCode,
    string? Currency,
    string? Status,
    string? Notes);

internal sealed record SupplierLookupItem(Guid Id, string Code, string NameAr, string NameEn, string? Phone, string? VatNumber);

internal sealed class SupplierService(
    SuppliersDbContext db,
    IUnitOfWork unitOfWork,
    INumberSequenceService numbers,
    IAccountProvisioningService accounts,
    IAccountingPostingService posting,
    IAccountLookup accountLookup,
    IAccountBalanceQueries balances,
    IBankDirectory banks,
    TimeProvider clock)
{
    public const string Module = "suppliers";
    public const string DocumentType = "supplier";

    public async Task<PagedResult<SupplierDto>> ListAsync(PaginationParams paging, CancellationToken ct)
    {
        var query = db.Suppliers.AsNoTracking().Include(s => s.BankDetails).AsQueryable();
        if (!string.IsNullOrWhiteSpace(paging.SearchTerm))
        {
            var term = paging.SearchTerm.Trim();
            query = query.Where(s => s.Code.Contains(term) || s.NameAr.Contains(term) || s.NameEn.Contains(term)
                || (s.Phone != null && s.Phone.Contains(term)) || (s.VatNumber != null && s.VatNumber.Contains(term)));
        }

        if (paging.Status is "active" or "inactive")
        {
            var active = paging.Status == "active";
            query = query.Where(s => s.IsActive == active);
        }

        var page = await query.OrderBy(s => s.Code).ToPagedResultAsync(paging, ct);
        return new PagedResult<SupplierDto>(await ToDtosAsync(page.Items, ct), page.TotalCount, page.PageNumber, page.PageSize);
    }

    public Task<List<SupplierLookupItem>> LookupAsync(string? q, CancellationToken ct)
    {
        var query = db.Suppliers.AsNoTracking().Where(s => s.IsActive);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(s => s.Code.Contains(term) || s.NameAr.Contains(term) || s.NameEn.Contains(term) || (s.Phone != null && s.Phone.Contains(term)));
        }

        return query.OrderBy(s => s.NameAr).Take(20)
            .Select(s => new SupplierLookupItem(s.Id, s.Code, s.NameAr, s.NameEn, s.Phone, s.VatNumber))
            .ToListAsync(ct);
    }

    public async Task<SupplierDto> GetAsync(Guid id, CancellationToken ct) =>
        (await ToDtosAsync([await FindAsync(id, tracking: false, ct)], ct))[0];

    public async Task<SupplierDto> CreateAsync(SaveSupplierRequest request, CancellationToken ct)
    {
        await ValidateAsync(request, ct);
        var id = await unitOfWork.ExecuteAsync(
            async innerCt =>
            {
                var code = string.IsNullOrWhiteSpace(request.Code) ? await numbers.NextCodeAsync("supplier", "SUP-", 4, innerCt) : request.Code.Trim().ToUpperInvariant();
                if (await db.Suppliers.AnyAsync(s => s.Code == code, innerCt))
                {
                    throw ErpException.Conflict("supplier_code_taken", $"Supplier code {code} already exists.", $"رمز المورد {code} مستخدم مسبقاً.");
                }

                var supplier = new Supplier(code);
                supplier.Update(ToData(request, null));
                supplier.SetPrimaryBank(request.BankId, request.BankName, request.Iban, request.SwiftCode);
                db.Suppliers.Add(supplier);
                await LinkGlAccountAsync(supplier, request.AccountCode, innerCt);
                await PostOpeningBalanceAsync(supplier, innerCt);
                return supplier.Id;
            },
            ct);
        return await GetAsync(id, ct);
    }

    public async Task<SupplierDto> UpdateAsync(Guid id, SaveSupplierRequest request, CancellationToken ct)
    {
        await ValidateAsync(request, ct);
        await unitOfWork.ExecuteAsync(
            async innerCt =>
            {
                var supplier = await FindAsync(id, tracking: true, innerCt);
                var openingChanged = request.OpeningBalance is { } opening && opening != supplier.OpeningBalance;
                supplier.Update(ToData(request, supplier));
                supplier.SetPrimaryBank(request.BankId, request.BankName, request.Iban, request.SwiftCode);
                if (supplier.AccountId is { } glAccount)
                {
                    await accounts.RenameAsync(glAccount, supplier.NameAr, supplier.NameEn, innerCt);
                    if (openingChanged)
                    {
                        await PostOpeningBalanceAsync(supplier, innerCt);
                    }
                }
            },
            ct);
        return await GetAsync(id, ct);
    }

    public async Task<SupplierDto> EnsureGlAccountAsync(Guid id, string? customCode, CancellationToken ct)
    {
        await unitOfWork.ExecuteAsync(
            async innerCt =>
            {
                var supplier = await FindAsync(id, tracking: true, innerCt);
                if (supplier.AccountId is null)
                {
                    await LinkGlAccountAsync(supplier, customCode, innerCt);
                    await PostOpeningBalanceAsync(supplier, innerCt);
                }
            },
            ct);
        return await GetAsync(id, ct);
    }

    public async Task<AccountStatement> StatementAsync(Guid id, DateOnly? from, DateOnly? to, IAccountStatementService statements, CancellationToken ct)
    {
        var supplier = await FindAsync(id, tracking: false, ct);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return await statements.GetAsync(
            supplier.AccountId ?? throw ErpException.Conflict("supplier_without_account", "The supplier has no GL account.", "المورد ليس له حساب محاسبي."),
            from ?? new DateOnly(today.Year, 1, 1),
            to ?? today,
            ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var supplier = await FindAsync(id, tracking: true, ct);
        if (supplier.AccountId is { } glAccount && await balances.GetBalanceAsync(glAccount, null, ct) != 0)
        {
            throw ErpException.Conflict("supplier_has_balance", "A supplier with an open balance cannot be deleted; deactivate it instead.", "لا يمكن حذف مورد عليه رصيد؛ يمكنك إيقافه بدلاً من ذلك.");
        }

        supplier.Delete();
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task LinkGlAccountAsync(Supplier supplier, string? preferredCode, CancellationToken ct)
    {
        var gl = await accounts.CreateSubAccountAsync(
            new SubAccountRequest(PostingPurpose.SupplierControl, supplier.NameAr, supplier.NameEn, "supplier", supplier.Id, preferredCode, supplier.CurrencyCode),
            ct);
        supplier.LinkAccount(gl.Id);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task PostOpeningBalanceAsync(Supplier supplier, CancellationToken ct) =>
        await posting.RepostAsync(
            OpeningBalancePosting.Build(
                new SourceRef(Module, DocumentType, supplier.Id, supplier.Code),
                DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
                supplier.AccountId!.Value,
                supplier.OpeningBalance,
                debitNature: false,
                $"رصيد افتتاحي للمورد {supplier.NameAr}"),
            ct);

    private async Task<Supplier> FindAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var query = (tracking ? db.Suppliers : db.Suppliers.AsNoTracking()).Include(s => s.BankDetails);
        return await query.SingleOrDefaultAsync(s => s.Id == id, ct) ?? throw ErpException.NotFound("Supplier", "المورد");
    }

    private async Task<List<SupplierDto>> ToDtosAsync(IReadOnlyList<Supplier> suppliers, CancellationToken ct)
    {
        var glIds = suppliers.Where(s => s.AccountId is not null).Select(s => s.AccountId!.Value).ToList();
        var glBalances = await balances.GetBalancesAsync(glIds, null, ct);
        var glAccounts = await accountLookup.FindManyAsync(glIds, ct);
        return suppliers.Select(s =>
        {
            var bank = s.PrimaryBank;
            return new SupplierDto(
                s.Id, s.TenantId, s.Code, s.NameAr, s.NameEn, s.VatNumber, s.CrNumber, s.Phone, s.Email, s.ContactPerson, s.City, s.Address,
                bank?.BankId, bank?.BankName, bank?.Iban, bank?.SwiftCode, s.PaymentTermsDays, s.OpeningBalance,
                s.AccountId is { } gl ? glBalances.GetValueOrDefault(gl) : 0,
                s.AccountId,
                s.AccountId is { } g && glAccounts.TryGetValue(g, out var account) ? account.Code : null,
                s.CurrencyCode,
                s.IsActive ? "active" : "inactive",
                s.Notes);
        }).ToList();
    }

    private static SupplierData ToData(SaveSupplierRequest r, Supplier? existing) =>
        new(
            r.NameAr,
            r.NameEn,
            r.VatNumber,
            r.CrNumber,
            r.Phone,
            r.Email,
            r.ContactPerson,
            r.City,
            r.Address,
            r.PaymentTermsDays ?? existing?.PaymentTermsDays ?? 30,
            r.OpeningBalance ?? existing?.OpeningBalance ?? 0,
            r.Currency ?? existing?.CurrencyCode ?? "SAR",
            !string.Equals(r.Status, "inactive", StringComparison.OrdinalIgnoreCase),
            r.Notes);

    private async Task ValidateAsync(SaveSupplierRequest r, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.NameAr))
        {
            errors["nameAr"] = ["required"];
        }

        var vat = r.VatNumber?.Trim();
        if (vat is { Length: > 0 } && !SaudiIdentifiers.IsVatNumber(vat))
        {
            errors["vatNumber"] = ["15 digits starting and ending with 3"];
        }

        if (r.Email is { Length: > 0 } email && !MailAddress.TryCreate(email.Trim(), out _))
        {
            errors["email"] = ["invalid"];
        }

        if (r.PaymentTermsDays < 0)
        {
            errors["paymentTermsDays"] = ["must not be negative"];
        }

        if (r.BankId is { } bankId && await banks.FindBankAsync(bankId, ct) is null)
        {
            errors["bankId"] = ["unknown bank"];
        }

        if (errors.Count > 0)
        {
            throw ErpException.Validation("The supplier is not valid.", "بيانات المورد غير مكتملة أو غير صحيحة.", errors);
        }
    }
}

internal sealed class SupplierDirectory(SuppliersDbContext db) : ISupplierDirectory
{
    public async Task<SupplierSummary?> FindAsync(Guid supplierId, CancellationToken cancellationToken) =>
        (await FindManyAsync([supplierId], cancellationToken)).GetValueOrDefault(supplierId);

    public async Task<IReadOnlyDictionary<Guid, SupplierSummary>> FindManyAsync(IReadOnlyCollection<Guid> supplierIds, CancellationToken cancellationToken) =>
        await db.Suppliers.AsNoTracking()
            .Where(s => supplierIds.Contains(s.Id))
            .Select(s => new SupplierSummary(s.Id, s.Code, s.NameAr, s.NameEn, s.VatNumber, s.CrNumber, s.Phone, s.Email, s.City, s.Address, s.PaymentTermsDays, s.AccountId, s.IsActive))
            .ToDictionaryAsync(s => s.Id, cancellationToken);
}
