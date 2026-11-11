using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using K = ERP.Core.Models.Accounting.SiteRentalMovementKinds;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// استحقاق إيجار المعدات المستأجرة وتسويته (انظر <see cref="IRentalAccrualService"/>). القيود:
/// <list type="bullet">
/// <item>الاستحقاق (وفرق إعادة الاعتماد): مدين تكلفة معدات مستأجرة للمشاريع [مركز المشروع] / دائن إيجار معدات مستحق؛ والفرق السالب بالعكس.</item>
/// <item>فاتورة المؤجر: قيدها المعتاد بلا تغيير (مصروف [المركز] + ضريبة مدخلات / مورد).</item>
/// <item>المطابقة: مدين إيجار معدات مستحق / دائن حساب مصروف الفاتورة [المركز] بالمطابَق؛ وفكها بالعكس على الحساب نفسه.</item>
/// <item>الإقفال: مدين إيجار معدات مستحق / دائن تكلفة معدات مستأجرة [المركز]؛ وفكه (إن نقص المستحق بعد إعادة الاعتماد) بالعكس.</item>
/// </list>
/// النتيجة لكل مؤجر ومركز: تكلفة المشروع = المستحق غير المقفل + (الفواتير − المطابَق)، أي لا تُحتسب التكلفة مرتين، والزيادة في
/// الفاتورة تبقى تكلفة فعلية، والنقص يُقفل. الموازنة مبنية على الهدف (لا تراكمية)، فاستدعاؤها مرة ثانية لا يرحّل شيئاً.
/// </summary>
public class RentalAccrualService : IRentalAccrualService
{
    public const string AccrualSource = "site_rental_accrual";
    public const string SettlementSource = "rental_accrual_settlement";
    public const string ClosureSource = "rental_accrual_close";
    /// <summary>مصادر قيود الإيجار المستحق (تُصنَّف «معدات مستأجرة» في تكلفة المشروع).</summary>
    public static readonly string[] Sources = { AccrualSource, SettlementSource, ClosureSource };

    private static readonly string[] PurchaseSources = { "purchase", "purchase_return" };

    private readonly ErpDbContext _db;
    private readonly IAccountingPostingService _posting;
    private readonly ICurrentUser _user;
    private readonly ITransactionRunner _tx;

    public RentalAccrualService(ErpDbContext db, IAccountingPostingService posting, ICurrentUser user, ITransactionRunner tx)
    {
        _db = db; _posting = posting; _user = user; _tx = tx;
    }

    private sealed record Accounts(string Cost, string Accrued);

    private async Task<Accounts> AccountsAsync(CancellationToken ct)
    {
        await ContractAccountsSetup.EnsureMissingAsync(_db, ct);
        var s = await PostingAccounts.SettingsAsync(_db, ct);
        return new Accounts(s.ContractAccount(nameof(AccountingSettings.ProjectRentedEquipmentCostAccountCode)),
            s.ContractAccount(nameof(AccountingSettings.ProjectAccruedRentalAccountCode)));
    }

    // ---------- الاستحقاق من التقرير اليومي ----------

