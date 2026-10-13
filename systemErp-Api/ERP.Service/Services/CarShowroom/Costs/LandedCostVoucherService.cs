using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.CarShowroom;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

/// <summary>
/// سند التكاليف الإضافية: يسجّل فاتورة مورد خدمات ويوزّعها على السيارات. الحصص تُحسب وتُحفظ مع المسودة (هي نفسها ما يُعرض
/// في المعاينة وما يُعتمد)، وعند الاعتماد يصير كل سطر مصروفاً فعلياً يحل محل التقديري المقابل وتُعدَّل تكلفة السيارات بالفرق.
/// </summary>
public class LandedCostVoucherService : ILandedCostVoucherService
{
    private readonly ErpDbContext _db;
    private readonly ITransactionRunner _tx;
    private readonly INumberSequenceService _numbers;
    private readonly IPermissionService _permissions;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;
    private readonly IAccountingPostingService _posting;

    public LandedCostVoucherService(ErpDbContext db, ITransactionRunner tx, INumberSequenceService numbers, IPermissionService permissions, ICurrentUser user, IAuditService audit,
        IAccountingPostingService posting)
    {
        _db = db; _tx = tx; _numbers = numbers; _permissions = permissions; _user = user; _audit = audit; _posting = posting;
    }

    private IQueryable<LandedCostVoucher> Vouchers => _db.Set<LandedCostVoucher>().Include(v => v.Lines).ThenInclude(l => l.Allocations).AsSplitQuery();

