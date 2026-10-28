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
    private const string ReversalSource = "reversal";

    private readonly ErpDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly IPeriodLock _periods;

    public AccountingPostingService(ErpDbContext db, INumberSequenceService numbers, IPeriodLock periods)
    {
        _db = db; _numbers = numbers; _periods = periods;
    }

    public async Task<PostingResult> PostAsync(GenericPostingRequest request, CancellationToken ct = default)
    {
        if (request.ReplaceEntryId is { } replaceId) return await ReplaceDocumentEntryAsync(replaceId, request, ct);
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

    /// <summary>
    /// تعديل قيد مستند مرحّل في مكانه (نفس الرقم والمعرّف): يُلغى أثر أسطره القديمة على الأرصدة وتُستبدل بالجديدة.
    /// فترته القديمة والجديدة يجب أن تكونا مفتوحتين، والقيد المطابَق مع البنك أو المعكوس لا يُعدَّل.
    /// </summary>
    private async Task<PostingResult> ReplaceDocumentEntryAsync(Guid entryId, GenericPostingRequest request, CancellationToken ct)
    {
        var entry = await _db.Set<JournalEntry>().Include(e => e.Lines).FirstOrDefaultAsync(e => e.Id == entryId, ct)
            ?? throw new NotFoundException(Messages.JournalEntryNotFound);
        if (entry.Status == JournalEntryStatus.Reversed) throw new ConflictException(Messages.ReversedEntryCannotBeEdited);
        EnsureNotReconciled(entry);
        await _periods.EnsureOpenAsync(entry.Date, ct);
        await _periods.EnsureOpenAsync(request.Date, ct);
        var prepared = await PrepareAsync(request, ct);
        await UnapplyAsync(entry, ct);
        _db.RemoveRange(entry.Lines);
        entry.Lines.Clear();
        entry.Date = request.Date;
        entry.Description = request.Description;
        entry.Module = ResolveModule(request.Module ?? entry.Module, request.SourceType);
        entry.ReferenceNumber = request.SourceNumber;
        Attach(entry, prepared);
        await SyncLinkedEntityBalancesAsync(_db, prepared.Accounts, ct);
        await _db.SaveChangesAsync(ct);
        return new PostingResult(entry.Id, entry.EntryNumber);
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

        var byCode = accounts.ToDictionary(a => a.Code);
        // مركز اختاره المستخدم لحساب في قيد المستند يحلّ محل مركز أسطره، ثم تُطبَّق قواعد المراكز عليه كأي مركز
        if (request.CostCenterByAccount.Count > 0)
            lines = lines.Select(l => request.CostCenterByAccount.TryGetValue(l.AccountCode, out var center) ? l with { CostCenterId = center } : l).ToList();
        lines = await ApplyCostCenterPolicyAsync(request, lines, byCode, ct);
        return new Prepared(lines, accounts, byCode, totalDebit, totalCredit);
    }

    /// <summary>
    /// مراكز التكلفة على الخادم لكل ترحيل: المركز موجود في المنشأة ونشط، والحساب الإلزامي لا يُرحَّل بلا مركز.
    /// الحساب غير المحلَّل بمراكز: يُرفض مركزه في القيد اليدوي (اختيار صريح من المستخدم)، ويُسقط من قيد المستند
    /// لأن المستند يمرّر مركز سطره لكل ما يخصّه ولا يعرف إعداد كل حساب.
    /// قيدا العكس والإقفال صورة لحركات قائمة: يحملان مراكزها كما هي ولو تغيّر إعداد الحساب أو أُوقف المركز بعدها،
    /// وإلا بقي أثر القيد الأصلي في تقارير المراكز أو تعذّر الإقفال.
    /// </summary>
    private async Task<List<PostingLine>> ApplyCostCenterPolicyAsync(GenericPostingRequest request, List<PostingLine> lines,
        Dictionary<string, Account> byCode, CancellationToken ct)
    {
        if (request.SourceType is ReversalSource or FiscalPeriodService.ClosingSource) return lines;
        var manual = IsManual(request.SourceType);
        var result = new List<PostingLine>(lines.Count);
        foreach (var line in lines)
        {
            var account = byCode[line.AccountCode];
            switch (account.CostCenterPolicy)
            {
                case CostCenterPolicy.None when line.CostCenterId.HasValue:
                    if (manual) throw new ValidationFailedException(string.Format(Messages.CostCenterNotAllowedForAccount, account.Code, account.NameAr));
                    result.Add(line with { CostCenterId = null });
                    continue;
                case CostCenterPolicy.Required when !line.CostCenterId.HasValue:
                    throw new ValidationFailedException(string.Format(Messages.CostCenterRequiredForAccount, account.Code, account.NameAr));
            }
            result.Add(line);
        }
        await CostCenterRules.EnsureUsableAsync(_db, result.Select(l => l.CostCenterId), ct, requireActive: !request.AllowInactiveCostCenters);
        return result;
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

    public async Task<PostingResult> PostSaleAsync(SalePostingRequest r, CancellationToken ct = default)
    {
        var accounts = await PostingAccounts.SettingsAsync(_db, ct);
        // الدوري: لا تكلفة مع البيع (تُستنتج من بضاعة آخر المدة)، إلا مستنداً بحساب مخزون محدد يدوياً (السيارات) فيبقى بتكلفته
        var periodic = r.InventoryAccountCode == null && accounts.IsPeriodic(r.Date);
        if (periodic) await PeriodicInventory.EnsureMethodOpeningAsync(_db, this, accounts, r.Date, ct);
        var total = r.NetAmount + r.ExciseTaxAmount + r.VatAmount;
        var payments = ValidatePayments(r.Payments, total);
        var receivable = total - payments.Sum(p => p.Amount);

        var lines = new List<PostingLine>();
        if (!string.IsNullOrWhiteSpace(r.PartyAccountCode))
            lines.AddRange(ThroughParty(r.PartyAccountCode, total, payments, partyIsDebit: true));
        else
        {
            foreach (var p in payments) lines.Add(new(p.TreasuryAccountCode, p.Amount, 0));
            if (receivable > Tolerance)
                lines.Add(new(RequireParty(r.PartyAccountCode, Messages.PartyCustomer), receivable, 0));
        }
        lines.AddRange(RevenueLines(r, r.RevenueAccountCode ?? accounts.SalesRevenue(r.IsReturn)));
        if (r.ExciseTaxAmount > 0) lines.Add(new(accounts.OutputExcise(), 0, r.ExciseTaxAmount));
        if (r.VatAmount > 0) lines.Add(new(accounts.OutputVat(), 0, r.VatAmount));
        var inventoryAccount = r.InventoryAccountCode ?? await accounts.InventoryAsync(_db, r.WarehouseId, ct);
        foreach (var cost in periodic ? Array.Empty<CostCenterAmount>() : CostLines(r))
        {
            if (cost.Amount > 0)
            {
                lines.Add(new(r.CogsAccountCode ?? accounts.Cogs(), cost.Amount, 0, null, cost.CostCenterId));
                lines.Add(new(inventoryAccount, 0, cost.Amount, null, cost.CostCenterId));
            }
            else if (cost.Amount < 0) // تغيّر قيمة المخزون عكس الاتجاه المعتاد (إعادة تسعير الرصيد)
            {
                lines.Add(new(inventoryAccount, -cost.Amount, 0, null, cost.CostCenterId));
                lines.Add(new(r.CogsAccountCode ?? accounts.Cogs(), 0, -cost.Amount, null, cost.CostCenterId));
            }
        }
        var request = Build(r.Date, r.Description, r.SourceType, r.SourceId, r.SourceNumber, lines, r.IsReturn);
        request.Module = r.Module;
        request.ReplaceEntryId = r.ReplaceEntryId;
        request.CostCenterByAccount = r.CostCenterByAccount;
        return await PostAsync(request, ct);
    }

    /// <summary>الإيراد موزّعاً على حساب ومركز تكلفة كل سطر (مجمَّعاً)، أو سطراً واحداً بحساب المستند.</summary>
    private static IEnumerable<PostingLine> RevenueLines(SalePostingRequest r, string fallback)
    {
        if (r.RevenueLines.Count == 0) return new[] { new PostingLine(fallback, 0, r.NetAmount, null, r.CostCenterId) };
        if (Math.Abs(r.RevenueLines.Sum(l => l.Amount) - r.NetAmount) > Tolerance)
            throw new ValidationFailedException(Messages.RevenueLinesMustEqualNet);
        return r.RevenueLines.Where(l => l.Amount != 0)
            .GroupBy(l => (Account: string.IsNullOrWhiteSpace(l.AccountCode) ? fallback : l.AccountCode!.Trim(), l.CostCenterId))
            .Select(g => new PostingLine(g.Key.Account, 0, g.Sum(l => l.Amount), null, g.Key.CostCenterId));
    }

    /// <summary>التكلفة موزّعة على مراكز أسطرها (مجمَّعة لكل مركز)، أو مبلغاً واحداً بلا مركز.</summary>
    private static IEnumerable<CostCenterAmount> CostLines(SalePostingRequest r)
    {
        if (r.CostLines.Count == 0) return new[] { new CostCenterAmount(null, r.CostAmount) };
        if (Math.Abs(r.CostLines.Sum(l => l.Amount) - r.CostAmount) > Tolerance)
            throw new ValidationFailedException(Messages.CostCenterSharesMismatch);
        return r.CostLines.GroupBy(l => l.CostCenterId).Select(g => new CostCenterAmount(g.Key, g.Sum(l => l.Amount)));
    }

    /// <summary>
    /// حصص المراكز من مستند المشتريات (مجمَّعة لكل مركز، ومخزون كل حصة محسوماً)، أو حصة واحدة بمركز المستند.
    /// مجاميع الحصص يجب أن تطابق مبالغ المستند وإلا اختلّ القيد بين المراكز. <paramref name="atDocumentValue"/>: الأصناف بصافيها في المستند
    /// لا بتغيّر قيمة المخزون (الجرد الدوري: لا فرق سعر).
    /// </summary>
    private static List<PurchaseCostCenterShare> PurchaseShares(PurchasePostingRequest r, decimal inventory, bool atDocumentValue)
    {
        if (r.CostCenterShares.Count == 0) return new() { new(r.CostCenterId, r.NetAmount, r.ExpenseAmount, inventory) };
        var shares = r.CostCenterShares.GroupBy(s => s.CostCenterId)
            .Select(g => new PurchaseCostCenterShare(g.Key, g.Sum(s => s.Net), g.Sum(s => s.Expense),
                g.Sum(s => atDocumentValue ? s.Net - s.Expense : s.Inventory ?? s.Net - s.Expense)))
            .ToList();
        if (shares.Any(s => s.Expense < 0)
            || Math.Abs(shares.Sum(s => s.Net) - r.NetAmount) > Tolerance
            || Math.Abs(shares.Sum(s => s.Expense) - r.ExpenseAmount) > Tolerance
            || Math.Abs(shares.Sum(s => s.Inventory!.Value) - inventory) > Tolerance)
            throw new ValidationFailedException(Messages.CostCenterSharesMismatch);
        return shares;
    }

    public async Task<PostingResult> PostPurchaseAsync(PurchasePostingRequest r, CancellationToken ct = default)
    {
        var accounts = await PostingAccounts.SettingsAsync(_db, ct);
        var total = r.NetAmount + r.VatAmount;
        var payments = ValidatePayments(r.Payments, total);
        var payable = total - payments.Sum(p => p.Amount);

        // الخدمات على حساب مصروف لا على المخزون؛ والمخزون بقيمته الفعلية، وما زاد أو نقص عنها فرق سعر
        // المخزون الدفتري قد يكون سالباً (شراء بآخر سعر أقل أعاد تسعير الرصيد كله بأقل من قيمته)
        if (r.ExpenseAmount < 0 || r.ExpenseAmount - r.NetAmount > Tolerance)
            throw new ValidationFailedException(Messages.PurchaseAmountsInvalid);
        // الدوري: الأصناف على حساب المشتريات (والمرتجع على مردوداتها) بسعر المستند، ولا يُمس حساب المخزون داخل الفترة
        var periodic = r.InventoryAccountCode == null && accounts.IsPeriodic(r.Date);
        if (periodic) await PeriodicInventory.EnsureMethodOpeningAsync(_db, this, accounts, r.Date, ct);
        var inventory = periodic ? r.NetAmount - r.ExpenseAmount : r.InventoryAmount ?? r.NetAmount - r.ExpenseAmount;
        var inventoryAccount = r.InventoryAccountCode
            ?? (periodic ? (r.IsReturn ? accounts.PurchaseReturns() : accounts.Purchases()) : await accounts.InventoryAsync(_db, r.WarehouseId, ct));
        var varianceAccount = string.IsNullOrWhiteSpace(r.VarianceAccountCode) ? DefaultAccounts.PurchasePriceVariance : r.VarianceAccountCode;

        // كل مركز بمخزونه وخدماته وفرق سعره: لا يُنسب مركز سطر لأسطر غيره
        var lines = new List<PostingLine>();
        foreach (var share in PurchaseShares(r, inventory, atDocumentValue: periodic))
        {
            var shareInventory = share.Inventory!.Value;
            var shareVariance = share.Net - share.Expense - shareInventory;
            if (shareInventory > 0) lines.Add(new(inventoryAccount, shareInventory, 0, null, share.CostCenterId));
            else if (shareInventory < 0) lines.Add(new(inventoryAccount, 0, -shareInventory, null, share.CostCenterId));
            if (share.Expense > 0) lines.Add(new(r.ExpenseAccountCode ?? DefaultAccounts.PurchasedServices, share.Expense, 0, null, share.CostCenterId));
            if (shareVariance > 0) lines.Add(new(varianceAccount, shareVariance, 0, null, share.CostCenterId));
            else if (shareVariance < 0) lines.Add(new(varianceAccount, 0, -shareVariance, null, share.CostCenterId));
        }
        if (r.VatAmount > 0) lines.Add(new(accounts.InputVat(), r.VatAmount, 0));
        if (!string.IsNullOrWhiteSpace(r.PartyAccountCode))
            lines.AddRange(ThroughParty(r.PartyAccountCode, total, payments, partyIsDebit: false));
        else
        {
            foreach (var p in payments) lines.Add(new(p.TreasuryAccountCode, 0, p.Amount));
            if (payable > Tolerance)
                lines.Add(new(RequireParty(r.PartyAccountCode, Messages.PartySupplier), 0, payable));
        }
        var request = Build(r.Date, r.Description, r.SourceType, r.SourceId, r.SourceNumber, lines, r.IsReturn);
        request.Module = r.Module;
        request.ReplaceEntryId = r.ReplaceEntryId;
        request.CostCenterByAccount = r.CostCenterByAccount;
        return await PostAsync(request, ct);
    }

    /// <summary>
    /// مستند طرف معروف (عميل/مورد) يُقيَّد كاملاً على حسابه ثم يُسدَّد المدفوع منه: صافي الطرف هو الآجل وحده كما كان،
    /// لكن حركته تظهر في ميزان المراجعة وكشف حسابه ولو دُفع المستند كله نقداً.
    /// </summary>
    private static IEnumerable<PostingLine> ThroughParty(string party, decimal total, List<PaymentPosting> payments, bool partyIsDebit)
    {
        yield return partyIsDebit ? new(party, total, 0) : new(party, 0, total);
        foreach (var p in payments)
        {
            yield return partyIsDebit ? new(party, 0, p.Amount) : new(party, p.Amount, 0);
            yield return partyIsDebit ? new(p.TreasuryAccountCode, p.Amount, 0) : new(p.TreasuryAccountCode, 0, p.Amount);
        }
    }

    public async Task<PostingResult> PostVoucherAsync(VoucherPostingRequest r, CancellationToken ct = default)
    {
        var accounts = await PostingAccounts.SettingsAsync(_db, ct);
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
            lines.Add(new(party, 0, net, null, r.CostCenterId));
            if (r.VatAmount > 0) lines.Add(new(accounts.OutputVat(), 0, r.VatAmount));
        }
        else
        {
            lines.Add(new(party, net, 0, null, r.CostCenterId));
            if (r.VatAmount > 0) lines.Add(new(accounts.InputVat(), r.VatAmount, 0));
            foreach (var t in r.Treasury) lines.Add(new(t.TreasuryAccountCode, 0, t.Amount));
        }
        var type = r.Type == VoucherType.Receipt ? "receipt_voucher" : "payment_voucher";
        return await PostAsync(new GenericPostingRequest
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
            Module = original.Module, SourceType = ReversalSource, SourceId = original.Id, SourceNumber = original.EntryNumber, Lines = lines,
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
            AllowInactiveCostCenters = reverse,
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