    public async Task PostReportAsync(SiteDailyReport report, CommercialContract contract, int revision, CancellationToken ct = default)
    {
        if (contract.CostCenterId is not Guid cc) return;
        var rented = await _db.Set<ProjectResource>().AsNoTracking()
            .Where(r => r.ContractId == contract.Id && r.Kind == ProjectResourceKinds.RentedEquipment)
            .ToDictionaryAsync(r => r.Id, ct);
        // المستحق الجديد لكل آلية = مجموع تكلفة أسطرها (المثبّتة عند الحفظ بسعر التقرير وشروط فوترتها)
        var target = report.Resources.Where(u => rented.ContainsKey(u.ProjectResourceId))
            .GroupBy(u => u.ProjectResourceId).ToDictionary(g => g.Key, g => DocumentPricing.Round(g.Sum(u => u.Cost)));
        var existing = await _db.Set<SiteRentalAccrual>().Where(a => a.ReportId == report.Id).ToListAsync(ct);

        var changes = new List<(SiteRentalAccrual Accrual, decimal Delta, bool IsNew)>();
        foreach (var resourceId in target.Keys.Union(existing.Select(a => a.ProjectResourceId)).ToList())
        {
            var accrual = existing.FirstOrDefault(a => a.ProjectResourceId == resourceId);
            var delta = target.GetValueOrDefault(resourceId) - (accrual?.Amount ?? 0);
            if (delta == 0) continue;
            var isNew = accrual == null;
            if (accrual == null)
            {
                if (rented[resourceId].SupplierId is not Guid supplier) continue; // المستأجرة تتطلب مؤجراً عند حفظها
                accrual = new SiteRentalAccrual
                {
                    ContractId = contract.Id, CostCenterId = cc, ReportId = report.Id, ReportDate = report.ReportDate.Date,
                    ProjectResourceId = resourceId, SupplierId = supplier,
                };
                _db.Add(accrual);
            }
            changes.Add((accrual, delta, isNew));
        }
        if (changes.Count == 0) return;

        var accounts = await AccountsAsync(ct);
        var names = rented.ToDictionary(kv => kv.Key, kv => kv.Value.Name);
        // الاعتماد الأول بتاريخ التقرير؛ فرق إعادة الاعتماد بتاريخ اليوم (فترة التقرير قد أُقفلت، والقيد الأصلي يبقى كما هو)
        var date = revision <= 1 ? report.ReportDate.Date : SaudiTime.Today;
        var note = $"{(revision <= 1 ? "استحقاق" : "فرق إعادة اعتماد استحقاق")} إيجار معدات التقرير اليومي {report.ReportDate:yyyy-MM-dd} - عقد {contract.ContractNumber}";
        var lines = new List<PostingLine>();
        foreach (var (accrual, delta, _) in changes)
        {
            var text = $"{note} - {names.GetValueOrDefault(accrual.ProjectResourceId)}";
            var amount = Math.Abs(delta);
            if (delta > 0)
            {
                lines.Add(new(accounts.Cost, amount, 0, text, cc));
                lines.Add(new(accounts.Accrued, 0, amount, text));
            }
            else
            {
                lines.Add(new(accounts.Accrued, amount, 0, text));
                lines.Add(new(accounts.Cost, 0, amount, text, cc));
            }
        }
        var posted = await _posting.PostAsync(new GenericPostingRequest
        {
            Date = date, Description = note, SourceType = AccrualSource, SourceId = report.Id, SourceNumber = contract.ContractNumber,
            WarehouseId = contract.WarehouseId, Lines = lines,
        }, ct);
        foreach (var (accrual, delta, isNew) in changes)
        {
            accrual.Amount += delta;
            _db.Add(new SiteRentalAccrualMovement
            {
                AccrualId = accrual.Id, SupplierId = accrual.SupplierId, CostCenterId = cc,
                Kind = isNew && revision <= 1 ? K.Accrual : K.Adjustment, Amount = delta, Revision = revision,
                JournalEntryId = posted.JournalEntryId, AccountCode = accounts.Cost, SourceDocumentId = report.Id, CreatedBy = _user.Name,
            });
        }
        await _db.SaveChangesAsync(ct);

        foreach (var supplier in changes.Select(c => c.Accrual.SupplierId).Distinct())
            await RebalanceAsync(supplier, cc, report.Id, date, contract.WarehouseId, ct);
    }

    // ---------- فواتير المؤجر ----------

    public async Task OnPurchaseDocumentChangedAsync(InvoiceBase document, CancellationToken ct = default)
    {
        if (document.Kind is not (InvoiceKind.Purchase or InvoiceKind.PurchaseReturn) || document.PartyId is not Guid supplier) return;
        // المراكز التي للمؤجر فيها استحقاق فقط (فاتورة مورد لا يؤجّر لأي مشروع لا تمر بشيء)
        var centers = await _db.Set<SiteRentalAccrual>().AsNoTracking().Where(a => a.SupplierId == supplier)
            .Select(a => a.CostCenterId).Distinct().ToListAsync(ct);
        foreach (var cc in centers)
            await RebalanceAsync(supplier, cc, document.Id, document.IssueDate.Date, document.WarehouseId, ct);
    }

    /// <summary>صافي مصروف فواتير المؤجر المرحّلة (ناقص مرتجعاته) على المركز من قيودها، وحساب مصروفها الأكبر.</summary>
    private async Task<(decimal Net, string? Account)> InvoicedAsync(Guid supplier, Guid cc, CancellationToken ct)
    {
        var docs = await _db.Set<PurchaseInvoice>().AsNoTracking().Where(i => i.PartyId == supplier).Select(i => i.Id).ToListAsync(ct);
        docs.AddRange(await _db.Set<PurchaseReturn>().AsNoTracking().Where(i => i.PartyId == supplier).Select(i => i.Id).ToListAsync(ct));
        if (docs.Count == 0) return (0, null);
        var rows = await (from l in _db.Set<JournalEntryLine>().AsNoTracking()
                          join e in _db.Set<JournalEntry>().AsNoTracking() on l.JournalEntryId equals e.Id
                          join a in _db.Set<Account>().AsNoTracking() on l.AccountCode equals a.Code
                          where e.Status == JournalEntryStatus.Posted && PurchaseSources.Contains(e.SourceType!)
                                && e.SourceReferenceId != null && docs.Contains(e.SourceReferenceId.Value)
                                && l.CostCenterId == cc && a.Type == AccountCategory.Expense
                          select new { l.AccountCode, l.Debit, l.Credit }).ToListAsync(ct);
        var account = rows.GroupBy(r => r.AccountCode).OrderByDescending(g => g.Sum(r => r.Debit - r.Credit)).Select(g => g.Key).FirstOrDefault();
        return (DocumentPricing.Round(rows.Sum(r => r.Debit - r.Credit)), account);
    }

