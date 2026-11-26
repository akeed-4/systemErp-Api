using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Accounting;

namespace ERP.Service.Services.Shared;

/// <summary>
/// فواتير الصرف والاستلام المخزني (<see cref="InventoryVoucher"/>): مستندان مخزنيان مستقلان، لا بيع ولا شراء.
/// - لا ضريبة إطلاقاً (لا عمود لها)، ولا عميل ولا مورد ولا التزام، ولا إرسال للهيئة ولا رمز QR.
/// - الصرف: كل سطر حركة <see cref="StockMovementType.AdjustmentOut"/> بتكلفته الفعلية من سياسة التكلفة (المتوسط/FIFO/آخر شراء/المعيارية)
///   والتشغيلات (المختارة أو FEFO)؛ القيد مدين الحساب المقابل / دائن مخزون موقع السطر بتغيّر القيمة الدفترية.
/// - الاستلام: سعر الشراء والخصومات بمحرك التسعير المشترك (<see cref="DocumentPricing"/>) بنسبة ضريبة صفر، وكل سطر حركة
///   <see cref="StockMovementType.InPurchase"/> فتسري قواعد الشراء في التكلفة (إعادة الاحتساب أو إيقافها، آخر سعر شراء، المعيارية)؛
///   القيد مدين مخزون الموقع بتغيّر قيمته / دائن الحساب المقابل بصافي المستند، والفرق على حساب الانحرافات كفاتورة الشراء.
/// - الجرد الدوري: لا يُمس حساب المخزون؛ الصرف مدين المقابل / دائن المشتريات بقيمة الخروج، والاستلام مدين المشتريات / دائن المقابل.
/// - الترحيل مرة واحدة: الحالة تُفحص في المعاملة ونسخة الصف (RowVersion) تمنع ترحيلين متزامنين؛ الإلغاء يعكس الحركات بتكلفتها
///   المحفوظة وتشغيلاتها ويعكس القيد، وفرق إعادة البناء قيد إعادة تقييم.
/// </summary>
public class InventoryVoucherService : IInventoryVoucherService
{
    private readonly ErpDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly IAccountingPostingService _posting;
    private readonly INumberSequenceService _numbers;
    private readonly ITransactionRunner _tx;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;

    /// <summary>الحسابات تُخفى عن المستخدم التشغيلي: تظهر لمن يملك عرض دليل الحسابات والقيود (المحاسب المخوّل).</summary>
    public const string AccountsScreen = "accounts";

    public InventoryVoucherService(ErpDbContext db, IInventoryService inventory, IAccountingPostingService posting,
        INumberSequenceService numbers, ITransactionRunner tx, IAuditService audit, ICurrentUser user, IPermissionService permissions)
    {
        _db = db; _inventory = inventory; _posting = posting; _numbers = numbers; _tx = tx; _audit = audit; _user = user; _permissions = permissions;
    }

    private Task<bool> CanViewAccountsAsync(CancellationToken ct) => _permissions.HasPermissionAsync(AccountsScreen, ScreenAction.View, ct);

    // ---------------- القراءة ----------------

