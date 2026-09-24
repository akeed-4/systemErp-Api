using System.Net.Mail;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.BuildingBlocks.Web;
using Erp.Modules.Accounting.Contracts;
using Erp.Modules.Customers.Contracts;
using Erp.Modules.Customers.Domain;
using Erp.Modules.Customers.Persistence;
using Erp.Modules.Settings.Contracts;
using Erp.SharedKernel.Errors;
using Erp.SharedKernel.Text;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Customers.Application;

/// <summary>The frontend Customer shape (+ car-buyer identity fields).</summary>
internal sealed record CustomerDto(
    Guid Id,
    Guid TenantId,
    string Code,
    CustomerType CustomerType,
    string NameAr,
    string NameEn,
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
    decimal CurrentBalance,
    Guid? AccountId,
    string? AccountCode,
    string Currency,
    string Status,
    string? Notes);

internal sealed record SaveCustomerRequest(
    string? Code,
    CustomerType? CustomerType,
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
    decimal? CreditLimit,
    int? CreditPeriodDays,
    decimal? OpeningBalance,
    string? AccountCode,
    string? Currency,
    string? Status,
    string? Notes);

internal sealed record CustomerLookupItem(Guid Id, string Code, string NameAr, string NameEn, string? Phone, string? VatNumber, string? NationalId);