    /// <summary>
    /// موازنة المؤجر على المركز: يصحّح كل سطر زاد مسوّاه ومقفله عن مستحقه (بعد إعادة اعتماد بأقل)، ثم يجعل المسوّى =
    /// أقل (الفواتير، المستحق غير المقفل): الزيادة تُطابَق بالأقدم أولاً، والنقص يُفك بالأحدث أولاً. قيد واحد بالفرق فقط.
    /// </summary>
    private async Task RebalanceAsync(Guid supplier, Guid cc, Guid sourceDocumentId, DateTime date, Guid? warehouseId, CancellationToken ct)
    {
        var accruals = await _db.Set<SiteRentalAccrual>().Where(a => a.SupplierId == supplier && a.CostCenterId == cc)
            .OrderBy(a => a.ReportDate).ThenBy(a => a.CreatedAt).ToListAsync(ct);
        if (accruals.Count == 0) return;
        var accounts = await AccountsAsync(ct);
        var (invoiced, invoiceAccount) = await InvoicedAsync(supplier, cc, ct);
        // حساب مطابقة كل سطر سابقاً: فكّها يُعكس على الحساب نفسه ولو حُذفت الفاتورة أو تغيّر حسابها
        var ids = accruals.Select(a => a.Id).ToList();
        var settledAccounts = (await _db.Set<SiteRentalAccrualMovement>().AsNoTracking()
                .Where(m => ids.Contains(m.AccrualId) && m.Kind == K.Settlement && m.Amount > 0)
                .OrderBy(m => m.CreatedAt).Select(m => new { m.AccrualId, m.AccountCode }).ToListAsync(ct))
            .GroupBy(m => m.AccrualId).ToDictionary(g => g.Key, g => g.Last().AccountCode);
        string SettledAccount(SiteRentalAccrual a) => settledAccounts.GetValueOrDefault(a.Id) ?? invoiceAccount ?? DefaultAccounts.PurchasedServices;

        var moves = new List<(SiteRentalAccrual Accrual, string Kind, decimal Amount, string Account)>();
        // 1) سطر نقص مستحقه عمّا سُوّي وأُقفل منه: يُفك الإقفال أولاً ثم المطابقة
        foreach (var a in accruals)
        {
            var excess = a.SettledAmount + a.ClosedAmount - a.Amount;
            if (excess <= 0) continue;
            var unclose = Math.Min(excess, a.ClosedAmount);
            if (unclose > 0) { moves.Add((a, K.Closure, -unclose, accounts.Cost)); a.ClosedAmount -= unclose; excess -= unclose; }
            if (excess > 0) { moves.Add((a, K.Settlement, -excess, SettledAccount(a))); a.SettledAmount -= excess; }
        }
        // 2) المسوّى = أقل (الفواتير، المستحق غير المقفل)
        var settled = accruals.Sum(a => a.SettledAmount);
        var target = Math.Max(0, invoiced);
        if (settled > target)
        {
            var need = settled - target;
            foreach (var a in accruals.AsEnumerable().Reverse().Where(a => a.SettledAmount > 0))
            {
                if (need <= 0) break;
                var take = Math.Min(need, a.SettledAmount);
                moves.Add((a, K.Settlement, -take, SettledAccount(a)));
                a.SettledAmount -= take;
                need -= take;
            }
        }
        else if (settled < target)
        {
            var available = target - settled;
            foreach (var a in accruals.Where(a => a.OpenAmount > 0))
            {
                if (available <= 0) break;
                var take = Math.Min(available, a.OpenAmount);
                moves.Add((a, K.Settlement, take, invoiceAccount ?? DefaultAccounts.PurchasedServices));
                a.SettledAmount += take;
                available -= take;
            }
        }
        if (moves.Count == 0) return;

        var note = $"تسوية إيجار معدات مستحق بفواتير المؤجر - مركز التكلفة";
        var lines = new List<PostingLine>();
        foreach (var (_, kind, amount, account) in moves)
        {
            var value = Math.Abs(amount);
            if (kind == K.Settlement && amount > 0)
            {
                lines.Add(new(accounts.Accrued, value, 0, note));
                lines.Add(new(account, 0, value, note, cc));
            }
            else // فك مطابقة أو فك إقفال: المستحق يعود، والحساب المقابل يعود مديناً على المركز
            {
                lines.Add(new(account, value, 0, note, cc));
                lines.Add(new(accounts.Accrued, 0, value, note));
            }
        }
        var posted = await _posting.PostAsync(new GenericPostingRequest
        {
            Date = date, Description = note, SourceType = SettlementSource, SourceId = sourceDocumentId, WarehouseId = warehouseId, Lines = lines,
        }, ct);
        foreach (var (accrual, kind, amount, account) in moves)
            _db.Add(new SiteRentalAccrualMovement
            {
                AccrualId = accrual.Id, SupplierId = supplier, CostCenterId = cc, Kind = kind, Amount = amount,
                JournalEntryId = posted.JournalEntryId, AccountCode = account, SourceDocumentId = sourceDocumentId, CreatedBy = _user.Name,
            });
        await _db.SaveChangesAsync(ct);
    }