    public async Task<PagedResult<LandedCostVoucherDto>> ListAsync(PaginationParams p, CancellationToken ct = default)
    {
        var q = _db.Set<LandedCostVoucher>().AsNoTracking().AsQueryable();
        if (Enum.TryParse<LandedCostVoucherStatus>(p.Status, true, out var status)) q = q.Where(v => v.Status == status);
        if (p.StartDate.HasValue) q = q.Where(v => v.Date >= p.StartDate);
        if (p.EndDate.HasValue) q = q.Where(v => v.Date <= p.EndDate);
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(v => v.VoucherNumber.Contains(t) || v.SupplierName.Contains(t) || (v.SupplierInvoiceNumber != null && v.SupplierInvoiceNumber.Contains(t)));
        }
        var total = await q.CountAsync(ct);
        var items = await q.Include(v => v.Lines).ThenInclude(l => l.Allocations).AsSplitQuery().OrderByDescending(v => v.Date).ThenByDescending(v => v.VoucherNumber)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);
        var vins = await VinsAsync(items.SelectMany(v => v.Lines).SelectMany(l => l.Allocations).Select(a => a.VehicleId), ct);
        return new PagedResult<LandedCostVoucherDto>
        {
            Items = items.Select(v => ToDto(v, vins)).ToList(), TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    public async Task<LandedCostVoucherDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var voucher = await Vouchers.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct) ?? throw new NotFoundException(Messages.LandedCostVoucherNotFound);
        return ToDto(voucher, await VinsAsync(voucher.Lines.SelectMany(l => l.Allocations).Select(a => a.VehicleId), ct));
    }

    public async Task<LandedCostPreviewDto> PreviewAsync(CreateLandedCostVoucherDto dto, CancellationToken ct = default)
    {
        var voucher = await BuildAsync(dto, ct);
        var matches = await MatchAsync(voucher, ct);
        var vehicleIds = voucher.Lines.SelectMany(l => l.Allocations).Select(a => a.VehicleId).Distinct().ToList();
        var vehicles = await _db.Set<Vehicle>().AsNoTracking().Where(v => vehicleIds.Contains(v.Id)).ToListAsync(ct);
        var vins = vehicles.ToDictionary(v => v.Id, v => v.ChassisNumber);

        var preview = new LandedCostPreviewDto
        {
            CurrencyCode = voucher.CurrencyCode, ExchangeRate = voucher.ExchangeRate, Subtotal = voucher.Subtotal, VatTotal = voucher.VatTotal,
            GrandTotal = voucher.GrandTotal, BaseCostTotal = voucher.BaseCostTotal,
        };
        foreach (var line in voucher.Lines.OrderBy(l => l.LineNo))
        {
            var row = Mapper.Map<LandedCostVoucherLineDto>(line);
            foreach (var a in row.Allocations) a.ChassisNumber = vins.GetValueOrDefault(a.VehicleId);
            if (matches.TryGetValue(line, out var replaced))
            {
                row.ReplacedLineId = replaced.Id; row.EstimatedAmount = replaced.CostAmount; row.Variance = line.CostAmount - replaced.CostAmount;
            }
            preview.Lines.Add(row);
        }
        foreach (var v in vehicles.OrderBy(v => v.ChassisNumber))
        {
            var capitalized = voucher.Lines.Where(l => l.Treatment == ChargeTreatment.Capitalize).SelectMany(l => l.Allocations).Where(a => a.VehicleId == v.Id).Sum(a => a.Amount);
            // سطر فعلي مستحق (رسوم أمر الشراء) يخرج من التكلفة حين يحل السند محله
            var replacedActual = matches.Values.Where(l => l.Status == ChargeStatus.Actual && l.Treatment == ChargeTreatment.Capitalize)
                .SelectMany(l => l.Allocations).Where(a => a.VehicleId == v.Id).Sum(a => a.Amount);
            preview.Vehicles.Add(new LandedCostPreviewVehicleDto
            {
                VehicleId = v.Id, ChassisNumber = v.ChassisNumber, Status = v.Status,
                Description = $"{v.BrandNameAr} {v.ModelNameAr} {v.TrimNameAr} {v.Year}".Replace("  ", " ").Trim(),
                CurrentCost = v.TotalCost, Capitalized = capitalized, ReplacedActual = replacedActual, CostAfter = v.TotalCost + capitalized - replacedActual,
            });
        }
        return preview;
    }

    public Task<LandedCostVoucherDto> CreateAsync(CreateLandedCostVoucherDto dto, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var voucher = await BuildAsync(dto, token);
            voucher.VoucherNumber = await _numbers.NextAsync("landed_cost_voucher", "LCV-", token);
            _db.Add(voucher);
            await _db.SaveChangesAsync(token);
            return await GetAsync(voucher.Id, token);
        }, ct);

    public Task<LandedCostVoucherDto> UpdateAsync(Guid id, UpdateLandedCostVoucherDto dto, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var voucher = await Vouchers.FirstOrDefaultAsync(v => v.Id == id, token) ?? throw new NotFoundException(Messages.LandedCostVoucherNotFound);
            if (voucher.Status != LandedCostVoucherStatus.Draft) throw new ConflictException(Messages.LandedCostVoucherNotDraft);
            var fresh = await BuildAsync(dto, token);

            _db.RemoveRange(voucher.Lines.SelectMany(l => l.Allocations));
            _db.RemoveRange(voucher.Lines);
            voucher.Lines.Clear();
            voucher.Date = fresh.Date; voucher.SupplierId = fresh.SupplierId; voucher.SupplierName = fresh.SupplierName;
            voucher.SupplierInvoiceNumber = fresh.SupplierInvoiceNumber; voucher.CurrencyCode = fresh.CurrencyCode; voucher.ExchangeRate = fresh.ExchangeRate;
            voucher.ProcurementOrderId = fresh.ProcurementOrderId; voucher.ProcurementOrderNumber = fresh.ProcurementOrderNumber; voucher.Notes = fresh.Notes;
            voucher.Subtotal = fresh.Subtotal; voucher.VatTotal = fresh.VatTotal; voucher.GrandTotal = fresh.GrandTotal; voucher.BaseCostTotal = fresh.BaseCostTotal;
            foreach (var line in fresh.Lines)
            {
                voucher.Lines.Add(line);
                _db.Add(line);
            }
            await _db.SaveChangesAsync(token);
            return await GetAsync(id, token);
        }, ct);

    public Task<LandedCostVoucherDto> ApproveAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var voucher = await Vouchers.FirstOrDefaultAsync(v => v.Id == id, token) ?? throw new NotFoundException(Messages.LandedCostVoucherNotFound);
            if (voucher.Status != LandedCostVoucherStatus.Draft) throw new ConflictException(Messages.LandedCostVoucherNotDraft);

            var matches = await MatchAsync(voucher, token);
            var affected = voucher.Lines.SelectMany(l => l.Allocations).Select(a => a.VehicleId)
                .Union(matches.Values.SelectMany(l => l.Allocations).Select(a => a.VehicleId)).Distinct().ToList();
            // تكلفة سيارة مباعة لا تُمسّ من هنا (يُعالَج فرقها على تكلفة المبيعات بقيد تعديل)
            var touchesCost = voucher.Lines.Where(l => l.Treatment == ChargeTreatment.Capitalize).SelectMany(l => l.Allocations).Select(a => a.VehicleId)
                .Union(matches.Values.Where(l => l.Status == ChargeStatus.Actual && l.Treatment == ChargeTreatment.Capitalize).SelectMany(l => l.Allocations).Select(a => a.VehicleId)).ToList();
            // سيارة مباعة: الفرق على تكلفة المبيعات بقيد تعديل موثق، باعتماد من يملك صلاحيته
            var sold = await SoldVehicleAdjustments.EnsureAllowedAsync(_db, _permissions, touchesCost, $"سند التكاليف الإضافية {voucher.VoucherNumber}", system: false, token);
            var costBefore = matches.Values.Where(l => l.Status == ChargeStatus.Actual && l.Treatment == ChargeTreatment.Capitalize)
                .SelectMany(l => l.Allocations).GroupBy(a => a.VehicleId).ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));
            var costAfter = voucher.Lines.Where(l => l.Treatment == ChargeTreatment.Capitalize)
                .SelectMany(l => l.Allocations).GroupBy(a => a.VehicleId).ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));
            await VehicleCostLedger.EnsureOpeningAsync(_db, affected, token);

            var types = await TypesAsync(voucher.Lines.Select(l => l.ExpenseTypeId), token);
            var charges = new List<AdditionalChargeLine>();
            // مستحق رُحِّل سابقاً من حقول أمر الشراء ويحل السند محله: يُعكس أثره في قيد السند
            var replacedPosted = matches.Values.Where(l => l.Source == ChargeSource.OrderFields && l.JournalEntryId != null).ToList();
            foreach (var line in voucher.Lines.OrderBy(l => l.LineNo))
            {
                var type = types[line.ExpenseTypeId];
                var charge = new AdditionalChargeLine
                {
                    DocumentType = ChargeDocumentTypes.LandedCostVoucher, DocumentId = voucher.Id, DocumentNumber = voucher.VoucherNumber,
                    ExpenseTypeId = type.Id, ExpenseTypeCode = type.Code, ExpenseTypeNameAr = type.NameAr, Stage = type.Stage,
                    Date = voucher.Date, Description = line.Description,
                    Amount = line.Amount, CurrencyCode = voucher.CurrencyCode, ExchangeRate = voucher.ExchangeRate, BaseAmount = line.BaseAmount,
                    VatCategory = type.VatCategory, VatAmount = line.VatAmount, IsVatRecoverable = type.IsVatRecoverable, IsInputTax = type.IsInputTax,
                    CostAmount = line.CostAmount, Treatment = line.Treatment, Status = ChargeStatus.Actual, Source = ChargeSource.LandedCostVoucher,
                    AllocationMethod = line.AllocationMethod, SupplierId = voucher.SupplierId, SupplierName = voucher.SupplierName,
                    SupplierInvoiceNumber = voucher.SupplierInvoiceNumber,
                };
                foreach (var a in line.Allocations) charge.Allocations.Add(new AdditionalChargeAllocation { VehicleId = a.VehicleId, Amount = a.Amount });

                // الفعلي يحل محل التقديري المقابل؛ القيمتان والفرق يُحفظان على سطر السند
                if (matches.TryGetValue(line, out var replaced))
                {
                    charge.ReplacesLineId = replaced.Id;
                    line.ReplacedLineId = replaced.Id; line.ReplacedLineStatus = replaced.Status;
                    line.EstimatedAmount = replaced.CostAmount; line.Variance = line.CostAmount - replaced.CostAmount;
                    replaced.Status = ChargeStatus.Replaced;
                }
                _db.Add(charge);
                charges.Add(charge);
                line.ChargeLineId = charge.Id;
            }
            voucher.Status = LandedCostVoucherStatus.Approved; voucher.ApprovedAt = DateTime.UtcNow; voucher.ApprovedBy = _user.Name;
            await _db.SaveChangesAsync(token);
            await VehicleCostLedger.RecomputeAsync(_db, affected, token);
            await _db.SaveChangesAsync(token);
            voucher.JournalEntryId = await ChargePosting.PostVoucherAsync(_db, _posting, voucher, charges, replacedPosted, token);
            await _db.SaveChangesAsync(token);
            await SoldVehicleAdjustments.RecordAsync(_db, _numbers, _user.Name, SoldVehicleAdjustments.Deltas(costBefore, costAfter, sold),
                $"سند التكاليف الإضافية {voucher.VoucherNumber}", "landed_cost_voucher", null, voucher.JournalEntryId, token);
            await _audit.LogAsync("LANDED_COST_VOUCHER_APPROVED", nameof(LandedCostVoucher), voucher.Id.ToString(),
                $"اعتماد سند {voucher.VoucherNumber} - {voucher.SupplierName} - {voucher.BaseCostTotal:0.00} على {affected.Count} سيارة", token);
            return await GetAsync(id, token);
        }, ct);

    public Task<LandedCostVoucherDto> CancelAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var voucher = await Vouchers.FirstOrDefaultAsync(v => v.Id == id, token) ?? throw new NotFoundException(Messages.LandedCostVoucherNotFound);
            if (voucher.Status == LandedCostVoucherStatus.Cancelled) throw new ConflictException(Messages.LandedCostVoucherAlreadyCancelled);

            if (voucher.Status == LandedCostVoucherStatus.Approved)
            {
                var charges = await _db.Set<AdditionalChargeLine>().Include(l => l.Allocations)
                    .Where(l => l.DocumentType == ChargeDocumentTypes.LandedCostVoucher && l.DocumentId == voucher.Id).ToListAsync(token);
                var replacedIds = voucher.Lines.Where(l => l.ReplacedLineId.HasValue).Select(l => l.ReplacedLineId!.Value).ToList();
                var replaced = await _db.Set<AdditionalChargeLine>().Include(l => l.Allocations).Where(l => replacedIds.Contains(l.Id)).ToListAsync(token);
                var affected = charges.Concat(replaced).SelectMany(l => l.Allocations).Select(a => a.VehicleId).Distinct().ToList();
                var reason = $"إلغاء سند التكاليف الإضافية {voucher.VoucherNumber}";
                var sold = await SoldVehicleAdjustments.EnsureAllowedAsync(_db, _permissions, affected, reason, system: false, token);
                var costBefore = charges.Where(l => l.Treatment == ChargeTreatment.Capitalize)
                    .SelectMany(l => l.Allocations).GroupBy(a => a.VehicleId).ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));
                var costAfter = voucher.Lines.Where(l => l.ReplacedLineStatus == ChargeStatus.Actual)
                    .SelectMany(l => replaced.Where(x => x.Id == l.ReplacedLineId && x.Treatment == ChargeTreatment.Capitalize)).SelectMany(l => l.Allocations)
                    .GroupBy(a => a.VehicleId).ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));
                if (voucher.JournalEntryId != null)
                {
                    await _posting.ReverseAsync(voucher.JournalEntryId.Value, $"إلغاء سند التكاليف الإضافية {voucher.VoucherNumber}", token);
                    await ChargePosting.UndoReclassAsync(_db, _posting, charges, token);
                    voucher.JournalEntryId = null;
                }

                // عكس الاعتماد: تُحذف المصاريف الفعلية وتعود الأسطر المستبدَلة إلى حالتها
                foreach (var line in voucher.Lines)
                {
                    var back = replaced.FirstOrDefault(l => l.Id == line.ReplacedLineId);
                    if (back != null) back.Status = line.ReplacedLineStatus ?? ChargeStatus.Estimated;
                    line.ReplacedLineId = null; line.ReplacedLineStatus = null; line.EstimatedAmount = null; line.Variance = null; line.ChargeLineId = null;
                }
                _db.RemoveRange(charges.SelectMany(l => l.Allocations));
                _db.RemoveRange(charges);
                voucher.Status = LandedCostVoucherStatus.Cancelled;
                await _db.SaveChangesAsync(token);
                await VehicleCostLedger.RecomputeAsync(_db, affected, token);
                await SoldVehicleAdjustments.RecordAsync(_db, _numbers, _user.Name, SoldVehicleAdjustments.Deltas(costBefore, costAfter, sold),
                    reason, "landed_cost_voucher", null, null, token);
            }
            voucher.Status = LandedCostVoucherStatus.Cancelled;
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("LANDED_COST_VOUCHER_CANCELLED", nameof(LandedCostVoucher), voucher.Id.ToString(), $"إلغاء سند {voucher.VoucherNumber}", token);
            return await GetAsync(id, token);
        }, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var voucher = await Vouchers.FirstOrDefaultAsync(v => v.Id == id, token) ?? throw new NotFoundException(Messages.LandedCostVoucherNotFound);
            if (voucher.Status != LandedCostVoucherStatus.Draft) throw new ConflictException(Messages.LandedCostVoucherNotDraft);
            _db.RemoveRange(voucher.Lines.SelectMany(l => l.Allocations));
            _db.RemoveRange(voucher.Lines);
            _db.Remove(voucher);
            await _db.SaveChangesAsync(token);
        }, ct);

    // ---------------- الداخلي ----------------
    private async Task<Dictionary<Guid, ExpenseType>> TypesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await _db.Set<ExpenseType>().AsNoTracking().Where(t => list.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
    }

    private async Task<Dictionary<Guid, string>> VinsAsync(IEnumerable<Guid> vehicleIds, CancellationToken ct)
    {
        var ids = vehicleIds.Distinct().ToList();
        return await _db.Set<Vehicle>().AsNoTracking().Where(v => ids.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.ChassisNumber, ct);
    }

    private static LandedCostVoucherDto ToDto(LandedCostVoucher voucher, Dictionary<Guid, string> vins)
    {
        var dto = Mapper.Map<LandedCostVoucherDto>(voucher);
        dto.Lines = dto.Lines.OrderBy(l => l.LineNo).ToList();
        foreach (var a in dto.Lines.SelectMany(l => l.Allocations)) a.ChassisNumber = vins.GetValueOrDefault(a.VehicleId);
        return dto;
    }

    /// <summary>يتحقق من الطلب ويبني السند بأسطره وحصصه محسوبةً (بلا رقم ولا حفظ).</summary>
    private async Task<LandedCostVoucher> BuildAsync(CreateLandedCostVoucherDto d, CancellationToken ct)
    {
        if (d.Lines.Count == 0) throw new ValidationFailedException(Messages.LandedCostVoucherNeedsLines);
        var supplier = await _db.Set<Supplier>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == d.SupplierId, ct)
            ?? throw new ValidationFailedException(Messages.SupplierNotFound);
        var (currency, rate) = await ChargeShares.ResolveCurrencyAsync(_db, d.CurrencyCode, d.ExchangeRate, ct);

        string? orderNumber = null;
        var defaultVehicles = d.VehicleIds.Distinct().ToList();
        if (d.ProcurementOrderId.HasValue)
        {
            orderNumber = await _db.Set<CarProcurementOrder>().AsNoTracking().Where(o => o.Id == d.ProcurementOrderId).Select(o => o.OrderNumber).FirstOrDefaultAsync(ct)
                ?? throw new ValidationFailedException(Messages.ProcurementOrderNotFound);
            if (defaultVehicles.Count == 0) defaultVehicles = await ProcurementCharges.VehicleIdsAsync(_db, d.ProcurementOrderId.Value, ct);
        }

        var types = await TypesAsync(d.Lines.Select(l => l.ExpenseTypeId), ct);
        var allVehicleIds = defaultVehicles.Concat(d.Lines.SelectMany(l => l.Allocations).Select(a => a.VehicleId)).Distinct().ToList();
        var weights = await ChargeShares.LoadAsync(_db, allVehicleIds, ct);
        var canOverride = (bool?)null;

        var voucher = new LandedCostVoucher
        {
            Date = d.Date ?? DateTime.UtcNow, SupplierId = supplier.Id, SupplierName = supplier.NameAr,
            SupplierInvoiceNumber = string.IsNullOrWhiteSpace(d.SupplierInvoiceNumber) ? null : d.SupplierInvoiceNumber.Trim(),
            CurrencyCode = currency, ExchangeRate = rate, ProcurementOrderId = d.ProcurementOrderId, ProcurementOrderNumber = orderNumber,
            Notes = string.IsNullOrWhiteSpace(d.Notes) ? null : d.Notes.Trim(), Status = LandedCostVoucherStatus.Draft,
        };

        for (var i = 0; i < d.Lines.Count; i++)
        {
            var src = d.Lines[i];
            var n = i + 1;
            string Line(string message) => string.Format(Messages.LineMessage, n, message);

            if (!types.TryGetValue(src.ExpenseTypeId, out var type) || !type.IsActive) throw new ValidationFailedException(Line(Messages.ExpenseTypeNotFoundOrInactive));
            if (src.Amount <= 0) throw new ValidationFailedException(Line(Messages.ChargeAmountMustBePositive));
            if (src.VatAmount < 0) throw new ValidationFailedException(Line(Messages.ChargeVatCannotBeNegative));
            if (src.VatAmount > 0 && (type.IsInputTax || type.VatCategory != VatCategory.Standard)) throw new ValidationFailedException(Line(Messages.ChargeVatNotAllowedForType));

            var treatment = src.Treatment ?? type.DefaultTreatment;
            if (treatment != type.DefaultTreatment)
            {
                canOverride ??= await _permissions.HasPermissionAsync(PermissionService.ChargeTreatmentOverrideScreenId, ScreenAction.Edit, ct);
                if (canOverride != true) throw new ForbiddenException(Messages.ChargeTreatmentOverrideForbidden);
            }

            var vehicleIds = src.Allocations.Count > 0 ? src.Allocations.Select(a => a.VehicleId).ToList() : defaultVehicles;
            if (vehicleIds.Count == 0) throw new ValidationFailedException(string.Format(Messages.LandedCostVoucherNeedsVehicles, n));
            if (vehicleIds.Distinct().Count() != vehicleIds.Count) throw new ValidationFailedException(Line(Messages.ChargeVehicleRepeated));

            var baseAmount = DocumentPricing.Round(src.Amount * rate);
            var costAmount = ChargeShares.CostAmount(type, baseAmount, DocumentPricing.Round(src.VatAmount * rate));
            var method = src.AllocationMethod ?? type.DefaultAllocationMethod;
            decimal[] shares;
            try { shares = ChargeShares.Compute(method, costAmount, vehicleIds.Select(id => weights[id]).ToList(), src.Allocations.Select(a => a.Amount).ToList()); }
            catch (ValidationFailedException ex) { throw new ValidationFailedException(Line(ex.Message)); }

            var line = new LandedCostVoucherLine
            {
                LineNo = n, ExpenseTypeId = type.Id, ExpenseTypeCode = type.Code, ExpenseTypeNameAr = type.NameAr,
                Description = string.IsNullOrWhiteSpace(src.Description) ? null : src.Description.Trim(),
                Amount = src.Amount, VatAmount = src.VatAmount, BaseAmount = baseAmount, CostAmount = costAmount,
                Treatment = treatment, AllocationMethod = method, ReplacesLineId = src.ReplacesLineId,
            };
            for (var k = 0; k < vehicleIds.Count; k++) line.Allocations.Add(new LandedCostVoucherAllocation { VehicleId = vehicleIds[k], Amount = shares[k] });
            voucher.Lines.Add(line);
        }

        voucher.Subtotal = d.Lines.Sum(l => l.Amount);
        voucher.VatTotal = d.Lines.Sum(l => l.VatAmount);
        voucher.GrandTotal = voucher.Subtotal + voucher.VatTotal;
        voucher.BaseCostTotal = voucher.Lines.Sum(l => l.CostAmount);
        return voucher;
    }

    /// <summary>
    /// لكل سطر في السند: السطر الذي يحل محله. المحدَّد يدوياً أولاً؛ وإلا أقدم سطر من النوع نفسه، تقديري أو مستحق من حقول أمر الشراء،
    /// على أمر شراء السند أو على إحدى سيارات السطر. السطر الواحد لا يُستبدَل مرتين.
    /// </summary>
    private async Task<Dictionary<LandedCostVoucherLine, AdditionalChargeLine>> MatchAsync(LandedCostVoucher voucher, CancellationToken ct)
    {
        var typeIds = voucher.Lines.Select(l => l.ExpenseTypeId).Distinct().ToList();
        var vehicleIds = voucher.Lines.SelectMany(l => l.Allocations).Select(a => a.VehicleId).Distinct().ToList();
        var explicitIds = voucher.Lines.Where(l => l.ReplacesLineId.HasValue).Select(l => l.ReplacesLineId!.Value).ToList();
        var candidates = await _db.Set<AdditionalChargeLine>().Include(l => l.Allocations)
            .Where(l => explicitIds.Contains(l.Id) || (typeIds.Contains(l.ExpenseTypeId)
                && (l.Status == ChargeStatus.Estimated || (l.Status == ChargeStatus.Actual && l.Source == ChargeSource.OrderFields))
                && ((voucher.ProcurementOrderId != null && l.DocumentType == ChargeDocumentTypes.ProcurementOrder && l.DocumentId == voucher.ProcurementOrderId)
                    || l.Allocations.Any(a => vehicleIds.Contains(a.VehicleId)))))
            .OrderBy(l => l.Date).ThenBy(l => l.CreatedAt).ToListAsync(ct);

        static bool Replaceable(AdditionalChargeLine l)
            => l.Status == ChargeStatus.Estimated || (l.Status == ChargeStatus.Actual && l.Source == ChargeSource.OrderFields);

        var matches = new Dictionary<LandedCostVoucherLine, AdditionalChargeLine>();
        var taken = new HashSet<Guid>();
        foreach (var line in voucher.Lines.Where(l => l.ReplacesLineId.HasValue).OrderBy(l => l.LineNo))
        {
            var chosen = candidates.FirstOrDefault(c => c.Id == line.ReplacesLineId);
            if (chosen == null || chosen.ExpenseTypeId != line.ExpenseTypeId || !Replaceable(chosen) || !taken.Add(chosen.Id))
                throw new ValidationFailedException(string.Format(Messages.LandedCostReplacedLineInvalid, line.LineNo));
            matches[line] = chosen;
        }
        foreach (var line in voucher.Lines.Where(l => !l.ReplacesLineId.HasValue).OrderBy(l => l.LineNo))
        {
            var lineVehicles = line.Allocations.Select(a => a.VehicleId).ToHashSet();
            var chosen = candidates.FirstOrDefault(c => c.ExpenseTypeId == line.ExpenseTypeId && Replaceable(c) && !taken.Contains(c.Id)
                && ((voucher.ProcurementOrderId != null && c.DocumentType == ChargeDocumentTypes.ProcurementOrder && c.DocumentId == voucher.ProcurementOrderId)
                    || c.Allocations.Any(a => lineVehicles.Contains(a.VehicleId))));
            if (chosen == null) continue;
            taken.Add(chosen.Id);
            matches[line] = chosen;
        }
        return matches;
    }
}
