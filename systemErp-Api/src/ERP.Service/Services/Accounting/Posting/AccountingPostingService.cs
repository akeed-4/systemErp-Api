using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// المحرك المحاسبي المركزي: كل الوحدات التشغيلية تستدعيه ولا تبني قيوداً بنفسها. يتحقق من صحة القيد
/// (توازن، حسابات موجودة وورقية)، يرقّمه، يحدّث أرصدة الحسابات وأرصدة العملاء/الموردين/البنوك المرتبطة.
/// لا يبدأ معاملة بنفسه: يعمل داخل معاملة المتصل (ITransactionRunner) ليبقى الأثر كله ذرّياً.
/// </summary>
public class AccountingPostingService : IAccountingPostingService
{
    private const decimal Tolerance = 0.005m;

    private readonly ErpDbContext _db;
    private readonly INumberSequenceService _numbers;

    public AccountingPostingService(ErpDbContext db, INumberSequenceService numbers)
    {
        _db = db; _numbers = numbers;
    }

    public async Task<PostingResult> PostAsync(GenericPostingRequest request, CancellationToken ct = default)
    {
        var prepared = await PrepareAsync(request, ct);
        var entry = new JournalEntry
        {
            EntryNumber = await _numbers.NextAsync("journal_entry", "JE-"),
            Date = request.Date,
            Description = request.Description,
            ReferenceType = request.SourceType ?? "manual",
            ReferenceId = request.SourceId,
            ReferenceNumber = request.SourceNumber,
            SourceType = request.SourceType,
            SourceReferenceId = request.SourceId,
            Status = JournalEntryStatus.Posted,
        };
        Attach(entry, prepared);
        _db.Add(entry);
        await SyncLinkedEntityBalancesAsync(prepared.Accounts, ct);
        await _db.SaveChangesAsync(ct);
        return new PostingResult(entry.Id, entry.EntryNumber);
    }