    public async Task<PagedResult<InventoryVoucherDto>> ListAsync(string kind, PaginationParams p, CancellationToken ct = default)
    {
        kind = Kind(kind);
        var q = _db.Set<InventoryVoucher>().AsNoTracking().Where(v => v.Kind == kind);
        if (!string.IsNullOrWhiteSpace(p.Status)) q = q.Where(v => v.Status == p.Status);
        if (p.StartDate.HasValue) q = q.Where(v => v.Date >= p.StartDate.Value.Date);
        if (p.EndDate.HasValue) q = q.Where(v => v.Date <= p.EndDate.Value.Date);
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(v => v.VoucherNumber.Contains(t) || (v.Reference != null && v.Reference.Contains(t)) || (v.Notes != null && v.Notes.Contains(t))
                || v.Lines.Any(l => l.ItemName.Contains(t) || l.Sku.Contains(t)));
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(v => v.Date).ThenByDescending(v => v.CreatedAt)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize)
            .Select(v => new { Voucher = v, Count = v.Lines.Count, Quantity = v.Lines.Sum(l => (decimal?)l.Quantity) ?? 0 })
            .ToListAsync(ct);
        var names = await NamesAsync(rows.Select(r => r.Voucher).ToList(), ct);
        var showAccounts = await CanViewAccountsAsync(ct);
        return new PagedResult<InventoryVoucherDto>
        {
            Items = rows.Select(r =>
            {
                var dto = ToDto(r.Voucher, includeLines: false, names, showAccounts);
                dto.LineCount = r.Count; dto.TotalQuantity = r.Quantity;
                return dto;
            }).ToList(),
            TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    public async Task<InventoryVoucherDto> GetAsync(string kind, Guid id, CancellationToken ct = default)
    {
        kind = Kind(kind);
        var voucher = await _db.Set<InventoryVoucher>().AsNoTracking().Include(v => v.Lines)
            .FirstOrDefaultAsync(v => v.Id == id && v.Kind == kind, ct) ?? throw new NotFoundException(Messages.InventoryVoucherNotFound);
        var dto = ToDto(voucher, includeLines: true, await NamesAsync(new List<InventoryVoucher> { voucher }, ct), await CanViewAccountsAsync(ct));
        if (voucher.Status == InventoryVoucherStatuses.Draft) await FillAvailableAsync(voucher, dto.Lines, ct);
        if (kind == InventoryVoucherKinds.Receipt) dto.HasLandedCosts = await HasApprovedLandedCostsAsync(voucher.Id, ct);
        return dto;
    }

    public async Task<InventoryVoucherDefaultsDto> DefaultsAsync(string kind, CancellationToken ct = default)
    {
        kind = Kind(kind);
        var settings = await PostingAccounts.SettingsAsync(_db, ct);
        var showAccounts = await CanViewAccountsAsync(ct);
        var site = await WarehouseStocks.ResolveAsync(_db, null, ct);
        var dto = new InventoryVoucherDefaultsDto
        {
            DefaultReason = InventoryVoucherReasons.DefaultOf(kind), IsPeriodic = settings.IsPeriodic(SaudiTime.Today), CanViewAccounts = showAccounts,
        };
        foreach (var reason in InventoryVoucherReasons.Of(kind))
        {
            var item = new InventoryVoucherReasonDto { Reason = reason, PerSite = reason is InventoryVoucherReasons.Shortage or InventoryVoucherReasons.Surplus };
            if (showAccounts) item.AccountCode = await CounterAccountAsync(settings, reason, site.Id, ct);
            dto.Reasons.Add(item);
        }
        if (showAccounts)
        {
            var codes = dto.Reasons.Select(r => r.AccountCode).OfType<string>().Distinct().ToList();
            var names = await _db.Set<Account>().AsNoTracking().Where(a => codes.Contains(a.Code)).ToDictionaryAsync(a => a.Code, a => a.NameAr, ct);
            foreach (var r in dto.Reasons) r.AccountName = r.AccountCode == null ? null : names.GetValueOrDefault(r.AccountCode);
        }
        return dto;
    }

    // ---------------- الكتابة ----------------

    public Task<InventoryVoucherDto> CreateAsync(string kind, SaveInventoryVoucherDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            kind = Kind(kind);
            var voucher = new InventoryVoucher { Id = Guid.NewGuid(), Kind = kind, Status = InventoryVoucherStatuses.Draft };
            await ApplyAsync(voucher, r, tracked: false, token);
            voucher.VoucherNumber = await _numbers.AssignAsync(new DocumentNumberRequest
            {
                Key = InventoryVoucherKinds.SourceOf(kind), DefaultPrefix = kind == InventoryVoucherKinds.Receipt ? "IRC-" : "IIS-",
                WarehouseId = voucher.WarehouseId, Date = voucher.Date, ManualNumber = r.ManualNumber, UserEntered = true,
                Exists = (n, c) => _db.Set<InventoryVoucher>().AnyAsync(v => v.VoucherNumber == n, c),
            }, token);
            _db.Add(voucher);
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync(AuditAction(kind, "CREATED"), nameof(InventoryVoucher), voucher.Id.ToString(),
                $"مسودة {Label(kind)} {voucher.VoucherNumber}: {voucher.Lines.Count} سطر", token);
            if (r.Post) await PostCoreAsync(voucher, token);
            return await GetAsync(kind, voucher.Id, token);
        }, ct);

