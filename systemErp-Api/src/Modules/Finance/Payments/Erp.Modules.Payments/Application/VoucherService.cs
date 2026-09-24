using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.BuildingBlocks.Web;
using Erp.Modules.Accounting.Contracts;
using Erp.Modules.Customers.Contracts;
using Erp.Modules.Payments.Contracts;
using Erp.Modules.Payments.Domain;
using Erp.Modules.Payments.Persistence;
using Erp.Modules.Settings.Contracts;
using Erp.Modules.Suppliers.Contracts;
using Erp.SharedKernel.Errors;
using Erp.SharedKernel.Text;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Payments.Application;

internal sealed record VoucherPaymentDto(Guid PaymentMethodId, string Method, string MethodNameAr, Guid? BankAccountId, decimal Amount, string? Reference);

internal sealed record VoucherAllocationDto(string Module, string DocumentType, Guid DocumentId, string DocumentNumber, decimal Amount);

/// <summary>The frontend Voucher shape (paymentMethod = first method, paymentSplits = all rows).</summary>
internal sealed record VoucherDto(
    Guid Id,
    Guid TenantId,
    string? VoucherNumber,
    VoucherType Type,
    DateOnly Date,
    decimal Amount,
    string AmountInWordsAr,
    VoucherPartyType PartyType,
    Guid? PartyId,
    string PartyName,
    Guid PartyAccountId,
    string? PartyAccountCode,
    string? TreasuryAccountCode,
    string? PaymentMethod,
    bool IsSplitPayment,
    IReadOnlyList<VoucherPaymentDto> PaymentSplits,
    IReadOnlyList<VoucherAllocationDto> Allocations,
    string? ReferenceNumber,
    string? Notes,
    string? ReceivedOrPaidBy,
    Guid? CostCenterId,
    string Status,
    Guid? JournalEntryId,
    DateTimeOffset CreatedAt);