    public async Task ReplaceManualAsync(Guid journalEntryId, GenericPostingRequest request, CancellationToken ct = default)
    {
        var entry = await LoadManualAsync(journalEntryId, ct);
        var prepared = await PrepareAsync(request, ct);
        await UnapplyAsync(entry, ct);
        _db.RemoveRange(entry.Lines);
        entry.Lines.Clear();
        entry.Date = request.Date;
        // القيد الآلي المعدَّل يدوياً يبقى مرتبطاً بمستنده مع علامة صريحة في الوصف للتدقيق.
        var automatic = entry.ReferenceType is not (null or "manual");
        entry.Description = automatic && !request.Description.Contains("[مُعدَّل يدوياً]") ? request.Description + " [مُعدَّل يدوياً]" : request.Description;
        Attach(entry, prepared);
        await SyncLinkedEntityBalancesAsync(prepared.Accounts, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteManualAsync(Guid journalEntryId, CancellationToken ct = default)
    {
        var entry = await LoadManualAsync(journalEntryId, ct);
        await UnapplyAsync(entry, ct);
        // فك ارتباط المستندات التي تشير لهذا القيد كي لا يبقى مرجع معلّق.
        foreach (var inv in await _db.Set<Invoice>().Where(i => i.JournalEntryId == entry.Id).ToListAsync(ct)) inv.JournalEntryId = null;
        foreach (var v in await _db.Set<Voucher>().Where(x => x.JournalEntryId == entry.Id).ToListAsync(ct)) v.JournalEntryId = null;
        _db.RemoveRange(entry.Lines);
        _db.Remove(entry);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<JournalEntry> LoadManualAsync(Guid id, CancellationToken ct)
    {
        var entry = await _db.Set<JournalEntry>().Include(e => e.Lines).FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new NotFoundException("القيد غير موجود");
        if (entry.Status == JournalEntryStatus.Reversed) throw new ConflictException("القيد المعكوس لا يُعدَّل.");
        if (entry.SourceType == "reversal") throw new ConflictException("قيد العكس لا يُعدَّل ولا يُحذف؛ هو أثر تدقيق.");
        return entry;
    }

    private sealed record Prepared(List<PostingLine> Lines, List<Account> Accounts, Dictionary<string, Account> ByCode, decimal TotalDebit, decimal TotalCredit);

    private async Task<Prepared> PrepareAsync(GenericPostingRequest request, CancellationToken ct)
    {
        var lines = request.Lines.Where(l => l.Debit != 0 || l.Credit != 0).ToList();
        if (lines.Count < 2) throw new ValidationFailedException("القيد يحتاج سطرين على الأقل.");
        if (lines.Any(l => l.Debit < 0 || l.Credit < 0 || (l.Debit > 0 && l.Credit > 0)))
            throw new ValidationFailedException("كل سطر إما مدين أو دائن بمبلغ موجب.");

        var totalDebit = lines.Sum(l => l.Debit);
        var totalCredit = lines.Sum(l => l.Credit);
        if (Math.Abs(totalDebit - totalCredit) > Tolerance)
            throw new ValidationFailedException($"القيد غير متوازن: مدين {totalDebit:0.00} ≠ دائن {totalCredit:0.00}.");

        var codes = lines.Select(l => l.AccountCode).Distinct().ToList();
        var accounts = await _db.Set<Account>().Where(a => codes.Contains(a.Code)).ToListAsync(ct);
        var missing = codes.Except(accounts.Select(a => a.Code)).ToList();
        if (missing.Count > 0) throw new ValidationFailedException("حسابات غير موجودة: " + string.Join(", ", missing));

        var parents = await _db.Set<Account>().Where(a => a.ParentCode != null && codes.Contains(a.ParentCode))
            .Select(a => a.ParentCode!).Distinct().ToListAsync(ct);
        if (parents.Count > 0)
            throw new ValidationFailedException("لا يمكن الترحيل على حساب رئيسي، اختر حساباً فرعياً: " + string.Join(", ", parents));

        return new Prepared(lines, accounts, accounts.ToDictionary(a => a.Code), totalDebit, totalCredit);
    }

    private static void Attach(JournalEntry entry, Prepared p)
    {
        entry.TotalDebit = Round(p.TotalDebit);
        entry.TotalCredit = Round(p.TotalCredit);
        foreach (var l in p.Lines)
        {
            entry.Lines.Add(new JournalEntryLine
            {
                AccountCode = l.AccountCode, AccountName = p.ByCode[l.AccountCode].NameAr,
                Debit = Round(l.Debit), Credit = Round(l.Credit), Notes = l.Notes, CostCenterId = l.CostCenterId,
            });
            ApplyToBalance(p.ByCode[l.AccountCode], l.Debit, l.Credit);
        }
    }

    public Task<PostingResult> PostSaleAsync(SalePostingRequest r, CancellationToken ct = default)
    {
        var total = r.NetAmount + r.VatAmount;
        var payments = ValidatePayments(r.Payments, total);
        var receivable = total - payments.Sum(p => p.Amount);

        var lines = new List<PostingLine>();
        foreach (var p in payments) lines.Add(new(p.TreasuryAccountCode, p.Amount, 0));
        if (receivable > Tolerance)
            lines.Add(new(RequireParty(r.PartyAccountCode, "العميل"), receivable, 0));
        lines.Add(new(r.RevenueAccountCode ?? DefaultAccounts.Revenue, 0, r.NetAmount, null, r.CostCenterId));
        if (r.VatAmount > 0) lines.Add(new(DefaultAccounts.OutputVat, 0, r.VatAmount));
        if (r.CostAmount > 0)
        {
            lines.Add(new(r.CogsAccountCode ?? DefaultAccounts.Cogs, r.CostAmount, 0));
            lines.Add(new(r.InventoryAccountCode ?? DefaultAccounts.Inventory, 0, r.CostAmount));
        }
        return PostAsync(Build(r.Date, r.Description, r.SourceType, r.SourceId, r.SourceNumber, lines, r.IsReturn), ct);
    }

    public Task<PostingResult> PostPurchaseAsync(PurchasePostingRequest r, CancellationToken ct = default)
    {
        var total = r.NetAmount + r.VatAmount;
        var payments = ValidatePayments(r.Payments, total);
        var payable = total - payments.Sum(p => p.Amount);

        var lines = new List<PostingLine> { new(r.InventoryAccountCode ?? DefaultAccounts.Inventory, r.NetAmount, 0, null, r.CostCenterId) };
        if (r.VatAmount > 0) lines.Add(new(DefaultAccounts.InputVat, r.VatAmount, 0));
        foreach (var p in payments) lines.Add(new(p.TreasuryAccountCode, 0, p.Amount));
        if (payable > Tolerance)
            lines.Add(new(RequireParty(r.PartyAccountCode, "المورد"), 0, payable));
        return PostAsync(Build(r.Date, r.Description, r.SourceType, r.SourceId, r.SourceNumber, lines, r.IsReturn), ct);
    }

    public Task<PostingResult> PostVoucherAsync(VoucherPostingRequest r, CancellationToken ct = default)
    {
        if (r.Treasury.Count == 0 || r.Treasury.Any(t => t.Amount <= 0))
            throw new ValidationFailedException("حدّد حساب الخزينة/البنك ومبلغاً موجباً.");
        var total = r.Treasury.Sum(t => t.Amount);
        var party = RequireParty(r.PartyAccountCode, "الطرف");

        var lines = new List<PostingLine>();
        if (r.Type == VoucherType.Receipt)
        {
            foreach (var t in r.Treasury) lines.Add(new(t.TreasuryAccountCode, t.Amount, 0));
            lines.Add(new(party, 0, total));
        }
        else
        {
            lines.Add(new(party, total, 0));
            foreach (var t in r.Treasury) lines.Add(new(t.TreasuryAccountCode, 0, t.Amount));
        }
        var type = r.Type == VoucherType.Receipt ? "receipt_voucher" : "payment_voucher";
        return PostAsync(new GenericPostingRequest
        {
            Date = r.Date, Description = r.Description, SourceType = type, SourceId = r.VoucherId, SourceNumber = r.VoucherNumber, Lines = lines,
        }, ct);
    }

    public async Task<PostingResult> ReverseAsync(Guid journalEntryId, string? reason = null, CancellationToken ct = default)
    {
        var original = await _db.Set<JournalEntry>().Include(e => e.Lines).FirstOrDefaultAsync(e => e.Id == journalEntryId, ct)
            ?? throw new NotFoundException("القيد غير موجود");
        if (original.Status == JournalEntryStatus.Reversed) throw new ConflictException("القيد معكوس مسبقاً.");
        if (original.SourceType == "reversal") throw new ConflictException("لا يمكن عكس قيد عكسي.");

        var lines = original.Lines.Select(l => new PostingLine(l.AccountCode, l.Credit, l.Debit, l.Notes, l.CostCenterId)).ToList();
        var result = await PostAsync(new GenericPostingRequest
        {
            Date = DateTime.UtcNow,
            Description = $"عكس القيد {original.EntryNumber}" + (string.IsNullOrWhiteSpace(reason) ? "" : $" - {reason}"),
            SourceType = "reversal", SourceId = original.Id, SourceNumber = original.EntryNumber, Lines = lines,
        }, ct);

        original.Status = JournalEntryStatus.Reversed;
        await _db.SaveChangesAsync(ct);
        return result;
    }

    // ---------- مساعدات ----------
    private static GenericPostingRequest Build(DateTime date, string description, string sourceType, Guid sourceId, string sourceNumber,
        List<PostingLine> lines, bool reverse)
        => new()
        {
            Date = date,
            Description = string.IsNullOrWhiteSpace(description) ? $"{sourceType} {sourceNumber}" : description,
            SourceType = sourceType, SourceId = sourceId, SourceNumber = sourceNumber,
            Lines = reverse ? lines.Select(l => l with { Debit = l.Credit, Credit = l.Debit }).ToList() : lines,
        };

    private static List<PaymentPosting> ValidatePayments(List<PaymentPosting> payments, decimal total)
    {
        if (total < 0) throw new ValidationFailedException("إجمالي المستند لا يكون سالباً.");
        if (payments.Any(p => p.Amount <= 0 || string.IsNullOrWhiteSpace(p.TreasuryAccountCode)))
            throw new ValidationFailedException("مبالغ الدفع يجب أن تكون موجبة ومرتبطة بحساب.");
        if (payments.Sum(p => p.Amount) - total > Tolerance)
            throw new ValidationFailedException("المدفوع أكبر من إجمالي المستند.");
        return payments;
    }

    private static string RequireParty(string? code, string who)
        => string.IsNullOrWhiteSpace(code) ? throw new ValidationFailedException($"يلزم تحديد حساب {who} للمبلغ الآجل.") : code;

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    /// <summary>الرصيد الطبيعي: مدين الطبيعة = مدين - دائن، ودائن الطبيعة = دائن - مدين.</summary>
    internal static void ApplyToBalance(Account account, decimal debit, decimal credit)
        => account.Balance += account.IsDebitNature ? debit - credit : credit - debit;

    private async Task SyncLinkedEntityBalancesAsync(List<Account> touched, CancellationToken ct)
    {
        foreach (var a in touched.Where(a => a.LinkedEntityId.HasValue && a.LinkedEntityType is > LinkedEntityType.General))
        {
            var id = a.LinkedEntityId!.Value;
            switch (a.LinkedEntityType)
            {
                case LinkedEntityType.Customer:
                    var c = await _db.Set<Customer>().FirstOrDefaultAsync(x => x.Id == id, ct);
                    if (c != null) c.CurrentBalance = c.OpeningBalance + a.Balance;
                    break;
                case LinkedEntityType.Supplier:
                    var s = await _db.Set<Supplier>().FirstOrDefaultAsync(x => x.Id == id, ct);
                    if (s != null) s.CurrentBalance = s.OpeningBalance + a.Balance;
                    break;
                case LinkedEntityType.Bank:
                    var b = await _db.Set<BankEntity>().FirstOrDefaultAsync(x => x.Id == id, ct);
                    if (b != null) b.CurrentBalance = b.OpeningBalance + a.Balance;
                    break;
            }
        }
    }

    /// <summary>يسحب أثر قيد قائم من الأرصدة (للتعديل/الحذف الداخلي للقيود اليدوية).</summary>
    private async Task UnapplyAsync(JournalEntry entry, CancellationToken ct)
    {
        var codes = entry.Lines.Select(l => l.AccountCode).Distinct().ToList();
        var accounts = await _db.Set<Account>().Where(a => codes.Contains(a.Code)).ToListAsync(ct);
        var byCode = accounts.ToDictionary(a => a.Code);
        foreach (var l in entry.Lines)
            if (byCode.TryGetValue(l.AccountCode, out var acc)) ApplyToBalance(acc, -l.Debit, -l.Credit);
        await SyncLinkedEntityBalancesAsync(accounts, ct);
    }
}
