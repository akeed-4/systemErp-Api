using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.CarShowroom;
using ERP.Service.Data;
using ERP.Service.Services.Accounting;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

/// <summary>
/// تكاليف الاستيراد المقدمة لأمر شراء: تُدفع قبل وصول السيارات وتُقيَّد على «تكاليف استيراد تحت التسوية»، ثم تُسوّى بسحبها في سند
/// التكاليف الإضافية فتُوزَّع على السيارات. الاعتماد يرحّل قيد الدفع، والإلغاء يعكسه ولا يجوز بعد أي سحب.
/// </summary>
public class ImportCostAdvanceService : IImportCostAdvanceService
{
    public const string PaymentSource = "car_import_cost_advance";

    private readonly ErpDbContext _db;
    private readonly ITransactionRunner _tx;
    private readonly INumberSequenceService _numbers;
    private readonly IAccountingPostingService _posting;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;

    public ImportCostAdvanceService(ErpDbContext db, ITransactionRunner tx, INumberSequenceService numbers, IAccountingPostingService posting, ICurrentUser user,
        IAuditService audit)
    {
        _db = db; _tx = tx; _numbers = numbers; _posting = posting; _user = user; _audit = audit;
    }

    public async Task<PagedResult<ImportCostAdvanceDto>> ListAsync(PaginationParams p, Guid? procurementOrderId, CancellationToken ct = default)
    {
        var q = _db.Set<ImportCostAdvance>().AsNoTracking().AsQueryable();
        if (procurementOrderId.HasValue) q = q.Where(a => a.ProcurementOrderId == procurementOrderId);
        if (Enum.TryParse<ImportCostAdvanceStatus>(p.Status, true, out var status)) q = q.Where(a => a.Status == status);
        if (p.StartDate.HasValue) q = q.Where(a => a.Date >= p.StartDate);
        if (p.EndDate.HasValue) q = q.Where(a => a.Date <= p.EndDate);
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(a => a.Number.Contains(t) || a.ProcurementOrderNumber.Contains(t) || (a.SupplierName != null && a.SupplierName.Contains(t))
                || (a.SupplierInvoiceNumber != null && a.SupplierInvoiceNumber.Contains(t)));
        }
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(a => a.Date).ThenByDescending(a => a.Number)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);
        return new PagedResult<ImportCostAdvanceDto> { Items = items.Select(ToDto).ToList(), TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize };
    }

    public async Task<ImportCostAdvanceDto> GetAsync(Guid id, CancellationToken ct = default)
        => ToDto(await _db.Set<ImportCostAdvance>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException(Messages.ImportCostAdvanceNotFound));

    public async Task<List<ImportCostAdvanceDto>> OpenForOrderAsync(Guid procurementOrderId, CancellationToken ct = default)
        => (await _db.Set<ImportCostAdvance>().AsNoTracking()
            .Where(a => a.ProcurementOrderId == procurementOrderId
                && (a.Status == ImportCostAdvanceStatus.Open || a.Status == ImportCostAdvanceStatus.PartiallySettled))
            .OrderBy(a => a.Date).ThenBy(a => a.Number).ToListAsync(ct)).Select(ToDto).ToList();

    private static ImportCostAdvanceDto ToDto(ImportCostAdvance a)
    {
        var dto = Mapper.Map<ImportCostAdvanceDto>(a);
        var open = a.Status is ImportCostAdvanceStatus.Open or ImportCostAdvanceStatus.PartiallySettled;
        dto.RemainingAmount = open ? a.Amount - a.SettledAmount : 0;
        dto.RemainingCost = open ? a.CostAmount - a.SettledCost : 0;
        return dto;
    }

    public Task<ImportCostAdvanceDto> CreateAsync(CreateImportCostAdvanceDto dto, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var advance = new ImportCostAdvance { Number = await _numbers.NextAsync("import_cost_advance", "ICA-", token) };
            await ApplyAsync(advance, dto, token);
            _db.Add(advance);
            await _db.SaveChangesAsync(token);
            return ToDto(advance);
        }, ct);

    public Task<ImportCostAdvanceDto> UpdateAsync(Guid id, UpdateImportCostAdvanceDto dto, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var advance = await _db.Set<ImportCostAdvance>().FirstOrDefaultAsync(a => a.Id == id, token) ?? throw new NotFoundException(Messages.ImportCostAdvanceNotFound);
            if (advance.Status != ImportCostAdvanceStatus.Draft) throw new ConflictException(Messages.ImportCostAdvanceNotDraft);
            await ApplyAsync(advance, dto, token);
            await _db.SaveChangesAsync(token);
            return ToDto(advance);
        }, ct);

    /// <summary>يتحقق من المستند ويحسب تكلفته وضريبته المستردة بالعملة الأساسية (كما في سطر المصروف).</summary>
    private async Task ApplyAsync(ImportCostAdvance a, CreateImportCostAdvanceDto d, CancellationToken ct)
    {
        if (d.Amount <= 0 || d.VatAmount < 0) throw new ValidationFailedException(Messages.ImportCostAdvanceValuesInvalid);
        var order = await _db.Set<CarProcurementOrder>().AsNoTracking().Where(o => o.Id == d.ProcurementOrderId)
            .Select(o => new { o.Id, o.OrderNumber }).FirstOrDefaultAsync(ct) ?? throw new ValidationFailedException(Messages.ProcurementOrderNotFound);
        var type = await _db.Set<ExpenseType>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == d.ExpenseTypeId && t.IsActive, ct)
            ?? throw new ValidationFailedException(Messages.ExpenseTypeNotFoundOrInactive);
        // نوع «ضريبة» (ضريبة الاستيراد) ضريبة مدخلات كله لا تكلفة تُوزَّع؛ وغير الخاضع لا ضريبة عليه
        if (type.IsInputTax) throw new ValidationFailedException(Messages.ImportCostAdvanceTypeNotAllowed);
        if (d.VatAmount > 0 && type.VatCategory != VatCategory.Standard) throw new ValidationFailedException(Messages.ChargeVatNotAllowedForType);
        string? supplierName = null;
        if (d.SupplierId.HasValue)
            supplierName = await _db.Set<Supplier>().AsNoTracking().Where(s => s.Id == d.SupplierId).Select(s => s.NameAr).FirstOrDefaultAsync(ct)
                ?? throw new ValidationFailedException(Messages.SupplierNotFound);
        if (string.IsNullOrWhiteSpace(d.PaymentMethodCode)) throw new ValidationFailedException(Messages.ImportCostAdvancePaymentMethodRequired);
        var account = await PaymentMethodRules.AccountForAsync(_db, d.PaymentMethodCode, null, ct);
        if (string.IsNullOrWhiteSpace(account) || !await _db.Set<Account>().AnyAsync(x => x.Code == account, ct))
            throw new ValidationFailedException(Messages.ChargePaymentAccountInvalid);
        var (currency, rate) = await ChargeShares.ResolveCurrencyAsync(_db, d.CurrencyCode, d.ExchangeRate, ct);

        var baseAmount = DocumentPricing.Round(d.Amount * rate);
        var baseVat = DocumentPricing.Round(d.VatAmount * rate);
        a.Date = d.Date ?? SaudiTime.Today;
        a.ProcurementOrderId = order.Id; a.ProcurementOrderNumber = order.OrderNumber;
        a.ExpenseTypeId = type.Id; a.ExpenseTypeCode = type.Code; a.ExpenseTypeNameAr = type.NameAr;
        a.Description = string.IsNullOrWhiteSpace(d.Description) ? null : d.Description.Trim();
        a.SupplierId = d.SupplierId; a.SupplierName = supplierName;
        a.SupplierInvoiceNumber = string.IsNullOrWhiteSpace(d.SupplierInvoiceNumber) ? null : d.SupplierInvoiceNumber.Trim();
        a.CurrencyCode = currency; a.ExchangeRate = rate; a.Amount = d.Amount; a.VatAmount = d.VatAmount;
        a.CostAmount = ChargeShares.CostAmount(type, baseAmount, baseVat);
        a.InputVatAmount = type.IsVatRecoverable ? baseVat : 0;
        a.PaymentMethodCode = d.PaymentMethodCode.Trim(); a.PaymentAccountCode = account;
        a.Notes = string.IsNullOrWhiteSpace(d.Notes) ? null : d.Notes.Trim();
    }

    public Task<ImportCostAdvanceDto> ApproveAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var advance = await _db.Set<ImportCostAdvance>().FirstOrDefaultAsync(a => a.Id == id, token) ?? throw new NotFoundException(Messages.ImportCostAdvanceNotFound);
            if (advance.Status != ImportCostAdvanceStatus.Draft) throw new ConflictException(Messages.ImportCostAdvanceNotDraft);
            var settings = await SalesCharges.SettingsAsync(_db, token);
            await ChargePosting.EnsureAccountsAsync(_db, settings, token);

            // مدين التسوية بالتكلفة ومدين ضريبة المدخلات المستردة، دائن حساب طريقة الدفع بالمدفوع كله
            var lines = new List<PostingLine> { new(settings.ImportCostsClearingAccount, advance.CostAmount, 0) };
            if (advance.InputVatAmount > 0) lines.Add(new((await PostingAccounts.SettingsAsync(_db, token)).InputVat(), advance.InputVatAmount, 0));
            lines.Add(new(advance.PaymentAccountCode, 0, advance.CostAmount + advance.InputVatAmount));
            var posted = await _posting.PostAsync(new GenericPostingRequest
            {
                Date = advance.Date, Description = $"تكاليف استيراد مقدمة {advance.Number} - {advance.ExpenseTypeNameAr} - أمر {advance.ProcurementOrderNumber}",
                SourceType = PaymentSource, SourceId = advance.Id, SourceNumber = advance.Number, Lines = lines,
            }, token);
            advance.JournalEntryId = posted.JournalEntryId;
            advance.Status = ImportCostAdvanceStatus.Open; advance.ApprovedAt = DateTime.UtcNow; advance.ApprovedBy = _user.Name;
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("IMPORT_COST_ADVANCE_APPROVED", nameof(ImportCostAdvance), advance.Id.ToString(),
                $"اعتماد {advance.Number} - أمر {advance.ProcurementOrderNumber} - {advance.CostAmount:0.00}", token);
            return ToDto(advance);
        }, ct);

    public Task<ImportCostAdvanceDto> CancelAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var advance = await _db.Set<ImportCostAdvance>().FirstOrDefaultAsync(a => a.Id == id, token) ?? throw new NotFoundException(Messages.ImportCostAdvanceNotFound);
            if (advance.Status == ImportCostAdvanceStatus.Cancelled) throw new ConflictException(Messages.ImportCostAdvanceAlreadyCancelled);
            // لا يُلغى بعد أي تسوية، ولا وهو مسحوب في سند مسودة (يُرفع منه أولاً)
            if (await _db.Set<LandedCostVoucherLine>().AnyAsync(l => l.ImportCostAdvanceId == advance.Id && l.Voucher.Status != LandedCostVoucherStatus.Cancelled, token))
                throw new ConflictException(Messages.ImportCostAdvanceHasSettlements);
            if (advance.JournalEntryId.HasValue)
            {
                await _posting.ReverseAsync(advance.JournalEntryId.Value, $"إلغاء {advance.Number}", token);
                advance.JournalEntryId = null;
            }
            advance.Status = ImportCostAdvanceStatus.Cancelled;
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("IMPORT_COST_ADVANCE_CANCELLED", nameof(ImportCostAdvance), advance.Id.ToString(), $"إلغاء {advance.Number}", token);
            return ToDto(advance);
        }, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var advance = await _db.Set<ImportCostAdvance>().FirstOrDefaultAsync(a => a.Id == id, token) ?? throw new NotFoundException(Messages.ImportCostAdvanceNotFound);
            if (advance.Status != ImportCostAdvanceStatus.Draft) throw new ConflictException(Messages.ImportCostAdvanceNotDraft);
            _db.Remove(advance);
            await _db.SaveChangesAsync(token);
        }, ct);

    public async Task<List<ImportCostAdvanceBalanceDto>> BalancesAsync(CancellationToken ct = default)
    {
        var advances = await _db.Set<ImportCostAdvance>().AsNoTracking()
            .Where(a => a.Status != ImportCostAdvanceStatus.Draft && a.Status != ImportCostAdvanceStatus.Cancelled)
            .GroupBy(a => a.ProcurementOrderId)
            .Select(g => new { OrderId = g.Key, Count = g.Count(), Paid = g.Sum(a => a.CostAmount), Settled = g.Sum(a => a.SettledCost) }).ToListAsync(ct);
        if (advances.Count == 0) return new();
        var orderIds = advances.Select(a => a.OrderId).ToList();
        var orders = await _db.Set<CarProcurementOrder>().AsNoTracking().Where(o => orderIds.Contains(o.Id))
            .Select(o => new { o.Id, o.OrderNumber, o.SupplierName, o.Status }).ToDictionaryAsync(o => o.Id, ct);
        var vehicles = await _db.Set<Vehicle>().AsNoTracking()
            .Where(v => v.ProcurementOrderId != null && orderIds.Contains(v.ProcurementOrderId.Value) && v.Status != VehicleStatus.Cancelled)
            .GroupBy(v => v.ProcurementOrderId!.Value)
            .Select(g => new { OrderId = g.Key, Total = g.Count(), Arrived = g.Count(v => v.ReceivedAt != null) }).ToDictionaryAsync(v => v.OrderId, ct);

        return advances.Select(a =>
        {
            var order = orders.GetValueOrDefault(a.OrderId);
            var cars = vehicles.GetValueOrDefault(a.OrderId);
            var status = order?.Status ?? ProcurementOrderStatus.Draft;
            var completed = status is ProcurementOrderStatus.Received or ProcurementOrderStatus.Invoiced or ProcurementOrderStatus.Closed
                || (cars != null && cars.Total > 0 && cars.Arrived == cars.Total);
            var remaining = a.Paid - a.Settled;
            return new ImportCostAdvanceBalanceDto
            {
                ProcurementOrderId = a.OrderId, ProcurementOrderNumber = order?.OrderNumber ?? string.Empty, SupplierName = order?.SupplierName,
                OrderStatus = status, AdvancesCount = a.Count, Paid = a.Paid, Settled = a.Settled, Remaining = remaining,
                VehiclesCount = cars?.Total ?? 0, ArrivedVehicles = cars?.Arrived ?? 0, OrderCompleted = completed, NeedsAttention = completed && remaining > 0,
            };
        }).OrderByDescending(r => r.NeedsAttention).ThenByDescending(r => r.Remaining).ThenBy(r => r.ProcurementOrderNumber).ToList();
    }
}