/// <summary>
/// Receipt/payment vouchers. Posting: receipt = Dr treasury (per payment row) / Cr party; payment = the reverse.
/// Cancelling a posted voucher reverses its journal entry.
/// </summary>
internal sealed class VoucherService(
    PaymentsDbContext db,
    IUnitOfWork unitOfWork,
    INumberSequenceService numbers,
    IPaymentMethodDirectory paymentMethods,
    ICustomerDirectory customers,
    ISupplierDirectory suppliers,
    IAccountLookup accounts,
    IAccountingPostingService posting,
    TimeProvider clock) : IVoucherService
{
    public const string Module = "payments";
    public const string PostingKind = "voucher";

    public async Task<VoucherResult> CreateAsync(CreateVoucherCommand command, CancellationToken cancellationToken)
    {
        var voucher = await unitOfWork.ExecuteAsync(
            async ct =>
            {
                var created = new Voucher(command.Type, command.Date);
                var (partyName, partyAccountId) = await ResolvePartyAsync(command, ct);
                created.SetParty(command.PartyType, command.PartyId, partyName, partyAccountId);
                created.SetDetails(command.ReferenceNumber, command.Notes, command.ReceivedOrPaidBy, command.CostCenterId);

                if (command.Payments.Count == 0)
                {
                    throw ErpException.Validation("At least one payment line is required.", "يجب إدخال دفعة واحدة على الأقل.");
                }

                foreach (var payment in command.Payments)
                {
                    var method = await paymentMethods.FindAsync(payment.PaymentMethodId, ct);
                    if (method?.RequiresReference == true && string.IsNullOrWhiteSpace(payment.Reference) && string.IsNullOrWhiteSpace(command.ReferenceNumber))
                    {
                        throw ErpException.Validation($"{method.NameEn} requires a reference number.", $"طريقة الدفع {method.NameAr} تتطلب رقماً مرجعياً.");
                    }

                    var treasury = await paymentMethods.ResolveTreasuryAccountAsync(payment.PaymentMethodId, payment.BankAccountId, ct);
                    created.AddPayment(payment.PaymentMethodId, payment.BankAccountId, treasury, payment.Amount, payment.Reference);
                }

                foreach (var allocation in command.Allocations ?? [])
                {
                    created.Allocate(allocation);
                }

                if ((command.Allocations ?? []).Sum(a => a.Amount) > created.Amount)
                {
                    throw ErpException.Validation("Allocations exceed the voucher amount.", "مجموع التخصيصات يتجاوز مبلغ السند.");
                }

                created.SetWords(ArabicAmountInWords.Riyals(created.Amount));
                db.Vouchers.Add(created);

                if (command.Post)
                {
                    await PostAsync(created, ct);
                }

                return created;
            },
            cancellationToken);

        return new VoucherResult(voucher.Id, voucher.VoucherNumber ?? string.Empty, voucher.Amount, voucher.JournalEntryId);
    }

    public async Task<decimal> GetAllocatedAmountAsync(string module, string documentType, Guid documentId, CancellationToken cancellationToken) =>
        await (
            from a in db.VoucherAllocations.AsNoTracking()
            join v in db.Vouchers.AsNoTracking() on a.VoucherId equals v.Id
            where a.TargetModule == module && a.TargetDocumentType == documentType && a.TargetDocumentId == documentId && v.Status == VoucherStatus.Posted
            select a.Amount).SumAsync(cancellationToken);

    public async Task<VoucherDto> PostDraftAsync(Guid id, CancellationToken ct)
    {
        await unitOfWork.ExecuteAsync(
            async innerCt =>
            {
                var voucher = await LoadAsync(id, tracking: true, innerCt);
                if (voucher.Status != VoucherStatus.Draft)
                {
                    throw ErpException.Conflict("voucher_not_draft", "Only draft vouchers can be posted.", "لا يمكن ترحيل إلا السندات المسودة.");
                }

                await PostAsync(voucher, innerCt);
            },
            ct);
        return await GetAsync(id, ct);
    }

    public async Task<VoucherDto> CancelAsync(Guid id, string? reason, CancellationToken ct)
    {
        await unitOfWork.ExecuteAsync(
            async innerCt =>
            {
                var voucher = await LoadAsync(id, tracking: true, innerCt);
                var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
                if (voucher.Status == VoucherStatus.Posted)
                {
                    await posting.ReverseAsync(Source(voucher), PostingKind, today, reason ?? "إلغاء السند", innerCt);
                }

                voucher.Cancel(clock.GetUtcNow(), reason ?? "إلغاء السند");
            },
            ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteDraftAsync(Guid id, CancellationToken ct)
    {
        var voucher = await LoadAsync(id, tracking: true, ct);
        if (voucher.Status != VoucherStatus.Draft)
        {
            throw ErpException.Conflict("voucher_not_draft", "Posted vouchers are cancelled, not deleted.", "السندات المرحّلة تُلغى ولا تُحذف.");
        }

        db.Vouchers.Remove(voucher);
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<VoucherDto>> ListAsync(PaginationParams paging, VoucherType? type, CancellationToken ct)
    {
        var query = db.Vouchers.AsNoTracking().Include(v => v.Payments).Include(v => v.Allocations).AsQueryable();
        if (type is { } t)
        {
            query = query.Where(v => v.Type == t);
        }

        if (paging.StartDate is { } start)
        {
            query = query.Where(v => v.Date >= start);
        }

        if (paging.EndDate is { } end)
        {
            query = query.Where(v => v.Date <= end);
        }

        if (!string.IsNullOrWhiteSpace(paging.SearchTerm))
        {
            var term = paging.SearchTerm.Trim();
            query = query.Where(v => v.PartyName.Contains(term) || (v.VoucherNumber != null && v.VoucherNumber.Contains(term)) || (v.ReferenceNumber != null && v.ReferenceNumber.Contains(term)));
        }

        var page = await query.OrderByDescending(v => v.Date).ThenByDescending(v => v.CreatedAt).ToPagedResultAsync(paging, ct);
        return new PagedResult<VoucherDto>(await ToDtosAsync(page.Items, ct), page.TotalCount, page.PageNumber, page.PageSize);
    }

    public async Task<VoucherDto> GetAsync(Guid id, CancellationToken ct) =>
        (await ToDtosAsync([await LoadAsync(id, tracking: false, ct)], ct))[0];

    private async Task PostAsync(Voucher voucher, CancellationToken ct)
    {
        var number = voucher.VoucherNumber ?? await numbers.NextAsync(voucher.Type == VoucherType.Receipt ? "rv" : "pv", voucher.Date, null, ct);
        voucher.AssignNumber(number);

        var party = voucher.PartyType switch
        {
            VoucherPartyType.Customer when voucher.PartyId is { } id => new PartyRef(PartyType.Customer, id),
            VoucherPartyType.Supplier when voucher.PartyId is { } id => new PartyRef(PartyType.Supplier, id),
            _ => null,
        };

        var receipt = voucher.Type == VoucherType.Receipt;
        var lines = voucher.Payments
            .Select(p => new PostingLine(AccountRef.ById(p.TreasuryAccountId), receipt ? p.Amount : 0, receipt ? 0 : p.Amount, voucher.CostCenterId, Notes: p.Reference))
            .Append(new PostingLine(AccountRef.ById(voucher.PartyAccountId), receipt ? 0 : voucher.Amount, receipt ? voucher.Amount : 0, voucher.CostCenterId, party))
            .ToList();

        var description = $"{(receipt ? "سند قبض" : "سند صرف")} رقم {number} - {voucher.PartyName}{(voucher.Notes is { Length: > 0 } notes ? " - " + notes : string.Empty)}";
        var result = await posting.PostAsync(new PostingRequest(Source(voucher), PostingKind, voucher.Date, description, lines), ct);
        voucher.MarkPosted(number, result.JournalEntryId);
    }

    private async Task<(string Name, Guid AccountId)> ResolvePartyAsync(CreateVoucherCommand command, CancellationToken ct)
    {
        switch (command.PartyType)
        {
            case VoucherPartyType.Customer:
                var customer = command.PartyId is { } customerId ? await customers.FindAsync(customerId, ct) : null;
                return customer is { AccountId: { } customerAccount }
                    ? (customer.NameAr, customerAccount)
                    : throw ErpException.Validation("Unknown customer or customer without account.", "العميل غير موجود أو ليس له حساب محاسبي.");
            case VoucherPartyType.Supplier:
                var supplier = command.PartyId is { } supplierId ? await suppliers.FindAsync(supplierId, ct) : null;
                return supplier is { AccountId: { } supplierAccount }
                    ? (supplier.NameAr, supplierAccount)
                    : throw ErpException.Validation("Unknown supplier or supplier without account.", "المورد غير موجود أو ليس له حساب محاسبي.");
            default:
                var account = command.PartyAccountId is { } accountId ? await accounts.FindAsync(accountId, ct) : null;
                if (account is not { IsPostable: true, IsActive: true })
                {
                    throw ErpException.Validation("Choose an active sub-account for the voucher.", "يرجى اختيار حساب فرعي نشط للسند.");
                }

                return (string.IsNullOrWhiteSpace(command.PartyName) ? account.NameAr : command.PartyName, account.Id);
        }
    }

    private async Task<Voucher> LoadAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var query = (tracking ? db.Vouchers : db.Vouchers.AsNoTracking()).Include(v => v.Payments).Include(v => v.Allocations);
        return await query.SingleOrDefaultAsync(v => v.Id == id, ct) ?? throw ErpException.NotFound("Voucher", "السند");
    }

    private static SourceRef Source(Voucher voucher) =>
        new(Module, voucher.Type == VoucherType.Receipt ? "receipt_voucher" : "payment_voucher", voucher.Id, voucher.VoucherNumber ?? string.Empty);

    private async Task<List<VoucherDto>> ToDtosAsync(IReadOnlyList<Voucher> vouchers, CancellationToken ct)
    {
        var methodIds = vouchers.SelectMany(v => v.Payments.Select(p => p.PaymentMethodId)).Distinct().ToList();
        var methods = await paymentMethods.FindManyAsync(methodIds, ct);
        var accountIds = vouchers.Select(v => v.PartyAccountId).Concat(vouchers.SelectMany(v => v.Payments.Select(p => p.TreasuryAccountId))).Distinct().ToList();
        var glAccounts = await accounts.FindManyAsync(accountIds, ct);

        return vouchers.Select(v =>
        {
            var splits = v.Payments.OrderBy(p => p.LineNo).Select(p => new VoucherPaymentDto(
                p.PaymentMethodId,
                methods.TryGetValue(p.PaymentMethodId, out var m) ? m.Code : string.Empty,
                m?.NameAr ?? string.Empty,
                p.BankAccountId,
                p.Amount,
                p.Reference)).ToList();
            var first = v.Payments.OrderBy(p => p.LineNo).FirstOrDefault();
            return new VoucherDto(
                v.Id, v.TenantId, v.VoucherNumber, v.Type, v.Date, v.Amount, v.AmountInWordsAr, v.PartyType, v.PartyId, v.PartyName, v.PartyAccountId,
                glAccounts.TryGetValue(v.PartyAccountId, out var party) ? party.Code : null,
                first is not null && glAccounts.TryGetValue(first.TreasuryAccountId, out var treasury) ? treasury.Code : null,
                splits.FirstOrDefault()?.Method,
                splits.Count > 1,
                splits,
                v.Allocations.Select(a => new VoucherAllocationDto(a.TargetModule, a.TargetDocumentType, a.TargetDocumentId, a.TargetDocumentNumber, a.Amount)).ToList(),
                v.ReferenceNumber, v.Notes, v.ReceivedOrPaidBy, v.CostCenterId,
                v.Status.ToString().ToLowerInvariant(),
                v.JournalEntryId,
                v.CreatedAt);
        }).ToList();
    }
}
