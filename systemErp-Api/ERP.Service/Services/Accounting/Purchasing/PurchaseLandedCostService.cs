using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.CarShowroom;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// سند التكاليف الإضافية للمشتريات العامة (فواتير PurchaseInvoice وحدها؛ فواتير السيارات في جدول آخر لا يُمس).
/// كل سطر يُوزَّع على أسطر الأصناف بكمياتها الصافية بعد المرتجعات. عند الاعتماد ترتفع تكلفة حركة الشراء لكل سطر صنف بحصته
/// على الكمية الصافية، ويُعاد بناء تكلفة الصنف من حركاته: ما تغيّرت به قيمة المخزون فعلاً يُقيَّد على حسابه، والباقي
/// (حصة ما خرج من المخزون) على تكلفة المبيعات، أو فرق السعر في التكلفة المعيارية وآخر سعر شراء. الضريبة المستردة منفصلة.
/// </summary>
public class PurchaseLandedCostService : IPurchaseLandedCostService
{
    public const string ApproveSource = "purchase_landed_cost";
    public const string CancelSource = "purchase_landed_cost_cancel";

    private readonly ErpDbContext _db;
    private readonly ITransactionRunner _tx;
    private readonly INumberSequenceService _numbers;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;
    private readonly IAccountingPostingService _posting;

    public PurchaseLandedCostService(ErpDbContext db, ITransactionRunner tx, INumberSequenceService numbers, ICurrentUser user, IAuditService audit,
        IAccountingPostingService posting)
    {
        _db = db; _tx = tx; _numbers = numbers; _user = user; _audit = audit; _posting = posting;
    }

    private IQueryable<PurchaseLandedCost> Vouchers => _db.Set<PurchaseLandedCost>()
        .Include(v => v.Invoices).Include(v => v.Lines).ThenInclude(l => l.Allocations).AsSplitQuery();

