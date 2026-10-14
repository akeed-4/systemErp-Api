using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// المحرك المحاسبي المركزي: كل الوحدات التشغيلية تستدعيه ولا تبني قيوداً بنفسها. يتحقق من صحة القيد
/// (توازن، حسابات موجودة وورقية، تاريخ في فترة غير مقفلة)، يرقّمه، يحدّث أرصدة الحسابات وأرصدة العملاء/الموردين/البنوك المرتبطة.
/// لا يبدأ معاملة بنفسه: يعمل داخل معاملة المتصل (ITransactionRunner) ليبقى الأثر كله ذرّياً.
/// </summary>
public class AccountingPostingService : IAccountingPostingService
{
    private const decimal Tolerance = 0.005m;

    private readonly ErpDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly IPeriodLock _periods;

    public AccountingPostingService(ErpDbContext db, INumberSequenceService numbers, IPeriodLock periods)
    {
        _db = db; _numbers = numbers; _periods = periods;
    }

    public async Task<PostingResult> PostAsync(GenericPostingRequest request, CancellationToken ct = default)
    {
        await _periods.EnsureOpenAsync(request.Date, ct);
        var prepared = await PrepareAsync(request, ct);
        var entry = new JournalEntry
        {
            Module = ResolveModule(request.Module, request.SourceType),
            EntryNumber = await _numbers.NextAsync("journal_entry", "JE-", ct),
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
        await SyncLinkedEntityBalancesAsync(_db, prepared.Accounts, ct);
        await _db.SaveChangesAsync(ct);
        return new PostingResult(entry.Id, entry.EntryNumber);
    }

    public async Task ReplaceManualAsync(Guid journalEntryId, GenericPostingRequest request, CancellationToken ct = default)
    {
        var entry = await LoadManualAsync(journalEntryId, ct);
        EnsureNotReconciled(entry);
        await _periods.EnsureOpenAsync(entry.Date, ct);   // لا يُسحب قيد من فترة مقفلة
        await _periods.EnsureOpenAsync(request.Date, ct); // ولا يُنقل إليها
        var prepared = await PrepareAsync(request, ct);
        await UnapplyAsync(entry, ct);
        _db.RemoveRange(entry.Lines);
        entry.Lines.Clear();
        entry.Date = request.Date;
        entry.Description = request.Description;
        entry.Module = ResolveModule(request.Module ?? entry.Module, request.SourceType);
        Attach(entry, prepared);
        await SyncLinkedEntityBalancesAsync(_db, prepared.Accounts, ct);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>قيد طوبقت إحدى حركاته مع كشف البنك لا يُعدَّل ولا يُحذف (يُعكس بقيد جديد)، وإلا اختلّت التسوية المعتمدة.</summary>
    private static void EnsureNotReconciled(JournalEntry entry)
    {
        if (entry.Lines.Any(l => l.BankReconciliationId != null)) throw new ConflictException(Messages.JournalEntryReconciled);
    }

    /// <summary>
    /// «حذف» القيد إلغاءٌ لا مسح: يُعكس بقيد عكسي في فترته (يجب أن تكون مفتوحة) ويبقى القيدان في السجل،
    /// فلا فجوة في الترقيم ولا يضيع أثر ما حدث. يُرجع القيد العكسي.
    /// </summary>
    public async Task<PostingResult> DeleteManualAsync(Guid journalEntryId, CancellationToken ct = default)
    {
        var entry = await LoadManualAsync(journalEntryId, ct);
        EnsureNotReconciled(entry);
        await _periods.EnsureOpenAsync(entry.Date, ct);
        return await ReverseAsync(entry.Id, "إلغاء القيد", ct);
    }

    /// <summary>القيد اليدوي فقط. القيد الآلي يتبع مستنده (فاتورة، سند، جرد...)، وتعديله أو إلغاؤه وحده يفصل الدفاتر عن المستند والمخزون.</summary>
    private async Task<JournalEntry> LoadManualAsync(Guid id, CancellationToken ct)
    {
        var entry = await _db.Set<JournalEntry>().Include(e => e.Lines).FirstOrDefaultAsync(e => e.Id == id, ct)
            ?? throw new NotFoundException(Messages.JournalEntryNotFound);
        if (entry.Status == JournalEntryStatus.Reversed) throw new ConflictException(Messages.ReversedEntryCannotBeEdited);
        if (entry.SourceType == "reversal") throw new ConflictException(Messages.ReversalEntryIsAuditTrail);
        if (!IsManual(entry.SourceType)) throw new ConflictException(Messages.JournalEntryOwnedByDocument);
        return entry;
    }

    /// <summary>قيد أُنشئ من شاشة القيود (القيود القديمة قد تكون بلا مصدر).</summary>
    public static bool IsManual(string? sourceType) => sourceType is null or "manual";

    private sealed record Prepared(List<PostingLine> Lines, List<Account> Accounts, Dictionary<string, Account> ByCode, decimal TotalDebit, decimal TotalCredit);

    private async Task<Prepared> PrepareAsync(GenericPostingRequest request, CancellationToken ct)
    {
        var raw = request.Lines.Where(l => l.Debit != 0 || l.Credit != 0).ToList();
        if (raw.Count < 2) throw new ValidationFailedException(Messages.JournalEntryNeedsTwoLines);
        if (raw.Any(l => l.Debit < 0 || l.Credit < 0 || (l.Debit > 0 && l.Credit > 0)))
            throw new ValidationFailedException(Messages.LineMustBeDebitOrCredit);

        var rawDebit = raw.Sum(l => l.Debit);
        var rawCredit = raw.Sum(l => l.Credit);
        if (Math.Abs(rawDebit - rawCredit) > Tolerance)
            throw new ValidationFailedException(string.Format(Messages.JournalEntryUnbalanced, rawDebit, rawCredit));

        // التقريب لهللة يسبق كل شيء: الأسطر المحفوظة والأرصدة والإجماليات كلها بالقيم المقرّبة نفسها،
        // وكسر التقريب (المتبقي بعد اجتياز فحص التوازن أعلاه) يُحمَّل على أكبر سطر في الجانب الأصغر فيتوازن القيد تماماً.
        var lines = BalanceRounding(raw.Select(l => l with { Debit = Round(l.Debit), Credit = Round(l.Credit) })
            .Where(l => l.Debit != 0 || l.Credit != 0).ToList());
        if (lines.Count < 2) throw new ValidationFailedException(Messages.JournalEntryNeedsTwoLines);
        var totalDebit = lines.Sum(l => l.Debit);
        var totalCredit = lines.Sum(l => l.Credit);
        if (totalDebit != totalCredit)
            throw new ValidationFailedException(string.Format(Messages.JournalEntryUnbalanced, totalDebit, totalCredit));

        var codes = lines.Select(l => l.AccountCode).Distinct().ToList();
        var accounts = await _db.Set<Account>().Where(a => codes.Contains(a.Code)).ToListAsync(ct);
        var missing = codes.Except(accounts.Select(a => a.Code)).ToList();
        if (missing.Count > 0) throw new ValidationFailedException(Messages.AccountsNotFoundPrefix + string.Join(", ", missing));

        var parents = await _db.Set<Account>().Where(a => a.ParentCode != null && codes.Contains(a.ParentCode))
            .Select(a => a.ParentCode!).Distinct().ToListAsync(ct);
        if (parents.Count > 0)
            throw new ValidationFailedException(Messages.CannotPostToParentAccountPrefix + string.Join(", ", parents));

        return new Prepared(lines, accounts, accounts.ToDictionary(a => a.Code), totalDebit, totalCredit);
    }

    /// <summary>
    /// يوزّع فرق التقريب بعد تقريب كل سطر: مجموع أسطر مقرّبة قد يختلف بهللات عن الجانب الآخر وإن توازن القيد قبل التقريب.
    /// الفرق يُضاف لأكبر سطر في الجانب الأصغر (أقل أثر نسبي)، فيُحفظ القيد متوازناً بلا فرق.
    /// </summary>
    internal static List<PostingLine> BalanceRounding(List<PostingLine> lines)
    {
        var diff = lines.Sum(l => l.Debit) - lines.Sum(l => l.Credit);
        if (diff == 0) return lines;
        var creditSide = diff > 0; // المدين أكبر: يُزاد الدائن
        var target = lines.Select((l, i) => (l, i)).Where(x => creditSide ? x.l.Credit > 0 : x.l.Debit > 0)
            .OrderByDescending(x => creditSide ? x.l.Credit : x.l.Debit).Select(x => (int?)x.i).FirstOrDefault();
        if (target == null) return lines; // جانب بلا أسطر: يرفضه فحص التوازن عند المتصل
        var t = lines[target.Value];
        lines[target.Value] = creditSide ? t with { Credit = t.Credit + diff } : t with { Debit = t.Debit - diff };
        return lines;
    }

    /// <summary>الأسطر في p مقرّبة مسبقاً؛ الرصيد يتحرك بما يُحفظ في السطر تماماً فيبقى = مجموع حركات الحساب.</summary>
    private static void Attach(JournalEntry entry, Prepared p)
    {
        entry.TotalDebit = p.TotalDebit;
        entry.TotalCredit = p.TotalCredit;
        foreach (var l in p.Lines)
        {
            entry.Lines.Add(new JournalEntryLine
            {
                AccountCode = l.AccountCode, AccountName = p.ByCode[l.AccountCode].NameAr,
                Debit = l.Debit, Credit = l.Credit, Notes = l.Notes, CostCenterId = l.CostCenterId,
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
            lines.Add(new(RequireParty(r.PartyAccountCode, Messages.PartyCustomer), receivable, 0));
        lines.AddRange(RevenueLines(r));
        if (r.VatAmount > 0) lines.Add(new(DefaultAccounts.OutputVat, 0, r.VatAmount));
        if (r.CostAmount > 0)
        {
            lines.Add(new(r.CogsAccountCode ?? DefaultAccounts.Cogs, r.CostAmount, 0));
            lines.Add(new(r.InventoryAccountCode ?? DefaultAccounts.Inventory, 0, r.CostAmount));
        }
        else if (r.CostAmount < 0) // تغيّر قيمة المخزون عكس الاتجاه المعتاد (إعادة تسعير الرصيد)
        {
            lines.Add(new(r.InventoryAccountCode ?? DefaultAccounts.Inventory, -r.CostAmount, 0));
            lines.Add(new(r.CogsAccountCode ?? DefaultAccounts.Cogs, 0, -r.CostAmount));
        }
        var request = Build(r.Date, r.Description, r.SourceType, r.SourceId, r.SourceNumber, lines, r.IsReturn);
        request.Module = r.Module;
        return PostAsync(request, ct);
    }

    /// <summary>الإيراد موزّعاً على حساب ومركز تكلفة كل سطر (مجمَّعاً)، أو سطراً واحداً بحساب المستند.</summary>
    private static IEnumerable<PostingLine> RevenueLines(SalePostingRequest r)
    {
        var fallback = r.RevenueAccountCode ?? DefaultAccounts.Revenue;
        if (r.RevenueLines.Count == 0) return new[] { new PostingLine(fallback, 0, r.NetAmount, null, r.CostCenterId) };
        if (Math.Abs(r.RevenueLines.Sum(l => l.Amount) - r.NetAmount) > Tolerance)
            throw new ValidationFailedException(Messages.RevenueLinesMustEqualNet);
        return r.RevenueLines.Where(l => l.Amount != 0)
            .GroupBy(l => (Account: string.IsNullOrWhiteSpace(l.AccountCode) ? fallback : l.AccountCode!.Trim(), l.CostCenterId))
            .Select(g => new PostingLine(g.Key.Account, 0, g.Sum(l => l.Amount), null, g.Key.CostCenterId));
    }

    public Task<PostingResult> PostPurchaseAsync(PurchasePostingRequest r, CancellationToken ct = default)
    {
        var total = r.NetAmount + r.VatAmount;
        var payments = ValidatePayments(r.Payments, total);
        var payable = total - payments.Sum(p => p.Amount);

        // الخدمات على حساب مصروف لا على المخزون؛ والمخزون بقيمته الفعلية، وما زاد أو نقص عنها فرق سعر
        // المخزون الدفتري قد يكون سالباً (شراء بآخر سعر أقل أعاد تسعير الرصيد كله بأقل من قيمته)
        if (r.ExpenseAmount < 0 || r.ExpenseAmount - r.NetAmount > Tolerance)
            throw new ValidationFailedException(Messages.PurchaseAmountsInvalid);
        var inventory = r.InventoryAmount ?? r.NetAmount - r.ExpenseAmount;
        var variance = r.NetAmount - r.ExpenseAmount - inventory;
        var inventoryAccount = r.InventoryAccountCode ?? DefaultAccounts.Inventory;
        var varianceAccount = string.IsNullOrWhiteSpace(r.VarianceAccountCode) ? DefaultAccounts.PurchasePriceVariance : r.VarianceAccountCode;

        var lines = new List<PostingLine>();
        if (inventory > 0) lines.Add(new(inventoryAccount, inventory, 0, null, r.CostCenterId));
        else if (inventory < 0) lines.Add(new(inventoryAccount, 0, -inventory, null, r.CostCenterId));
        if (r.ExpenseAmount > 0) lines.Add(new(r.ExpenseAccountCode ?? DefaultAccounts.PurchasedServices, r.ExpenseAmount, 0, null, r.CostCenterId));
        if (variance > 0) lines.Add(new(varianceAccount, variance, 0));
        else if (variance < 0) lines.Add(new(varianceAccount, 0, -variance));
        if (r.VatAmount > 0) lines.Add(new(DefaultAccounts.InputVat, r.VatAmount, 0));
        foreach (var p in payments) lines.Add(new(p.TreasuryAccountCode, 0, p.Amount));
        if (payable > Tolerance)
            lines.Add(new(RequireParty(r.PartyAccountCode, Messages.PartySupplier), 0, payable));
        var request = Build(r.Date, r.Description, r.SourceType, r.SourceId, r.SourceNumber, lines, r.IsReturn);
        request.Module = r.Module;
        return PostAsync(request, ct);
    }

    public Task<PostingResult> PostVoucherAsync(VoucherPostingRequest r, CancellationToken ct = default)
    {
        if (r.Treasury.Count == 0 || r.Treasury.Any(t => t.Amount <= 0))
            throw new ValidationFailedException(Messages.TreasuryAccountAndPositiveAmountRequired);
        var total = r.Treasury.Sum(t => t.Amount);
        var party = RequireParty(r.PartyAccountCode, Messages.PartyGeneric);

        // الضريبة المتضمَّنة تُفصل عن الطرف: مدخلات عند الصرف ومخرجات عند القبض
        if (r.VatAmount < 0 || r.VatAmount >= total) throw new ValidationFailedException(Messages.VoucherVatInvalid);
        var net = total - r.VatAmount;

        var lines = new List<PostingLine>();
        if (r.Type == VoucherType.Receipt)
        {
            foreach (var t in r.Treasury) lines.Add(new(t.TreasuryAccountCode, t.Amount, 0));
            lines.Add(new(party, 0, net));
            if (r.VatAmount > 0) lines.Add(new(DefaultAccounts.OutputVat, 0, r.VatAmount));
        }
        else
        {
            lines.Add(new(party, net, 0));
            if (r.VatAmount > 0) lines.Add(new(DefaultAccounts.InputVat, r.VatAmount, 0));
            foreach (var t in r.Treasury) lines.Add(new(t.TreasuryAccountCode, 0, t.Amount));
        }
        var type = r.Type == VoucherType.Receipt ? "receipt_voucher" : "payment_voucher";
        return PostAsync(new GenericPostingRequest
        {
            Module = r.Module, Date = r.Date, Description = r.Description, SourceType = type, SourceId = r.VoucherId, SourceNumber = r.VoucherNumber, Lines = lines,
        }, ct);
    }

    public async Task<PostingResult> ReverseAsync(Guid journalEntryId, string? reason = null, CancellationToken ct = default)
    {
        var original = await _db.Set<JournalEntry>().Include(e => e.Lines).FirstOrDefaultAsync(e => e.Id == journalEntryId, ct)
            ?? throw new NotFoundException(Messages.JournalEntryNotFound);
        if (original.Status == JournalEntryStatus.Reversed) throw new ConflictException(Messages.JournalEntryAlreadyReversed);
        if (original.SourceType == "reversal") throw new ConflictException(Messages.CannotReverseReversalEntry);
        // قيد في فترة مقفلة لا يُعكس: مستنده (سند، فاتورة، مصروف...) جزء من فترة أُقفلت وربما قُدِّم إقرارها الضريبي،
        // وعكسه بتاريخ اليوم مع تعديل المستند أو حذفه يغيّر أرقام تلك الفترة. التصحيح بمستند جديد في الفترة المفتوحة.
        await _periods.EnsureOpenAsync(original.Date, ct);

        var lines = original.Lines.Select(l => new PostingLine(l.AccountCode, l.Credit, l.Debit, l.Notes, l.CostCenterId)).ToList();
        var result = await PostAsync(new GenericPostingRequest
        {
            Date = original.Date, // العكس في فترة القيد نفسها فيُلغى أثره فيها
            Description = $"عكس القيد {original.EntryNumber}" + (string.IsNullOrWhiteSpace(reason) ? "" : $" - {reason}"),
            Module = original.Module, SourceType = "reversal", SourceId = original.Id, SourceNumber = original.EntryNumber, Lines = lines,
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

    private static string ResolveModule(string? module, string? sourceType)
    {
        if (!string.IsNullOrWhiteSpace(module))
        {
            var normalized = module.Trim().ToLowerInvariant();
            if (PlatformModules.All.Contains(normalized)) return normalized;
            throw new ValidationFailedException("وحدة القيد غير معروفة.");
        }

        if (sourceType?.StartsWith("car_", StringComparison.OrdinalIgnoreCase) == true) return PlatformModules.CarShowroom;
        if (sourceType?.StartsWith("pos_", StringComparison.OrdinalIgnoreCase) == true) return PlatformModules.Pos;
        if (sourceType is "payroll" or "employee_loan" or "end_of_service") return PlatformModules.Hr;
        return PlatformModules.Accounting;
    }

    private static List<PaymentPosting> ValidatePayments(List<PaymentPosting> payments, decimal total)
    {
        if (total < 0) throw new ValidationFailedException(Messages.DocumentTotalCannotBeNegative);
        if (payments.Any(p => p.Amount <= 0 || string.IsNullOrWhiteSpace(p.TreasuryAccountCode)))
            throw new ValidationFailedException(Messages.PaymentAmountsMustBePositiveWithAccount);
        if (payments.Sum(p => p.Amount) - total > Tolerance)
            throw new ValidationFailedException(Messages.PaidExceedsDocumentTotal);
        return payments;
    }

    private static string RequireParty(string? code, string who)
        => string.IsNullOrWhiteSpace(code) ? throw new ValidationFailedException(string.Format(Messages.PartyAccountRequiredForCredit, who)) : code;

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    /// <summary>الرصيد الطبيعي: مدين الطبيعة = مدين - دائن، ودائن الطبيعة = دائن - مدين.</summary>
    internal static void ApplyToBalance(Account account, decimal debit, decimal credit)
        => account.Balance += account.IsDebitNature ? debit - credit : credit - debit;

    /// <summary>رصيد العميل/المورد/البنك المرتبط يتبع رصيد حسابه (مع رصيده الافتتاحي القديم إن وُجد).</summary>
    internal static async Task SyncLinkedEntityBalancesAsync(ErpDbContext db, IEnumerable<Account> touched, CancellationToken ct)
    {
        foreach (var a in touched.Where(a => a.LinkedEntityId.HasValue && a.LinkedEntityType is > LinkedEntityType.General))
        {
            var id = a.LinkedEntityId!.Value;
            switch (a.LinkedEntityType)
            {
                case LinkedEntityType.Customer:
                    var c = await db.Set<Customer>().FirstOrDefaultAsync(x => x.Id == id, ct);
                    if (c != null) c.CurrentBalance = OpeningBalances.Current(a.Balance, c.OpeningBalance, c.OpeningEntryId);
                    break;
                case LinkedEntityType.Supplier:
                    var s = await db.Set<Supplier>().FirstOrDefaultAsync(x => x.Id == id, ct);
                    if (s != null) s.CurrentBalance = OpeningBalances.Current(a.Balance, s.OpeningBalance, s.OpeningEntryId);
                    break;
                case LinkedEntityType.Bank:
                    var b = await db.Set<BankEntity>().FirstOrDefaultAsync(x => x.Id == id, ct);
                    if (b != null) b.CurrentBalance = OpeningBalances.Current(a.Balance, b.OpeningBalance, b.OpeningEntryId);
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
        await SyncLinkedEntityBalancesAsync(_db, accounts, ct);
    }
}