/// <summary>Customer use cases. Customers own the creation of their GL sub-account (never the other way round, §15 P3).</summary>
internal sealed class CustomerService(
    CustomersDbContext db,
    IUnitOfWork unitOfWork,
    INumberSequenceService numbers,
    IAccountProvisioningService accounts,
    IAccountingPostingService posting,
    IAccountLookup accountLookup,
    IAccountBalanceQueries balances,
    TimeProvider clock)
{
    public const string Module = "customers";
    public const string DocumentType = "customer";

    public async Task<PagedResult<CustomerDto>> ListAsync(PaginationParams paging, CancellationToken ct)
    {
        var query = db.Customers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(paging.SearchTerm))
        {
            var term = paging.SearchTerm.Trim();
            query = query.Where(c => c.Code.Contains(term) || c.NameAr.Contains(term) || c.NameEn.Contains(term)
                || (c.Phone != null && c.Phone.Contains(term)) || (c.VatNumber != null && c.VatNumber.Contains(term)) || (c.NationalId != null && c.NationalId.Contains(term)));
        }

        if (paging.Status is "active" or "inactive")
        {
            var active = paging.Status == "active";
            query = query.Where(c => c.IsActive == active);
        }

        var page = await query.OrderBy(c => c.Code).ToPagedResultAsync(paging, ct);
        return new PagedResult<CustomerDto>(await ToDtosAsync(page.Items, ct), page.TotalCount, page.PageNumber, page.PageSize);
    }

    public async Task<List<CustomerLookupItem>> LookupAsync(string? q, CancellationToken ct)
    {
        var query = db.Customers.AsNoTracking().Where(c => c.IsActive);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(c => c.Code.Contains(term) || c.NameAr.Contains(term) || c.NameEn.Contains(term) || (c.Phone != null && c.Phone.Contains(term)) || (c.NationalId != null && c.NationalId.Contains(term)));
        }

        return await query.OrderBy(c => c.NameAr).Take(20)
            .Select(c => new CustomerLookupItem(c.Id, c.Code, c.NameAr, c.NameEn, c.Phone, c.VatNumber, c.NationalId))
            .ToListAsync(ct);
    }

    public async Task<CustomerDto> GetAsync(Guid id, CancellationToken ct) =>
        (await ToDtosAsync([await FindAsync(id, tracking: false, ct)], ct))[0];

    public async Task<CustomerDto> CreateAsync(SaveCustomerRequest request, CancellationToken ct)
    {
        Validate(request);
        var id = await unitOfWork.ExecuteAsync(
            async innerCt =>
            {
                var code = string.IsNullOrWhiteSpace(request.Code) ? await numbers.NextCodeAsync("customer", "CUST-", 4, innerCt) : request.Code.Trim().ToUpperInvariant();
                if (await db.Customers.AnyAsync(c => c.Code == code, innerCt))
                {
                    throw ErpException.Conflict("customer_code_taken", $"Customer code {code} already exists.", $"رمز العميل {code} مستخدم مسبقاً.");
                }

                var customer = new Customer(code);
                customer.Update(ToData(request, null));
                db.Customers.Add(customer);
                await LinkGlAccountAsync(customer, request.AccountCode, innerCt);
                await PostOpeningBalanceAsync(customer, innerCt);
                return customer.Id;
            },
            ct);
        return await GetAsync(id, ct);
    }

    public async Task<CustomerDto> UpdateAsync(Guid id, SaveCustomerRequest request, CancellationToken ct)
    {
        Validate(request);
        await unitOfWork.ExecuteAsync(
            async innerCt =>
            {
                var customer = await FindAsync(id, tracking: true, innerCt);
                var openingChanged = request.OpeningBalance is { } opening && opening != customer.OpeningBalance;
                customer.Update(ToData(request, customer));
                if (customer.AccountId is { } glAccount)
                {
                    await accounts.RenameAsync(glAccount, customer.NameAr, customer.NameEn, innerCt);
                    if (openingChanged)
                    {
                        await PostOpeningBalanceAsync(customer, innerCt);
                    }
                }
            },
            ct);
        return await GetAsync(id, ct);
    }

    /// <summary>POST customers/{id}/account (frontend createAccountForCustomer): creates the GL sub-account if missing.</summary>
    public async Task<CustomerDto> EnsureGlAccountAsync(Guid id, string? customCode, CancellationToken ct)
    {
        await unitOfWork.ExecuteAsync(
            async innerCt =>
            {
                var customer = await FindAsync(id, tracking: true, innerCt);
                if (customer.AccountId is null)
                {
                    await LinkGlAccountAsync(customer, customCode, innerCt);
                    await PostOpeningBalanceAsync(customer, innerCt);
                }
            },
            ct);
        return await GetAsync(id, ct);
    }

    public async Task<AccountStatement> StatementAsync(Guid id, DateOnly? from, DateOnly? to, IAccountStatementService statements, CancellationToken ct)
    {
        var customer = await FindAsync(id, tracking: false, ct);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return await statements.GetAsync(
            customer.AccountId ?? throw ErpException.Conflict("customer_without_account", "The customer has no GL account.", "العميل ليس له حساب محاسبي."),
            from ?? new DateOnly(today.Year, 1, 1),
            to ?? today,
            ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var customer = await FindAsync(id, tracking: true, ct);
        if (customer.AccountId is { } glAccount && await balances.GetBalanceAsync(glAccount, null, ct) != 0)
        {
            throw ErpException.Conflict("customer_has_balance", "A customer with an open balance cannot be deleted; deactivate it instead.", "لا يمكن حذف عميل عليه رصيد؛ يمكنك إيقافه بدلاً من ذلك.");
        }

        customer.Delete();
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task LinkGlAccountAsync(Customer customer, string? preferredCode, CancellationToken ct)
    {
        var gl = await accounts.CreateSubAccountAsync(
            new SubAccountRequest(PostingPurpose.CustomerControl, customer.NameAr, customer.NameEn, "customer", customer.Id, preferredCode, customer.CurrencyCode),
            ct);
        customer.LinkAccount(gl.Id);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task PostOpeningBalanceAsync(Customer customer, CancellationToken ct) =>
        await posting.RepostAsync(
            OpeningBalancePosting.Build(
                new SourceRef(Module, DocumentType, customer.Id, customer.Code),
                DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
                customer.AccountId!.Value,
                customer.OpeningBalance,
                debitNature: true,
                $"رصيد افتتاحي للعميل {customer.NameAr}"),
            ct);

    private async Task<Customer> FindAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var query = tracking ? db.Customers : db.Customers.AsNoTracking();
        return await query.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw ErpException.NotFound("Customer", "العميل");
    }

    private async Task<List<CustomerDto>> ToDtosAsync(IReadOnlyList<Customer> customers, CancellationToken ct)
    {
        var glIds = customers.Where(c => c.AccountId is not null).Select(c => c.AccountId!.Value).ToList();
        var glBalances = await balances.GetBalancesAsync(glIds, null, ct);
        var glAccounts = await accountLookup.FindManyAsync(glIds, ct);
        return customers.Select(c => new CustomerDto(
            c.Id, c.TenantId, c.Code, c.Type, c.NameAr, c.NameEn, c.VatNumber, c.CrNumber, c.NationalId, c.IdExpiryDate, c.Phone, c.AltPhone, c.Email,
            c.ContactPerson, c.City, c.District, c.Street, c.BuildingNo, c.PostalCode, c.AdditionalNo, c.CreditLimit, c.CreditPeriodDays, c.OpeningBalance,
            c.AccountId is { } gl ? glBalances.GetValueOrDefault(gl) : 0,
            c.AccountId,
            c.AccountId is { } g && glAccounts.TryGetValue(g, out var account) ? account.Code : null,
            c.CurrencyCode,
            c.IsActive ? "active" : "inactive",
            c.Notes)).ToList();
    }

    private static CustomerData ToData(SaveCustomerRequest r, Customer? existing) =>
        new(
            r.CustomerType ?? existing?.Type ?? (string.IsNullOrWhiteSpace(r.VatNumber) && string.IsNullOrWhiteSpace(r.CrNumber) ? CustomerType.Individual : CustomerType.Corporate),
            r.NameAr,
            r.NameEn,
            r.VatNumber,
            r.CrNumber,
            r.NationalId,
            r.IdExpiryDate,
            r.Phone,
            r.AltPhone,
            r.Email,
            r.ContactPerson,
            r.City,
            r.District,
            r.Street,
            r.BuildingNo,
            r.PostalCode,
            r.AdditionalNo,
            r.CreditLimit ?? existing?.CreditLimit ?? 0,
            r.CreditPeriodDays ?? existing?.CreditPeriodDays ?? 0,
            r.OpeningBalance ?? existing?.OpeningBalance ?? 0,
            r.Currency ?? existing?.CurrencyCode ?? "SAR",
            !string.Equals(r.Status, "inactive", StringComparison.OrdinalIgnoreCase),
            r.Notes);

    private static void Validate(SaveCustomerRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.NameAr))
        {
            errors["nameAr"] = ["required"];
        }

        if (r.VatNumber is { Length: > 0 } vat && !SaudiIdentifiers.IsVatNumber(vat))
        {
            errors["vatNumber"] = ["15 digits starting and ending with 3"];
        }

        if (r.NationalId is { Length: > 0 } nid && !SaudiIdentifiers.IsTenDigitId(nid))
        {
            errors["nationalId"] = ["10 digits starting with 1 (citizen) or 2 (resident)"];
        }

        if (r.Email is { Length: > 0 } email && !MailAddress.TryCreate(email.Trim(), out _))
        {
            errors["email"] = ["invalid"];
        }

        if (r.CreditLimit < 0 || r.CreditPeriodDays < 0)
        {
            errors["creditLimit"] = ["must not be negative"];
        }

        if (errors.Count > 0)
        {
            throw ErpException.Validation("The customer is not valid.", "بيانات العميل غير مكتملة أو غير صحيحة.", errors);
        }
    }
}

internal sealed class CustomerDirectory(CustomersDbContext db) : ICustomerDirectory
{
    public async Task<CustomerSummary?> FindAsync(Guid customerId, CancellationToken cancellationToken) =>
        (await FindManyAsync([customerId], cancellationToken)).GetValueOrDefault(customerId);

    public async Task<IReadOnlyDictionary<Guid, CustomerSummary>> FindManyAsync(IReadOnlyCollection<Guid> customerIds, CancellationToken cancellationToken) =>
        await db.Customers.AsNoTracking()
            .Where(c => customerIds.Contains(c.Id))
            .Select(c => new CustomerSummary(c.Id, c.Code, c.Type, c.NameAr, c.NameEn, c.VatNumber, c.CrNumber, c.NationalId, c.Phone, c.Email, c.City,
                c.District, c.Street, c.BuildingNo, c.PostalCode, c.CreditLimit, c.CreditPeriodDays, c.AccountId, c.IsActive))
            .ToDictionaryAsync(c => c.Id, cancellationToken);
}