    public async Task<PagedResult<PurchaseLandedCostDto>> ListAsync(PaginationParams p, CancellationToken ct = default)
    {
        var q = _db.Set<PurchaseLandedCost>().AsNoTracking().AsQueryable();
        if (Enum.TryParse<PurchaseLandedCostStatus>(p.Status, true, out var status)) q = q.Where(v => v.Status == status);
        if (p.StartDate.HasValue) q = q.Where(v => v.Date >= p.StartDate);
        if (p.EndDate.HasValue) q = q.Where(v => v.Date <= p.EndDate);
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(v => v.VoucherNumber.Contains(t) || (v.SupplierName != null && v.SupplierName.Contains(t))
                || (v.SupplierInvoiceNumber != null && v.SupplierInvoiceNumber.Contains(t)) || v.Invoices.Any(i => i.InvoiceNumber.Contains(t)));
        }
        var total = await q.CountAsync(ct);
        var items = await q.Include(v => v.Invoices).Include(v => v.Lines).ThenInclude(l => l.Allocations).AsSplitQuery()
            .OrderByDescending(v => v.Date).ThenByDescending(v => v.VoucherNumber)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);
        var costs = await UnitCostsAsync(items.SelectMany(v => v.Lines).SelectMany(l => l.Allocations).Select(a => a.InvoiceItemId), ct);
        return new PagedResult<PurchaseLandedCostDto>
        {
            Items = items.Select(v => ToDto(v, costs)).ToList(), TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    public async Task<PurchaseLandedCostDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var voucher = await Vouchers.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct) ?? throw new NotFoundException(Messages.PurchaseLandedCostNotFound);
        return ToDto(voucher, await UnitCostsAsync(voucher.Lines.SelectMany(l => l.Allocations).Select(a => a.InvoiceItemId), ct));
    }

    public async Task<PurchaseLandedCostDto> PreviewAsync(SavePurchaseLandedCostDto dto, CancellationToken ct = default)
    {
        var voucher = await BuildAsync(dto, ct);
        return ToDto(voucher, await UnitCostsAsync(voucher.Lines.SelectMany(l => l.Allocations).Select(a => a.InvoiceItemId), ct));
    }

    public Task<PurchaseLandedCostDto> CreateAsync(SavePurchaseLandedCostDto dto, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var voucher = await BuildAsync(dto, token);
            voucher.VoucherNumber = await _numbers.NextAsync("purchase_landed_cost", "PLC-", token);
            _db.Add(voucher);
            await _db.SaveChangesAsync(token);
            return await GetAsync(voucher.Id, token);
        }, ct);

    public Task<PurchaseLandedCostDto> UpdateAsync(Guid id, SavePurchaseLandedCostDto dto, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var voucher = await Vouchers.FirstOrDefaultAsync(v => v.Id == id, token) ?? throw new NotFoundException(Messages.PurchaseLandedCostNotFound);
            if (voucher.Status != PurchaseLandedCostStatus.Draft) throw new ConflictException(Messages.PurchaseLandedCostNotDraft);
            Replace(voucher, await BuildAsync(dto, token));
            await _db.SaveChangesAsync(token);
            return await GetAsync(id, token);
        }, ct);

    public Task<PurchaseLandedCostDto> ApproveAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var voucher = await Vouchers.FirstOrDefaultAsync(v => v.Id == id, token) ?? throw new NotFoundException(Messages.PurchaseLandedCostNotFound);
            if (voucher.Status != PurchaseLandedCostStatus.Draft) throw new ConflictException(Messages.PurchaseLandedCostNotDraft);
            // التوزيع يُعاد على حال الفواتير الآن: مرتجع بعد حفظ المسودة يُنقص كميتها الصافية
            Replace(voucher, await BuildAsync(ToSaveDto(voucher), token));
            await _db.SaveChangesAsync(token);

            var policy = await _db.Set<CostingPolicy>().AsNoTracking().FirstOrDefaultAsync(token) ?? new CostingPolicy();
            (Dictionary<Guid, decimal> ValueChange, CostingMethod Method) effect;
            if (policy.IncludeFreightAndCustoms)
            {
                var allocations = voucher.Lines.SelectMany(l => l.Allocations).Where(a => a.Amount > 0).ToList();
                var movements = await PurchaseMovementsAsync(voucher.Invoices.Select(i => i.InvoiceId).ToList(), allocations, token);
                foreach (var a in allocations)
                {
                    a.StockMovementId = movements[a.InvoiceItemId].Id;
                    a.UnitCostIncrease = Math.Round(a.Amount / a.NetQuantity, 4);
                }
                var increases = allocations.GroupBy(a => a.StockMovementId!.Value).ToDictionary(g => g.Key, g => g.Sum(a => a.UnitCostIncrease));
                effect = await ApplyCostAsync(increases, voucher, $"سند تكاليف إضافية للمشتريات {voucher.VoucherNumber}", token);
            }
            else
            {
                // «تضمين أعباء الشحن في التكلفة» موقوف: لا تُرفع تكلفة الأصناف (ولا تُربط التوزيعات بحركات، فالإلغاء لا يعكس تكلفة)،
                // فيُحمَّل السند كله مصروفاً على الحساب المقابل في القيد
                effect = (new Dictionary<Guid, decimal>(), policy.Method);
            }
            var posted = await PostAsync(voucher, effect, sign: 1, voucher.Date, ApproveSource,
                $"سند تكاليف إضافية للمشتريات {voucher.VoucherNumber}", token);

            voucher.Status = PurchaseLandedCostStatus.Approved;
            voucher.ApprovedAt = DateTime.UtcNow; voucher.ApprovedBy = _user.Email ?? _user.Name;
            voucher.JournalEntryId = posted.EntryId;
            voucher.InventoryAmount = posted.Inventory; voucher.ExpensedAmount = posted.Expensed; voucher.ExpensedAccountCode = posted.ExpensedAccount;
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("PURCHASE_LANDED_COST_APPROVED", nameof(PurchaseLandedCost), voucher.Id.ToString(),
                $"{voucher.VoucherNumber}: تحميل {voucher.BaseCostTotal:0.00} على الأصناف (مخزون {posted.Inventory:0.00}، {posted.ExpensedAccount} {posted.Expensed:0.00}) - فواتير {string.Join("، ", voucher.Invoices.Select(i => i.InvoiceNumber))}", token);
            return await GetAsync(id, token);
        }, ct);

    public Task<PurchaseLandedCostDto> CancelAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var voucher = await Vouchers.FirstOrDefaultAsync(v => v.Id == id, token) ?? throw new NotFoundException(Messages.PurchaseLandedCostNotFound);
            if (voucher.Status == PurchaseLandedCostStatus.Cancelled) throw new ConflictException(Messages.PurchaseLandedCostAlreadyCancelled);
            if (voucher.Status == PurchaseLandedCostStatus.Approved)
            {
                // تُعاد تكلفة الحركات بما رُفعت به تماماً، والقيد المعاكس يُحسب بأثر ذلك الآن (قد بيع بعد الاعتماد ما كان مخزوناً)
                var increases = voucher.Lines.SelectMany(l => l.Allocations).Where(a => a.StockMovementId.HasValue)
                    .GroupBy(a => a.StockMovementId!.Value).ToDictionary(g => g.Key, g => -g.Sum(a => a.UnitCostIncrease));
                var effect = await ApplyCostAsync(increases, voucher, $"إلغاء سند تكاليف إضافية للمشتريات {voucher.VoucherNumber}", token);
                var posted = await PostAsync(voucher, effect, sign: -1, SaudiTime.Today, CancelSource,
                    $"إلغاء سند تكاليف إضافية للمشتريات {voucher.VoucherNumber}", token);
                voucher.CancelJournalEntryId = posted.EntryId;
            }
            voucher.Status = PurchaseLandedCostStatus.Cancelled;
            voucher.CancelledAt = DateTime.UtcNow; voucher.CancelledBy = _user.Email ?? _user.Name;
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("PURCHASE_LANDED_COST_CANCELLED", nameof(PurchaseLandedCost), voucher.Id.ToString(), voucher.VoucherNumber, token);
            return await GetAsync(id, token);
        }, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var voucher = await Vouchers.FirstOrDefaultAsync(v => v.Id == id, token) ?? throw new NotFoundException(Messages.PurchaseLandedCostNotFound);
            if (voucher.Status != PurchaseLandedCostStatus.Draft) throw new ConflictException(Messages.PurchaseLandedCostNotDraft);
            _db.RemoveRange(voucher.Lines.SelectMany(l => l.Allocations));
            _db.RemoveRange(voucher.Lines);
            _db.RemoveRange(voucher.Invoices);
            _db.Remove(voucher);
            await _db.SaveChangesAsync(token);
        }, ct);

    // ---------- البناء والتوزيع ----------

    /// <summary>سطر صنف في فاتورة شراء مرتبطة: كميته الصافية وأوزان توزيعه.</summary>
    private sealed record Target(Guid InvoiceId, string InvoiceNumber, Guid InvoiceItemId, Guid ItemId, string ItemName, decimal NetQuantity, decimal Value, decimal? Weight);

    private async Task<PurchaseLandedCost> BuildAsync(SavePurchaseLandedCostDto d, CancellationToken ct)
    {
        var invoiceIds = d.InvoiceIds.Where(i => i != Guid.Empty).Distinct().ToList();
        if (invoiceIds.Count == 0) throw new ValidationFailedException(Messages.PurchaseLandedCostInvoicesRequired);
        if (d.Lines.Count == 0) throw new ValidationFailedException(Messages.PurchaseLandedCostLinesRequired);
        var targets = await TargetsAsync(invoiceIds, ct);

        var voucher = new PurchaseLandedCost
        {
            Date = (d.Date ?? SaudiTime.Today).Date, Payee = d.Payee,
            SupplierInvoiceNumber = string.IsNullOrWhiteSpace(d.SupplierInvoiceNumber) ? null : d.SupplierInvoiceNumber.Trim(),
            Notes = string.IsNullOrWhiteSpace(d.Notes) ? null : d.Notes.Trim(),
        };
        switch (d.Payee)
        {
            case PurchaseCostPayee.Supplier:
                var supplier = d.SupplierId.HasValue ? await _db.Set<Supplier>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == d.SupplierId, ct) : null;
                if (supplier == null) throw new ValidationFailedException(Messages.PurchaseLandedCostPayeeInvalid);
                (voucher.SupplierId, voucher.SupplierName) = (supplier.Id, supplier.NameAr);
                break;
            case PurchaseCostPayee.PaymentAccount:
                var code = d.PaymentAccountCode?.Trim();
                if (string.IsNullOrEmpty(code)) throw new ValidationFailedException(Messages.PurchaseLandedCostPayeeInvalid);
                if (!await _db.Set<Account>().AnyAsync(a => a.Code == code, ct)) throw new ValidationFailedException(Messages.AccountNotFound);
                voucher.PaymentAccountCode = code;
                break;
            case PurchaseCostPayee.Accrued:
                break;
            default:
                throw new ValidationFailedException(Messages.PurchaseLandedCostPayeeInvalid);
        }
        (voucher.CurrencyCode, voucher.ExchangeRate) = await ChargeShares.ResolveCurrencyAsync(_db, d.CurrencyCode, d.ExchangeRate, ct);
        foreach (var number in targets.GroupBy(t => t.InvoiceId).Select(g => (g.Key, g.First().InvoiceNumber)))
            voucher.Invoices.Add(new PurchaseLandedCostInvoice { InvoiceId = number.Key, InvoiceNumber = number.InvoiceNumber });

        var typeIds = d.Lines.Select(l => l.ExpenseTypeId).Distinct().ToList();
        var types = await _db.Set<PurchaseExpenseType>().AsNoTracking().Where(t => typeIds.Contains(t.Id) && t.IsActive).ToDictionaryAsync(t => t.Id, ct);
        var lineNo = 0;
        foreach (var src in d.Lines)
        {
            if (!types.TryGetValue(src.ExpenseTypeId, out var type)) throw new ValidationFailedException(Messages.PurchaseExpenseTypeNotFound);
            if (src.Amount <= 0) throw new ValidationFailedException(Messages.PurchaseLandedCostAmountInvalid);
            if (src.VatAmount < 0) throw new ValidationFailedException(Messages.ChargeVatCannotBeNegative);
            // ضريبة مقطوعة بأي مبلغ، لكن لا على نوع غير خاضع ولا على ما مبلغه كله ضريبة
            if (src.VatAmount > 0 && (type.IsInputTax || type.VatCategory != VatCategory.Standard))
                throw new ValidationFailedException(Messages.ChargeVatNotAllowedForType);
            var method = src.AllocationMethod ?? type.DefaultAllocationMethod;
            if (!Enum.IsDefined(method)) throw new ValidationFailedException(Messages.PurchaseExpenseTypeValuesInvalid);

            var baseAmount = DocumentPricing.Round(src.Amount * voucher.ExchangeRate);
            var baseVat = DocumentPricing.Round(src.VatAmount * voucher.ExchangeRate);
            var line = new PurchaseLandedCostLine
            {
                LineNo = ++lineNo, ExpenseTypeId = type.Id, ExpenseTypeCode = type.Code, ExpenseTypeNameAr = type.NameAr,
                Description = string.IsNullOrWhiteSpace(src.Description) ? null : src.Description.Trim(),
                Amount = src.Amount, VatAmount = src.VatAmount, BaseAmount = baseAmount, BaseVatAmount = baseVat, AllocationMethod = method,
                // ضريبة الاستيراد: مبلغها كله ضريبة مدخلات (أو تكلفة إن لم تُسترد)؛ وغيرها: المبلغ تكلفة وضريبته مدخلات إن استُردت
                CostAmount = type.IsInputTax ? (type.IsVatRecoverable ? 0 : baseAmount) : baseAmount + (type.IsVatRecoverable ? 0 : baseVat),
                InputVatAmount = type.IsInputTax ? (type.IsVatRecoverable ? baseAmount : 0) : (type.IsVatRecoverable ? baseVat : 0),
                CapitalizeToItemCost = type.CapitalizeToItemCost,
            };
            // نوع لا يدخل تكلفة الأصناف (شحن، نقل، تأمين): مبلغه مصروف على حساب النوع في قيد السند، ولا يُوزَّع على الأصناف
            if (!type.CapitalizeToItemCost)
            {
                (line.ExpenseAmount, line.CostAmount) = (line.CostAmount, 0);
                line.ExpenseAccountCode = type.ExpenseAccountCode ?? DefaultAccounts.PurchaseFreightExpense;
            }
            if (line.CostAmount > 0)
            {
                var shares = Allocate(method, line.CostAmount, targets, src.Manual);
                for (var i = 0; i < targets.Count; i++)
                {
                    if (shares[i] == 0) continue;
                    var t = targets[i];
                    line.Allocations.Add(new PurchaseLandedCostAllocation
                    {
                        InvoiceId = t.InvoiceId, InvoiceNumber = t.InvoiceNumber, InvoiceItemId = t.InvoiceItemId, ItemId = t.ItemId, ItemName = t.ItemName,
                        NetQuantity = t.NetQuantity, Amount = shares[i],
                    });
                }
            }
            voucher.Lines.Add(line);
        }
        voucher.Subtotal = voucher.Lines.Sum(l => l.Amount);
        voucher.VatTotal = voucher.Lines.Sum(l => l.VatAmount);
        voucher.GrandTotal = voucher.Subtotal + voucher.VatTotal;
        voucher.BaseCostTotal = voucher.Lines.Sum(l => l.CostAmount);
        voucher.BaseInputVat = voucher.Lines.Sum(l => l.InputVatAmount);
        return voucher;
    }

    /// <summary>أسطر الأصناف في فواتير الشراء المرحّلة بكمياتها الصافية بعد المرتجعات المرحّلة (المرتجع يُخصم من أسطر صنفه بترتيبها).</summary>
    private async Task<List<Target>> TargetsAsync(List<Guid> invoiceIds, CancellationToken ct)
    {
        var invoices = await _db.Set<PurchaseInvoice>().AsNoTracking().Include(i => i.Items).Where(i => invoiceIds.Contains(i.Id)).ToListAsync(ct);
        foreach (var id in invoiceIds)
        {
            var invoice = invoices.FirstOrDefault(i => i.Id == id);
            if (invoice == null || invoice.Kind != InvoiceKind.Purchase || invoice.IsReturn || invoice.Status != "posted")
                throw new ValidationFailedException(string.Format(Messages.PurchaseLandedCostInvoiceInvalid, invoice?.InvoiceNumber ?? id.ToString()));
        }
        var returned = (await _db.Set<PurchaseReturn>().AsNoTracking().Include(r => r.Items)
                .Where(r => r.OriginalInvoiceId != null && invoiceIds.Contains(r.OriginalInvoiceId.Value) && r.Status == "posted").ToListAsync(ct))
            .SelectMany(r => r.Items.Select(i => (Invoice: r.OriginalInvoiceId!.Value, i.ItemId, i.Quantity)))
            .GroupBy(x => (x.Invoice, x.ItemId)).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        var productIds = invoices.SelectMany(i => i.Items).Select(i => i.ItemId).Where(i => i != Guid.Empty).Distinct().ToList();
        var weights = await _db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.WeightKg, ct);

        var targets = new List<Target>();
        foreach (var invoice in invoiceIds.Select(id => invoices.First(i => i.Id == id)))
            foreach (var group in invoice.Items.Where(i => i.ItemId != Guid.Empty && i.Quantity > 0).OrderBy(i => i.CreatedAt).GroupBy(i => i.ItemId))
            {
                var left = returned.GetValueOrDefault((invoice.Id, group.Key));
                foreach (var item in group)
                {
                    var less = Math.Min(left, item.Quantity);
                    left -= less;
                    var net = item.Quantity - less;
                    if (net <= 0) continue;
                    var value = DocumentPricing.Round(item.TotalBeforeVat * invoice.ExchangeRate * net / item.Quantity);
                    var unitWeight = weights.GetValueOrDefault(item.ItemId);
                    targets.Add(new Target(invoice.Id, invoice.InvoiceNumber, item.Id, item.ItemId, item.ItemName, net, value,
                        unitWeight is > 0 ? unitWeight * net : null));
                }
            }
        if (targets.Count == 0) throw new ValidationFailedException(Messages.PurchaseLandedCostNoStockLines);
        return targets;
    }

    private static decimal[] Allocate(PurchaseCostAllocationMethod method, decimal amount, List<Target> targets, List<ManualPurchaseCostShareDto>? manual)
    {
        switch (method)
        {
            case PurchaseCostAllocationMethod.Manual:
                var given = (manual ?? new()).GroupBy(m => m.InvoiceItemId).ToDictionary(g => g.Key, g => DocumentPricing.Round(g.Sum(m => m.Amount)));
                var shares = targets.Select(t => given.GetValueOrDefault(t.InvoiceItemId)).ToArray();
                if (given.Keys.Any(k => targets.All(t => t.InvoiceItemId != k)) || shares.Any(s => s < 0) || shares.Sum() != amount)
                    throw new ValidationFailedException(string.Format(Messages.PurchaseLandedCostManualMismatch, given.Values.Sum(), amount));
                return shares;
            case PurchaseCostAllocationMethod.ByWeight:
                var missing = targets.FirstOrDefault(t => t.Weight is not > 0);
                if (missing != null) throw new ValidationFailedException(string.Format(Messages.PurchaseLandedCostWeightMissing, missing.ItemName));
                return ChargeAllocator.Allocate(amount, targets.Select(t => t.Weight!.Value).ToList());
            case PurchaseCostAllocationMethod.ByValue when targets.Sum(t => t.Value) > 0:
                return ChargeAllocator.Allocate(amount, targets.Select(t => t.Value).ToList());
            default:
                // بالكمية، أو بالقيمة لأصناف بلا قيمة (هدايا مورد)
                return ChargeAllocator.Allocate(amount, targets.Select(t => t.NetQuantity).ToList());
        }
    }

    private static SavePurchaseLandedCostDto ToSaveDto(PurchaseLandedCost v) => new()
    {
        Date = v.Date, InvoiceIds = v.Invoices.Select(i => i.InvoiceId).ToList(), Payee = v.Payee, SupplierId = v.SupplierId,
        PaymentAccountCode = v.PaymentAccountCode, SupplierInvoiceNumber = v.SupplierInvoiceNumber, CurrencyCode = v.CurrencyCode,
        ExchangeRate = v.ExchangeRate, Notes = v.Notes,
        Lines = v.Lines.OrderBy(l => l.LineNo).Select(l => new SavePurchaseLandedCostLineDto
        {
            ExpenseTypeId = l.ExpenseTypeId, Description = l.Description, Amount = l.Amount, VatAmount = l.VatAmount, AllocationMethod = l.AllocationMethod,
            Manual = l.AllocationMethod == PurchaseCostAllocationMethod.Manual
                ? l.Allocations.Select(a => new ManualPurchaseCostShareDto { InvoiceItemId = a.InvoiceItemId, Amount = a.Amount }).ToList() : null,
        }).ToList(),
    };

    private void Replace(PurchaseLandedCost voucher, PurchaseLandedCost fresh)
    {
        _db.RemoveRange(voucher.Lines.SelectMany(l => l.Allocations));
        _db.RemoveRange(voucher.Lines);
        _db.RemoveRange(voucher.Invoices);
        voucher.Lines.Clear();
        voucher.Invoices.Clear();
        voucher.Date = fresh.Date; voucher.Payee = fresh.Payee; voucher.SupplierId = fresh.SupplierId; voucher.SupplierName = fresh.SupplierName;
        voucher.PaymentAccountCode = fresh.PaymentAccountCode; voucher.SupplierInvoiceNumber = fresh.SupplierInvoiceNumber;
        voucher.CurrencyCode = fresh.CurrencyCode; voucher.ExchangeRate = fresh.ExchangeRate; voucher.Notes = fresh.Notes;
        voucher.Subtotal = fresh.Subtotal; voucher.VatTotal = fresh.VatTotal; voucher.GrandTotal = fresh.GrandTotal;
        voucher.BaseCostTotal = fresh.BaseCostTotal; voucher.BaseInputVat = fresh.BaseInputVat;
        foreach (var invoice in fresh.Invoices) { voucher.Invoices.Add(invoice); _db.Add(invoice); }
        foreach (var line in fresh.Lines) { voucher.Lines.Add(line); _db.Add(line); }
    }

    // ---------- أثر التكلفة والقيد ----------

    /// <summary>
    /// حركة الشراء لكل سطر صنف. فاتورة عُدِّلت بعد ترحيلها لها حركات قديمة عُكست: تُؤخذ حركات آخر ترحيل وحدها.
    /// يُطابَق السطر بحركته بالصنف والكمية بترتيب الإنشاء.
    /// </summary>
    private async Task<Dictionary<Guid, StockMovement>> PurchaseMovementsAsync(List<Guid> invoiceIds, List<PurchaseLandedCostAllocation> allocations, CancellationToken ct)
    {
        var itemIds = allocations.Select(a => a.InvoiceItemId).Distinct().ToList();
        var lines = await _db.Set<PurchaseInvoiceItem>().AsNoTracking().Where(i => itemIds.Contains(i.Id)).OrderBy(i => i.CreatedAt).ToListAsync(ct);
        var moves = await _db.Set<StockMovement>().Where(m => m.SourceId != null && invoiceIds.Contains(m.SourceId.Value)
            && (m.SourceType == "invoice" || m.SourceType == "invoice_reversal")).OrderBy(m => m.CreatedAt).ToListAsync(ct);
        var result = new Dictionary<Guid, StockMovement>();
        foreach (var invoiceLines in lines.GroupBy(l => l.InvoiceId))
        {
            var mine = moves.Where(m => m.SourceId == invoiceLines.Key).ToList();
            var lastReversal = mine.Where(m => m.SourceType == "invoice_reversal").Select(m => (DateTime?)m.CreatedAt).Max();
            var current = mine.Where(m => m.SourceType == "invoice" && m.Type == StockMovementType.InPurchase && (lastReversal == null || m.CreatedAt > lastReversal)).ToList();
            foreach (var line in invoiceLines)
            {
                var movement = current.FirstOrDefault(m => m.ItemId == line.ItemId && m.Quantity == line.Quantity)
                    ?? current.FirstOrDefault(m => m.ItemId == line.ItemId)
                    ?? throw new ConflictException(string.Format(Messages.PurchaseLandedCostInvoiceInvalid, line.ItemName));
                current.Remove(movement);
                result[line.Id] = movement;
            }
        }
        return result;
    }

    /// <summary>
    /// يغيّر تكلفة حركات الشراء ويعيد بناء تكلفة أصنافها. التغيّر يُحسب بإعادة البناء قبل التغيير وبعده (فلا يدخل فيه أي فرق سابق
    /// بين التكلفة الحية وحركاتها) ثم يُضاف للمتوسط الحي. يُرجع تغيّر قيمة كل صنف كما يراه حساب المخزون.
    /// </summary>
    private async Task<(Dictionary<Guid, decimal> ValueChange, CostingMethod Method)> ApplyCostAsync(Dictionary<Guid, decimal> increases,
        PurchaseLandedCost voucher, string notes, CancellationToken ct)
    {
        var policy = await _db.Set<CostingPolicy>().AsNoTracking().FirstOrDefaultAsync(ct) ?? new CostingPolicy();
        var ids = increases.Keys.ToList();
        var touched = await _db.Set<StockMovement>().Where(m => ids.Contains(m.Id)).Select(m => m.ItemId).Distinct().ToListAsync(ct);
        var change = new Dictionary<Guid, decimal>();
        foreach (var itemId in touched)
        {
            var product = await _db.Set<Product>().FirstAsync(p => p.Id == itemId, ct);
            var ordered = (await _db.Set<StockMovement>().Where(m => m.ItemId == itemId).ToListAsync(ct)).OrderBy(m => m.Date).ThenBy(m => m.CreatedAt).ToList();
            var before = Replayed(product, ordered, policy);
            foreach (var m in ordered.Where(m => increases.ContainsKey(m.Id))) m.UnitCost = Math.Max(0, m.UnitCost + increases[m.Id]);
            var after = Replayed(product, ordered, policy);

            var liveBefore = InventoryValuation.Value(product);
            var delta = InventoryValuation.Value(after) - InventoryValuation.Value(before);
            if (product.CurrentStock > 0 && delta != 0)
                product.AverageCost = Math.Max(0, Math.Round(product.AverageCost + delta / product.CurrentStock, 4));
            if (after.LastPurchaseCost != before.LastPurchaseCost) product.LastPurchaseCost = after.LastPurchaseCost;
            change[itemId] = InventoryValuation.Value(product) - liveBefore;
            InventoryValuation.AddRevaluation(_db, product, change[itemId], voucher.VoucherNumber, notes, voucher.Id); // أثر السند في سجل الصنف
        }
        await _db.SaveChangesAsync(ct);
        return (change, policy.Method);
    }

    private static Product Replayed(Product product, List<StockMovement> ordered, CostingPolicy policy)
    {
        var copy = new Product { StandardCost = product.StandardCost, LastPurchaseCost = 0 };
        StockReplay.Apply(copy, ordered, policy);
        return copy;
    }

    private sealed record Posted(Guid? EntryId, decimal Inventory, decimal Expensed, string ExpensedAccount);

    /// <summary>
    /// قيد السند (sign = 1) أو إلغائه (sign = -1): المخزون بتغيّر قيمة أصنافه موزّعاً على حسابات مستودعاتها، والباقي من التكلفة
    /// على تكلفة المبيعات (أو فرق السعر)، وضريبة المدخلات المستردة، ودائن الجهة المستحقة. الجرد الدوري: التكلفة كلها على المشتريات.
    /// </summary>
    private async Task<Posted> PostAsync(PurchaseLandedCost v, (Dictionary<Guid, decimal> ValueChange, CostingMethod Method) effect, int sign,
        DateTime date, string source, string description, CancellationToken ct)
    {
        var accounts = await PostingAccounts.SettingsAsync(_db, ct);
        var amounts = new Dictionary<string, decimal>();
        void Add(string account, decimal amount) => amounts[account] = amounts.GetValueOrDefault(account) + amount; // موجب مدين، سالب دائن

        var cost = sign * v.BaseCostTotal;
        decimal inventory = 0, expensed = 0;
        var expensedAccount = effect.Method is CostingMethod.Standard or CostingMethod.LastPurchase ? DefaultAccounts.PurchasePriceVariance : accounts.Cogs();
        if (accounts.IsPeriodic(date))
        {
            // الدوري: لا يُمس حساب المخزون داخل الفترة، والتكلفة المحمَّلة مشتريات تظهر في بضاعة آخر المدة
            expensedAccount = accounts.Purchases();
            expensed = cost;
            Add(expensedAccount, cost);
        }
        else
        {
            foreach (var (account, amount) in await InventoryValuation.ByAccountAsync(_db, accounts, effect.ValueChange.Select(c => (c.Key, c.Value)), ct))
            {
                Add(account, amount);
                inventory += amount;
            }
            expensed = cost - inventory;
            if (expensed != 0) Add(expensedAccount, expensed);
        }
        // أنواع لا تدخل التكلفة: كل سطر على حساب مصروفه، في القيد نفسه (الجرد الدوري أيضاً: مصروف الفترة لا مشتريات)
        foreach (var line in v.Lines.Where(l => l.ExpenseAmount != 0))
            Add(line.ExpenseAccountCode ?? DefaultAccounts.PurchaseFreightExpense, sign * line.ExpenseAmount);
        if (v.BaseInputVat > 0) Add(accounts.InputVat(), sign * v.BaseInputVat);
        var payable = v.Lines.Sum(l => l.BaseAmount + l.BaseVatAmount);
        var payee = v.Payee switch
        {
            PurchaseCostPayee.Supplier => await _db.Set<Supplier>().AsNoTracking().Where(s => s.Id == v.SupplierId).Select(s => s.AccountCode).FirstOrDefaultAsync(ct),
            PurchaseCostPayee.PaymentAccount => v.PaymentAccountCode,
            _ => null,
        };
        Add(string.IsNullOrWhiteSpace(payee) ? DefaultAccounts.AccruedLandedCosts : payee, -sign * payable);

        var lines = amounts.Where(a => a.Value != 0)
            .Select(a => a.Value > 0 ? new PostingLine(a.Key, a.Value, 0) : new PostingLine(a.Key, 0, -a.Value)).ToList();
        if (lines.Count == 0) return new Posted(null, 0, 0, expensedAccount);
        await DefaultAccounts.EnsureAsync(_db, ct, lines.Select(l => l.AccountCode).ToArray());
        var posted = await _posting.PostAsync(new GenericPostingRequest
        {
            Date = date, Description = description, SourceType = source, SourceId = v.Id, SourceNumber = v.VoucherNumber, Lines = lines,
        }, ct);
        return new Posted(posted.JournalEntryId, sign * inventory, sign * expensed, expensedAccount);
    }

    // ---------- العرض ----------

    /// <summary>تكلفة وحدة سطر الفاتورة بالعملة الأساسية كما دخلت المخزون (قبل أي سند).</summary>
    private async Task<Dictionary<Guid, decimal>> UnitCostsAsync(IEnumerable<Guid> invoiceItemIds, CancellationToken ct)
    {
        var ids = invoiceItemIds.Distinct().ToList();
        if (ids.Count == 0) return new();
        return await _db.Set<PurchaseInvoiceItem>().AsNoTracking().Where(i => ids.Contains(i.Id))
            .Select(i => new { i.Id, Cost = i.Quantity == 0 ? 0 : i.TotalBeforeVat * i.Invoice.ExchangeRate / i.Quantity })
            .ToDictionaryAsync(i => i.Id, i => Math.Round(i.Cost, 4), ct);
    }

    private static PurchaseLandedCostDto ToDto(PurchaseLandedCost v, Dictionary<Guid, decimal> unitCosts)
    {
        var dto = Mapper.Map<PurchaseLandedCostDto>(v);
        dto.Invoices = v.Invoices.Select(i => new PurchaseLandedCostInvoiceDto { InvoiceId = i.InvoiceId, InvoiceNumber = i.InvoiceNumber }).ToList();
        dto.Lines = v.Lines.OrderBy(l => l.LineNo).Select(l =>
        {
            var line = Mapper.Map<PurchaseLandedCostLineDto>(l);
            line.Allocations = l.Allocations.Select(Mapper.Map<PurchaseLandedCostAllocationDto>).ToList();
            return line;
        }).ToList();
        dto.Items = v.Lines.SelectMany(l => l.Allocations).GroupBy(a => a.InvoiceItemId).Select(g =>
        {
            var first = g.First();
            var unit = unitCosts.GetValueOrDefault(first.InvoiceItemId);
            var capitalized = g.Sum(a => a.Amount);
            return new PurchaseLandedCostItemDto
            {
                InvoiceId = first.InvoiceId, InvoiceNumber = first.InvoiceNumber, InvoiceItemId = first.InvoiceItemId, ItemId = first.ItemId,
                ItemName = first.ItemName, NetQuantity = first.NetQuantity, UnitCost = unit, Capitalized = capitalized,
                UnitCostAfter = first.NetQuantity == 0 ? unit : Math.Round(unit + capitalized / first.NetQuantity, 4),
            };
        }).OrderBy(i => i.InvoiceNumber).ThenBy(i => i.ItemName).ToList();
        return dto;
    }
}