    // ---------- العرض والإقفال ----------

    public async Task<List<SiteRentalAccrualDto>> ListAsync(Guid contractId, bool openOnly, CancellationToken ct = default)
    {
        var rows = await (from a in _db.Set<SiteRentalAccrual>().AsNoTracking()
                          join r in _db.Set<ProjectResource>().AsNoTracking() on a.ProjectResourceId equals r.Id
                          join s in _db.Set<Supplier>().AsNoTracking() on a.SupplierId equals s.Id into sj
                          from s in sj.DefaultIfEmpty()
                          where a.ContractId == contractId
                          orderby a.ReportDate, r.Name
                          select new SiteRentalAccrualDto
                          {
                              Id = a.Id, ReportId = a.ReportId, ReportDate = a.ReportDate, ProjectResourceId = a.ProjectResourceId, ResourceName = r.Name,
                              SupplierId = a.SupplierId, SupplierName = s != null ? s.NameAr : null, Amount = a.Amount, SettledAmount = a.SettledAmount,
                              ClosedAmount = a.ClosedAmount, OpenAmount = a.Amount - a.SettledAmount - a.ClosedAmount,
                          }).ToListAsync(ct);
        return openOnly ? rows.Where(r => r.OpenAmount != 0).ToList() : rows;
    }

    public Task<decimal> CloseAsync(Guid contractId, CloseRentalAccrualDto request, CancellationToken ct = default)
        => _tx.RunAsync(token => CloseCoreAsync(contractId, request, token), ct);

    private async Task<decimal> CloseCoreAsync(Guid contractId, CloseRentalAccrualDto request, CancellationToken ct)
    {
        var note = request.Note?.Trim();
        if (string.IsNullOrEmpty(note)) throw new ValidationFailedException(Messages.RentalAccrualCloseNoteRequired);
        var contract = await _db.Set<CommercialContract>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == contractId, ct)
            ?? throw new NotFoundException(Messages.ContractNotFound);
        var upTo = request.UpTo?.Date;
        var open = (await _db.Set<SiteRentalAccrual>()
                .Where(a => a.ContractId == contractId && a.SupplierId == request.SupplierId && (upTo == null || a.ReportDate <= upTo))
                .ToListAsync(ct))
            .Where(a => a.OpenAmount > 0).ToList();
        if (open.Count == 0) throw new ValidationFailedException(Messages.RentalAccrualNothingToClose);

        var accounts = await AccountsAsync(ct);
        var batch = Guid.NewGuid();
        var text = $"إقفال إيجار معدات مستحق - عقد {contract.ContractNumber}: {note}";
        var lines = new List<PostingLine>();
        foreach (var group in open.GroupBy(a => a.CostCenterId))
        {
            var amount = group.Sum(a => a.OpenAmount);
            lines.Add(new(accounts.Accrued, amount, 0, text));
            lines.Add(new(accounts.Cost, 0, amount, text, group.Key));
        }
        var posted = await _posting.PostAsync(new GenericPostingRequest
        {
            Date = SaudiTime.Today, Description = text, SourceType = ClosureSource, SourceId = batch, SourceNumber = contract.ContractNumber,
            WarehouseId = contract.WarehouseId, Lines = lines,
        }, ct);
        var total = 0m;
        foreach (var a in open)
        {
            var amount = a.OpenAmount;
            a.ClosedAmount += amount;
            total += amount;
            _db.Add(new SiteRentalAccrualMovement
            {
                AccrualId = a.Id, SupplierId = a.SupplierId, CostCenterId = a.CostCenterId, Kind = K.Closure, Amount = amount,
                JournalEntryId = posted.JournalEntryId, AccountCode = accounts.Cost, SourceDocumentId = batch, Note = note, CreatedBy = _user.Name,
            });
        }
        await _db.SaveChangesAsync(ct);
        return total;
    }
}