    public Task<InventoryVoucherDto> UpdateAsync(string kind, Guid id, SaveInventoryVoucherDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var voucher = await LoadAsync(kind, id, token);
            if (voucher.Status != InventoryVoucherStatuses.Draft) throw new ConflictException(Messages.InventoryVoucherNotDraft);
            _db.RemoveRange(voucher.Lines.ToList());
            voucher.Lines.Clear();
            await ApplyAsync(voucher, r, tracked: true, token);
            await _db.SaveChangesAsync(token);
            if (r.Post) await PostCoreAsync(voucher, token);
            return await GetAsync(kind, voucher.Id, token);
        }, ct);

    public Task DeleteAsync(string kind, Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var voucher = await LoadAsync(kind, id, token);
            if (voucher.Status != InventoryVoucherStatuses.Draft) throw new ConflictException(Messages.InventoryVoucherNotDraft);
            _db.RemoveRange(voucher.Lines.ToList());
            _db.Remove(voucher);
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync(AuditAction(voucher.Kind, "DELETED"), nameof(InventoryVoucher), voucher.Id.ToString(),
                $"حذف مسودة {Label(voucher.Kind)} {voucher.VoucherNumber}", token);
        }, ct);

    public Task<InventoryVoucherDto> PostAsync(string kind, Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var voucher = await LoadAsync(kind, id, token);
            if (voucher.Status != InventoryVoucherStatuses.Draft) throw new ConflictException(Messages.InventoryVoucherNotDraft);
            await PostCoreAsync(voucher, token);
            return await GetAsync(voucher.Kind, voucher.Id, token);
        }, ct);

    public Task<InventoryVoucherDto> CancelAsync(string kind, Guid id, string reason, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new ValidationFailedException(Messages.InventoryVoucherCancelReasonRequired);
            var voucher = await LoadAsync(kind, id, token);
            if (voucher.Status != InventoryVoucherStatuses.Posted) throw new ConflictException(Messages.InventoryVoucherNotPosted);
            var receipt = voucher.Kind == InventoryVoucherKinds.Receipt;
            // تكاليف إضافية معتمدة رفعت تكلفة حركات الاستلام: عكسه يُسقطها، فيُلغى السند أولاً (كفاتورة الشراء)
            if (receipt && await HasApprovedLandedCostsAsync(voucher.Id, token)) throw new ConflictException(Messages.InventoryVoucherHasLandedCosts);

            var source = InventoryVoucherKinds.SourceOf(voucher.Kind);
            var note = $"إلغاء {Label(voucher.Kind)} {voucher.VoucherNumber}: {reason.Trim()}";
            if (voucher.JournalEntryId is { } entryId) await _posting.ReverseAsync(entryId, note, token);

            var settings = await PostingAccounts.SettingsAsync(_db, token);
            // كل سطر يُعكس بتكلفته المحفوظة وتشغيلاته؛ ما يختلف عن أثره الأصلي (حركات تلته) إعادة تقييم على حساب موقعه
            var residuals = new Dictionary<string, decimal>();
            foreach (var line in voucher.Lines.Where(l => l.StockMovementId.HasValue).OrderBy(l => l.LineNo))
            {
                var baseQty = BaseQuantity(line);
                var baseCost = baseQty == 0 ? 0 : Math.Round(line.TotalCost / baseQty, 4);
                var site = line.WarehouseId ?? voucher.WarehouseId;
                var back = await _inventory.RecordMovementAsync(new RecordStockMovementDto
                {
                    ItemId = line.ItemId, WarehouseId = site, Quantity = baseQty,
                    Type = receipt ? StockMovementType.AdjustmentOut : StockMovementType.AdjustmentIn,
                    UnitCost = receipt ? 0 : baseCost, OutUnitCost = receipt ? baseCost : null,
                    ReferenceNumber = voucher.VoucherNumber, Date = voucher.Date, Notes = note,
                    SourceType = source + "_reversal", SourceId = voucher.Id, SourceLineId = line.Id,
                    BatchAllocations = await ItemBatches.OfDocumentAsync(_db, source, voucher.Id, line.ItemId, line.Id, token),
                }, token);
                var residual = receipt ? back.ValueChange + line.TotalCost : back.ValueChange - line.TotalCost;
                if (residual == 0) continue;
                var account = await settings.InventoryAsync(_db, site, token);
                residuals[account] = residuals.GetValueOrDefault(account) + residual;
            }
            await ItemBatches.MarkReversedAsync(_db, source, voucher.Id, token);
            await _db.SaveChangesAsync(token);
            if (voucher.JournalEntryId != null && residuals.Count > 0)
                await InventoryValuation.PostRevaluationAsync(_db, _posting, residuals, note, voucher.Id, token);

            voucher.Status = InventoryVoucherStatuses.Cancelled;
            voucher.CancelReason = reason.Trim();
            voucher.CancelledAt = SaudiTime.Now;
            voucher.CancelledBy = _user.Email ?? _user.Name;
            voucher.JournalEntryId = null;
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync(AuditAction(voucher.Kind, "CANCELLED"), nameof(InventoryVoucher), voucher.Id.ToString(), note, token);
            return await GetAsync(voucher.Kind, voucher.Id, token);
        }, ct);

    // ---------------- البناء ----------------

    /// <param name="tracked">المستند محفوظ ومتتبَّع (التعديل): أسطره الجديدة تُضاف للسياق صراحة. الجديد يُضاف بأسطره بعد ترقيمه
    /// (الترقيم وإنشاء الحساب الافتراضي يحفظان السياق، فلا يُحفظ سطر قبل مستنده).</param>
    private async Task ApplyAsync(InventoryVoucher v, SaveInventoryVoucherDto r, bool tracked, CancellationToken ct)
    {
        var receipt = v.Kind == InventoryVoucherKinds.Receipt;
        var site = await WarehouseStocks.ResolveAsync(_db, r.WarehouseId, ct);
        if (site.Status != "active") throw new ValidationFailedException(Messages.WarehouseInactive);
        _db.EnsureWarehouseAllowed(site.Id);
        v.Date = (r.Date ?? SaudiTime.Today).Date;
        v.WarehouseId = site.Id;
        v.CostCenterId = r.CostCenterId;
        v.Reference = string.IsNullOrWhiteSpace(r.Reference) ? null : r.Reference.Trim();
        v.Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();

        var reason = string.IsNullOrWhiteSpace(r.Reason) ? InventoryVoucherReasons.DefaultOf(v.Kind) : r.Reason.Trim();
        if (!InventoryVoucherReasons.IsValid(v.Kind, reason)) throw new ValidationFailedException(Messages.InventoryVoucherReasonInvalid);
        v.Reason = reason;
        v.CounterAccountCode = string.Empty; // يُحدَّد من الإعدادات عند الترحيل ويُثبَّت

        var inputs = r.Lines.Where(l => l.ItemId != Guid.Empty).ToList();
        if (inputs.Count == 0) throw new ValidationFailedException(Messages.InventoryVoucherLinesRequired);
        var ids = inputs.Select(l => l.ItemId).Distinct().ToList();
        var products = await _db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        foreach (var l in inputs)
        {
            if (!products.TryGetValue(l.ItemId, out var p)) throw new ValidationFailedException(Messages.ProductNotFound);
            if (!ProductItemTypes.TracksStock(ProductItemTypes.Normalize(p.ItemType)))
                throw new ValidationFailedException(string.Format(Messages.InventoryVoucherItemNotStock, p.NameAr));
            if (l.Quantity <= 0) throw new ValidationFailedException(string.Format(Messages.InventoryVoucherQuantityInvalid, p.NameAr));
        }
        await EnsureLineSitesAsync(inputs.Select(l => l.WarehouseId).Where(w => w.HasValue && w != site.Id).Select(w => w!.Value), ct);
        await CostCenterRules.EnsureUsableAsync(_db, inputs.Select(l => l.CostCenterId).Append(r.CostCenterId), ct, requireActive: true);
        var units = await ProductLineUnits.ResolveAsync(_db, inputs.Select(l => (l.ItemId, l.Unit)).ToList(), ct);

        // الاستلام بمحرك التسعير المشترك بضريبة صفر: خصم السطر، وخصم المستند موزعاً بنسبة الصافي والكسر على آخر سطر (كالمشتريات)
        var priced = receipt
            ? DocumentPricing.Price(inputs.Select(l => new PricedLineInput(l.Quantity, l.UnitPrice, l.Discount, 0m)).ToList(), r.DocumentDiscount)
            : null;

        for (var i = 0; i < inputs.Count; i++)
        {
            var l = inputs[i];
            var p = products[l.ItemId];
            var (unit, factor) = units[i];
            var line = new InventoryVoucherLine
            {
                Id = Guid.NewGuid(), VoucherId = v.Id, LineNo = i + 1, ItemId = p.Id, Sku = p.Sku, ItemName = p.NameAr,
                Unit = unit ?? (string.IsNullOrWhiteSpace(l.Unit) ? p.Unit : l.Unit.Trim()), UnitFactor = factor <= 0 ? 1 : factor,
                Quantity = l.Quantity, WarehouseId = l.WarehouseId ?? site.Id, CostCenterId = l.CostCenterId ?? r.CostCenterId,
                Notes = string.IsNullOrWhiteSpace(l.Notes) ? null : l.Notes.Trim(),
            };
            if (priced != null)
            {
                var pl = priced.Lines[i];
                line.UnitPrice = l.UnitPrice; line.Discount = pl.Discount; line.AllocatedDiscount = pl.AllocatedInvoiceDiscount; line.LineTotal = pl.Net;
            }
            if (p.TracksLots)
            {
                if (receipt)
                {
                    line.BatchNumber = string.IsNullOrWhiteSpace(l.BatchNumber) ? null : l.BatchNumber.Trim();
                    line.ProductionDate = l.ProductionDate?.Date;
                    line.ExpiryDate = l.ExpiryDate?.Date;
                }
                else line.ItemBatchId = l.ItemBatchId;
            }
            v.Lines.Add(line);
            if (tracked) _db.Add(line);
        }
        v.Subtotal = priced?.Subtotal ?? 0;
        v.DocumentDiscount = priced?.InvoiceDiscount ?? 0;
        v.DiscountTotal = priced?.DiscountTotal ?? 0;
        v.NetTotal = priced?.NetTotal ?? 0;
        v.InventoryValue = 0;
    }

    // ---------------- الترحيل ----------------

    private async Task PostCoreAsync(InventoryVoucher v, CancellationToken ct)
    {
        var receipt = v.Kind == InventoryVoucherKinds.Receipt;
        var source = InventoryVoucherKinds.SourceOf(v.Kind);
        var note = $"{Label(v.Kind)} {v.VoucherNumber}";
        // القيمة لكل (موقع، مركز تكلفة): المخزون بحساب موقعه، والمقابل بمركز السطر
        var values = new List<(Guid? Site, Guid? CostCenter, decimal Value, decimal Net, string Counter)>();
        var settings = await PostingAccounts.SettingsAsync(_db, ct);
        var counters = new Dictionary<Guid, string>();
        async Task<string> CounterOf(Guid? site)
        {
            var key = site ?? Guid.Empty;
            if (counters.TryGetValue(key, out var known)) return known;
            var code = await CounterAccountAsync(settings, v.Reason, site, ct);
            await DefaultAccounts.EnsureAsync(_db, ct, code); // الافتراضي (532/424/517/33) يُنشأ للمنشأة القديمة عند أول استخدام
            await EnsureCounterAccountAsync(settings, code, ct);
            return counters[key] = code;
        }
        foreach (var line in v.Lines.OrderBy(l => l.LineNo))
        {
            var site = line.WarehouseId ?? v.WarehouseId;
            var baseQty = BaseQuantity(line);
            var movement = new RecordStockMovementDto
            {
                ItemId = line.ItemId, WarehouseId = site, Quantity = baseQty, ReferenceNumber = v.VoucherNumber, Date = v.Date,
                Notes = line.Notes == null ? note : $"{note} - {line.Notes}", SourceType = source, SourceId = v.Id, SourceLineId = line.Id,
            };
            if (receipt)
            {
                // تكلفة الوحدة الأساسية = صافي السطر ÷ كميته بالأساسية، وسعرها = سعر وحدة السطر ÷ المعامل (كفاتورة الشراء)
                movement.Type = StockMovementType.InPurchase;
                movement.UnitCost = baseQty == 0 ? 0 : Math.Round(line.LineTotal / baseQty, 4);
                movement.UnitPrice = Math.Round(line.UnitPrice / line.UnitFactor, 4);
                movement.BatchNumber = line.BatchNumber; movement.ProductionDate = line.ProductionDate; movement.ExpiryDate = line.ExpiryDate;
            }
            else
            {
                movement.Type = StockMovementType.AdjustmentOut;
                movement.ItemBatchId = line.ItemBatchId;
                // الصرف كالبيع لا يستهلك المحجوز للمشاريع (والرصيد غير الكافي ترفضه سياسة المخزون السالب في محرك المخزون)
                if (site is { } siteId) await StockReservations.EnsureUnreservedAsync(_db, line.ItemId, line.ItemName, siteId, baseQty, ct);
            }
            var recorded = await _inventory.RecordMovementAsync(movement, ct);
            line.StockMovementId = recorded.Id;
            // تغيّر القيمة الدفترية الفعلي (لا الكمية × التكلفة): حساب المخزون يبقى مساوياً لقيمة الأصناف
            line.TotalCost = receipt ? recorded.ValueChange : -recorded.ValueChange;
            line.UnitCost = Math.Round((receipt ? movement.UnitCost : recorded.UnitCost) * line.UnitFactor, 4);
            if (!receipt) line.LineTotal = line.TotalCost;
            if (recorded.Batches is { Count: > 0 } parts)
            {
                line.BatchNumber = string.Join("، ", parts.Select(b => b.BatchNumber).Distinct());
                line.ExpiryDate = parts.Select(b => b.ExpiryDate).FirstOrDefault(d => d.HasValue);
            }
            line.CounterAccountCode = await CounterOf(site);
            values.Add((site, line.CostCenterId, line.TotalCost, receipt ? line.LineTotal : line.TotalCost, line.CounterAccountCode));
        }
        // الحساب المستخدم فعلاً (لموقع المستند، أو حساب السطر الأول إن لم يكن للمستند سطر في موقعه)
        v.CounterAccountCode = v.Lines.Where(l => (l.WarehouseId ?? v.WarehouseId) == v.WarehouseId).Select(l => l.CounterAccountCode).FirstOrDefault()
            ?? values.Select(x => x.Counter).FirstOrDefault() ?? string.Empty;
        v.InventoryValue = values.Sum(x => x.Value);
        if (!receipt) v.NetTotal = v.InventoryValue;

        v.JournalEntryId = (await PostEntryAsync(v, values, note, ct))?.JournalEntryId;
        v.Status = InventoryVoucherStatuses.Posted;
        v.PostedAt = SaudiTime.Now;
        v.PostedBy = _user.Email ?? _user.Name;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditAction(v.Kind, "POSTED"), nameof(InventoryVoucher), v.Id.ToString(),
            $"ترحيل {note} ({v.Reason}): قيمة المخزون {v.InventoryValue:0.00} على {string.Join("، ", values.Select(x => x.Counter).Distinct())}", ct);
    }

    /// <summary>
    /// قيد المستند. المستمر: الصرف مدين المقابل / دائن مخزون كل موقع؛ الاستلام مدين مخزون كل موقع / دائن المقابل بالصافي،
    /// والفرق (المعيارية أو إيقاف تحديث التكلفة) على حساب الانحرافات. الدوري: المشتريات بدل المخزون (الأثر في بضاعة آخر المدة).
    /// </summary>
    private async Task<PostingResult?> PostEntryAsync(InventoryVoucher v, List<(Guid? Site, Guid? CostCenter, decimal Value, decimal Net, string Counter)> values,
        string note, CancellationToken ct)
    {
        var receipt = v.Kind == InventoryVoucherKinds.Receipt;
        var settings = await PostingAccounts.SettingsAsync(_db, ct);
        var amounts = new Dictionary<(string Account, Guid? CostCenter), decimal>(); // موجب مدين، سالب دائن
        void Add(string account, Guid? center, decimal amount)
        {
            if (amount == 0) return;
            amounts[(account, center)] = amounts.GetValueOrDefault((account, center)) + amount;
        }

        if (settings.IsPeriodic(v.Date))
        {
            var purchases = settings.Purchases();
            foreach (var x in values)
            {
                var amount = receipt ? x.Net : x.Value;
                Add(receipt ? purchases : x.Counter, x.CostCenter, amount);
                Add(receipt ? x.Counter : purchases, x.CostCenter, -amount);
            }
        }
        else
        {
            foreach (var x in values)
            {
                var inventory = await settings.InventoryAsync(_db, x.Site, ct);
                if (receipt)
                {
                    Add(inventory, x.CostCenter, x.Value);
                    Add(x.Counter, x.CostCenter, -x.Net);
                }
                else
                {
                    Add(x.Counter, x.CostCenter, x.Value);
                    Add(inventory, x.CostCenter, -x.Value);
                }
            }
            var variance = receipt ? values.Sum(x => x.Net) - values.Sum(x => x.Value) : 0;
            if (variance != 0)
            {
                var policy = await _db.Set<CostingPolicy>().AsNoTracking().FirstOrDefaultAsync(ct);
                // حساب الانحرافات كفاتورة الشراء: المحدد في السياسة للمعيارية ولإيقاف تحديث التكلفة، وإلا فروق أسعار المشتريات
                var account = policy is { Method: CostingMethod.Standard } or { RecalculateOnNewPurchase: false }
                        && !string.IsNullOrWhiteSpace(policy.StandardCostVarianceAccountCode)
                    ? policy.StandardCostVarianceAccountCode.Trim() : DefaultAccounts.PurchasePriceVariance;
                Add(account, v.CostCenterId, variance);
            }
        }

        var lines = amounts.Where(a => a.Value != 0)
            .Select(a => a.Value > 0 ? new PostingLine(a.Key.Account, a.Value, 0, note, a.Key.CostCenter) : new PostingLine(a.Key.Account, 0, -a.Value, note, a.Key.CostCenter))
            .ToList();
        if (lines.Count == 0) return null;
        await DefaultAccounts.EnsureAsync(_db, ct, lines.Select(l => l.AccountCode).Distinct().ToArray());
        return await _posting.PostAsync(new GenericPostingRequest
        {
            Date = v.Date, Description = note, SourceType = InventoryVoucherKinds.SourceOf(v.Kind), SourceId = v.Id, SourceNumber = v.VoucherNumber,
            Lines = lines,
        }, ct);
    }

    // ---------------- التحقق ----------------

    /// <summary>
    /// الحساب المقابل: فرعي فعّال، لا حساب عميل أو مورد (لا ذمة ولا التزام)، ولا تحت المدينين أو الدائنين أو المخزون، ولا حساب ميزانية
    /// مقفل بدوره (الضرائب، المخزون، الدفعات والمحتجزات...).
    /// </summary>
    private async Task EnsureCounterAccountAsync(AccountingSettings settings, string code, CancellationToken ct)
    {
        var chart = await _db.Set<Account>().AsNoTracking()
            .Select(a => new { a.Code, a.ParentCode, a.IsGroup, a.IsActive, a.LinkedEntityType }).ToDictionaryAsync(a => a.Code, ct);
        var invalid = new ValidationFailedException(string.Format(Messages.InventoryVoucherCounterAccountInvalid, code));
        if (!chart.TryGetValue(code, out var account) || account.IsGroup || !account.IsActive) throw invalid;
        if (account.LinkedEntityType is LinkedEntityType.Customer or LinkedEntityType.Supplier) throw invalid;
        // سلسلة الآباء الفعلية (لا بداية الرمز): ما تحت المدينين أو الدائنين أو المخزون مرفوض
        var blockedRoots = new HashSet<string> { DefaultAccounts.Receivables, DefaultAccounts.Payables, DefaultAccounts.InventoryGroup };
        var seen = new HashSet<string>();
        for (var c = code; c != null && seen.Add(c); c = chart.TryGetValue(c, out var node) ? node.ParentCode : null)
            if (blockedRoots.Contains(c)) throw invalid;
        var locked = PostingAccounts.Roles.Where(r => r.LockedAfterUse && r.Key != nameof(AccountingSettings.ProjectMaterialsWipAccountCode))
            .Select(r => r.Effective(settings)).OfType<string>().ToHashSet();
        if (locked.Contains(code) || (await PostingAccounts.WarehouseInventoryAccountsAsync(_db, ct)).Contains(code)) throw invalid;
    }

    private async Task EnsureLineSitesAsync(IEnumerable<Guid> others, CancellationToken ct)
    {
        var ids = others.Distinct().ToList();
        if (ids.Count == 0) return;
        var found = await _db.Set<Warehouse>().AsNoTracking().Where(w => ids.Contains(w.Id)).Select(w => new { w.Id, w.Status }).ToDictionaryAsync(w => w.Id, ct);
        foreach (var id in ids)
        {
            if (!found.TryGetValue(id, out var site)) throw new ValidationFailedException(Messages.WarehouseNotFound);
            if (site.Status != "active") throw new ValidationFailedException(Messages.WarehouseInactive);
            _db.EnsureWarehouseAllowed(id);
        }
    }

    // ---------------- مساعدات ----------------

    private static string Kind(string kind)
        => InventoryVoucherKinds.IsValid(kind) ? kind : throw new NotFoundException(Messages.InventoryVoucherNotFound);

    /// <summary>
    /// الحساب المقابل لسبب الحركة من إعدادات الحسابات (قسم «ربط الحسابات بحركات المخزون»)، وإلا الافتراضي في الشجرة.
    /// تسوية النقص والزيادة على حساب فروقات المخزون لموقع السطر (كالجرد).
    /// </summary>
    private async Task<string> CounterAccountAsync(AccountingSettings s, string reason, Guid? site, CancellationToken ct) => reason switch
    {
        InventoryVoucherReasons.Project => s.InventoryProjectIssueAccountCode ?? DefaultAccounts.ProjectMaterialsCost,
        InventoryVoucherReasons.Shortage => (await s.VarianceAsync(_db, site, ct)).Shortage,
        InventoryVoucherReasons.OpeningBalance => s.InventoryOpeningReceiptAccountCode ?? DefaultAccounts.OpeningBalanceEquity,
        InventoryVoucherReasons.Surplus => (await s.VarianceAsync(_db, site, ct)).Surplus,
        InventoryVoucherReasons.OtherReceipt => s.InventoryReceiptAccountCode ?? DefaultAccounts.InventoryReceiptsWithoutInvoice,
        _ => s.InventoryIssueAccountCode ?? DefaultAccounts.InventoryIssueExpense,
    };

    private static decimal BaseQuantity(InventoryVoucherLine l) => l.Quantity * (l.UnitFactor > 0 ? l.UnitFactor : 1);

    private static string Label(string kind) => kind == InventoryVoucherKinds.Receipt ? "فاتورة استلام مخزني" : "فاتورة صرف مخزني";

    private static string AuditAction(string kind, string action) => $"INVENTORY_{(kind == InventoryVoucherKinds.Receipt ? "RECEIPT" : "ISSUE")}_{action}";

    private Task<bool> HasApprovedLandedCostsAsync(Guid voucherId, CancellationToken ct)
        => _db.Set<PurchaseLandedCostInvoice>().AnyAsync(l => l.InvoiceId == voucherId && l.SourceType == PurchaseLandedCostSources.InventoryReceipt
            && l.Voucher.Status == PurchaseLandedCostStatus.Approved, ct);

    private async Task<InventoryVoucher> LoadAsync(string kind, Guid id, CancellationToken ct)
    {
        kind = Kind(kind);
        return await _db.Set<InventoryVoucher>().Include(v => v.Lines).FirstOrDefaultAsync(v => v.Id == id && v.Kind == kind, ct)
               ?? throw new NotFoundException(Messages.InventoryVoucherNotFound);
    }

    /// <summary>رصيد كل سطر في موقعه وتكلفته الحالية لوحدته (للمسودة: التحقق والتقدير قبل الترحيل).</summary>
    private async Task FillAvailableAsync(InventoryVoucher v, List<InventoryVoucherLineDto> lines, CancellationToken ct)
    {
        var ids = lines.Select(l => l.ItemId).Distinct().ToList();
        var sites = lines.Select(l => l.WarehouseId ?? v.WarehouseId).OfType<Guid>().Distinct().ToList();
        var stock = await _db.Set<WarehouseStock>().AsNoTracking().Where(s => ids.Contains(s.ItemId) && sites.Contains(s.WarehouseId))
            .ToListAsync(ct);
        var costs = await _db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.AverageCost, ct);
        foreach (var l in lines)
        {
            var site = l.WarehouseId ?? v.WarehouseId;
            l.Available = stock.Where(s => s.ItemId == l.ItemId && s.WarehouseId == site).Sum(s => s.Quantity);
            l.CurrentUnitCost = Math.Round(costs.GetValueOrDefault(l.ItemId) * l.UnitFactor, 4);
        }
    }

    private async Task<(Dictionary<Guid, string> Sites, Dictionary<string, string> Accounts)> NamesAsync(List<InventoryVoucher> vouchers, CancellationToken ct)
    {
        var siteIds = vouchers.Select(v => v.WarehouseId).OfType<Guid>().Distinct().ToList();
        var codes = vouchers.Select(v => v.CounterAccountCode).Distinct().ToList();
        var sites = await _db.Set<Warehouse>().AsNoTracking().Where(w => siteIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.NameAr, ct);
        var accounts = await _db.Set<Account>().AsNoTracking().Where(a => codes.Contains(a.Code)).ToDictionaryAsync(a => a.Code, a => a.NameAr, ct);
        return (sites, accounts);
    }

    private static InventoryVoucherDto ToDto(InventoryVoucher v, bool includeLines, (Dictionary<Guid, string> Sites, Dictionary<string, string> Accounts) names,
        bool showAccounts)
    {
        var dto = new InventoryVoucherDto
        {
            Id = v.Id, CreatedAt = v.CreatedAt, Kind = v.Kind, VoucherNumber = v.VoucherNumber, Date = v.Date, WarehouseId = v.WarehouseId,
            WarehouseName = v.WarehouseId is { } w ? names.Sites.GetValueOrDefault(w) : null, Status = v.Status,
            Reason = v.Reason,
            CounterAccountCode = showAccounts && v.CounterAccountCode.Length > 0 ? v.CounterAccountCode : null,
            CounterAccountName = showAccounts ? names.Accounts.GetValueOrDefault(v.CounterAccountCode) : null,
            CostCenterId = v.CostCenterId, Reference = v.Reference, Notes = v.Notes, Subtotal = v.Subtotal, DocumentDiscount = v.DocumentDiscount,
            DiscountTotal = v.DiscountTotal, NetTotal = v.NetTotal, InventoryValue = v.InventoryValue, JournalEntryId = v.JournalEntryId,
            PostedAt = v.PostedAt, PostedBy = v.PostedBy, CancelReason = v.CancelReason, CancelledAt = v.CancelledAt,
            LineCount = v.Lines.Count, TotalQuantity = v.Lines.Sum(l => l.Quantity),
        };
        if (includeLines)
            dto.Lines = v.Lines.OrderBy(l => l.LineNo).Select(l => new InventoryVoucherLineDto
            {
                Id = l.Id, LineNo = l.LineNo, ItemId = l.ItemId, Sku = l.Sku, ItemName = l.ItemName, Unit = l.Unit, UnitFactor = l.UnitFactor,
                Quantity = l.Quantity, WarehouseId = l.WarehouseId, CostCenterId = l.CostCenterId, CounterAccountCode = showAccounts ? l.CounterAccountCode : null,
                UnitPrice = l.UnitPrice, Discount = l.Discount,
                AllocatedDiscount = l.AllocatedDiscount, LineTotal = l.LineTotal, UnitCost = l.UnitCost, TotalCost = l.TotalCost,
                ItemBatchId = l.ItemBatchId, BatchNumber = l.BatchNumber, ProductionDate = l.ProductionDate, ExpiryDate = l.ExpiryDate, Notes = l.Notes,
            }).ToList();
        return dto;
    }
}
