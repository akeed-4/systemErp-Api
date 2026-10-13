using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.CarShowroom;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

/// <summary>
/// أسطر المصاريف والتكاليف الإضافية على مستندات السيارات. السطر يرث معالجته وتوزيعه وفئته الضريبية من نوعه،
/// ويُوزَّع على سياراته بالعملة الأساسية، وبعد كل تغيير تُشتق تكلفة السيارات المتأثرة من الأسطر الفعلية المُرسمَلة.
/// </summary>
public class AdditionalChargeService : IAdditionalChargeService
{
    private readonly ErpDbContext _db;
    private readonly ITransactionRunner _tx;
    private readonly IPermissionService _permissions;
    private readonly ICostVisibility _cost;
    private readonly IAuditService _audit;
    private readonly IAccountingPostingService _posting;
    private readonly INumberSequenceService _numbers;
    private readonly ICurrentUser _user;

    public AdditionalChargeService(ErpDbContext db, ITransactionRunner tx, IPermissionService permissions, ICostVisibility cost, IAuditService audit,
        IAccountingPostingService posting, INumberSequenceService numbers, ICurrentUser user)
    {
        _db = db; _tx = tx; _permissions = permissions; _cost = cost; _audit = audit; _posting = posting; _numbers = numbers; _user = user;
    }

    public async Task<List<AdditionalChargeLineDto>> ListAsync(string? documentType, Guid? documentId, Guid? vehicleId, CancellationToken ct = default)
    {
        var byDocument = !string.IsNullOrWhiteSpace(documentType) && documentId.HasValue;
        if (!byDocument && !vehicleId.HasValue) throw new ValidationFailedException(Messages.ChargeFilterRequired);

        var q = _db.Set<AdditionalChargeLine>().AsNoTracking().AsQueryable();
        if (byDocument)
        {
            // أسطر أمر الشراء/عقد البيع تنتقل مع المستند عند تحويله إلى فاتورة: تظهر عليها دون نسخها
            var source = documentType == ChargeDocumentTypes.Invoice
                ? await _db.Set<Invoice>().AsNoTracking().Where(i => i.Id == documentId).Select(i => new { i.ReferenceType, i.ReferenceId }).FirstOrDefaultAsync(ct)
                : null;
            q = source?.ReferenceId != null && source.ReferenceType is ChargeDocumentTypes.ProcurementOrder or ChargeDocumentTypes.SalesContract
                ? q.Where(l => (l.DocumentType == documentType && l.DocumentId == documentId) || (l.DocumentType == source.ReferenceType && l.DocumentId == source.ReferenceId))
                : q.Where(l => l.DocumentType == documentType && l.DocumentId == documentId);
        }
        if (vehicleId.HasValue) q = q.Where(l => l.Allocations.Any(a => a.VehicleId == vehicleId));
        if (!await _cost.CanViewAsync(ct)) q = q.Where(l => l.Treatment == ChargeTreatment.RechargeToCustomer);

        var lines = await q.Include(l => l.Allocations).OrderBy(l => l.Date).ThenBy(l => l.CreatedAt).ToListAsync(ct);
        return await ToDtosAsync(lines, ct);
    }

    public async Task<AdditionalChargeLineDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var line = await _db.Set<AdditionalChargeLine>().AsNoTracking().Include(l => l.Allocations).FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NotFoundException(Messages.ChargeLineNotFound);
        if (line.Treatment != ChargeTreatment.RechargeToCustomer && !await _cost.CanViewAsync(ct)) throw new ForbiddenException();
        return (await ToDtosAsync(new List<AdditionalChargeLine> { line }, ct))[0];
    }

    /// <summary>نتيجة عملية كتابة: السطر الذي أدخله المستخدم للتو يعود إليه ولو كان لا يرى أسطر التكلفة في القوائم.</summary>
    private async Task<AdditionalChargeLineDto> SavedAsync(Guid id, CancellationToken ct)
    {
        var line = await _db.Set<AdditionalChargeLine>().AsNoTracking().Include(l => l.Allocations).FirstAsync(l => l.Id == id, ct);
        return (await ToDtosAsync(new List<AdditionalChargeLine> { line }, ct))[0];
    }

    public Task<AdditionalChargeLineDto> CreateAsync(CreateAdditionalChargeLineDto dto, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var line = new AdditionalChargeLine { Source = ChargeSource.Manual };
            var resolved = await ResolveAsync(dto, null, token);
            await SaveAsync(line, resolved, isNew: true, token);
            await _audit.LogAsync("CHARGE_LINE_CREATED", nameof(AdditionalChargeLine), line.Id.ToString(), Describe(line), token);
            return await SavedAsync(line.Id, token);
        }, ct);

    public Task<AdditionalChargeLineDto> UpdateAsync(Guid id, UpdateAdditionalChargeLineDto dto, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var line = await LoadEditableAsync(id, token);
            if (line.Status is ChargeStatus.Cancelled or ChargeStatus.Replaced) throw new ConflictException(Messages.ChargeLineAlreadyCancelled);
            var resolved = await ResolveAsync(dto, line, token);
            await SaveAsync(line, resolved, isNew: false, token);
            await _audit.LogAsync("CHARGE_LINE_UPDATED", nameof(AdditionalChargeLine), line.Id.ToString(), Describe(line), token);
            return await SavedAsync(line.Id, token);
        }, ct);

    public Task<AdditionalChargeLineDto> CancelAsync(Guid id, string? reason = null, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var line = await LoadEditableAsync(id, token);
            if (line.Status is ChargeStatus.Cancelled or ChargeStatus.Replaced) throw new ConflictException(Messages.ChargeLineAlreadyCancelled);
            var vehicleIds = line.Allocations.Select(a => a.VehicleId).ToList();
            var inCost = CostShares(line);
            var sold = inCost.Count > 0 ? await SoldVehicleAdjustments.EnsureAllowedAsync(_db, _permissions, vehicleIds, reason, system: false, token) : new();

            await EnsureContractOpenAsync(line.DocumentType, line.DocumentId, line.Treatment == ChargeTreatment.RechargeToCustomer, token);
            await VehicleCostLedger.EnsureOpeningAsync(_db, vehicleIds, token);
            await ChargePosting.UnpostAsync(_db, _posting, line, "إلغاء سطر المصروف", token);
            line.Status = ChargeStatus.Cancelled;
            await _db.SaveChangesAsync(token);
            await RecomputeAsync(vehicleIds, token);
            await SoldVehicleAdjustments.RecordAsync(_db, _numbers, _user.Name, SoldVehicleAdjustments.Deltas(inCost, new Dictionary<Guid, decimal>(), sold),
                reason ?? string.Empty, "charge_line", line.Id, null, token);
            await RefreshDocumentAsync(line.DocumentType, line.DocumentId, token);
            await _audit.LogAsync("CHARGE_LINE_CANCELLED", nameof(AdditionalChargeLine), line.Id.ToString(), Describe(line), token);
            return await SavedAsync(line.Id, token);
        }, ct);

    public Task DeleteAsync(Guid id, string? reason = null, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var line = await LoadEditableAsync(id, token);
            // سطر استبدله سند تكاليف يبقى سجلاً للقيمة التقديرية
            if (line.Status == ChargeStatus.Replaced) throw new ConflictException(Messages.ChargeLineAlreadyCancelled);
            var vehicleIds = line.Allocations.Select(a => a.VehicleId).ToList();
            var inCost = CostShares(line);
            var sold = inCost.Count > 0 ? await SoldVehicleAdjustments.EnsureAllowedAsync(_db, _permissions, vehicleIds, reason, system: false, token) : new();

            await EnsureContractOpenAsync(line.DocumentType, line.DocumentId, line.Treatment == ChargeTreatment.RechargeToCustomer, token);
            await VehicleCostLedger.EnsureOpeningAsync(_db, vehicleIds, token);
            var description = Describe(line);
            await ChargePosting.UnpostAsync(_db, _posting, line, "حذف سطر المصروف", token);
            _db.RemoveRange(line.Allocations);
            _db.Remove(line);
            await _db.SaveChangesAsync(token);
            await RecomputeAsync(vehicleIds, token);
            await SoldVehicleAdjustments.RecordAsync(_db, _numbers, _user.Name, SoldVehicleAdjustments.Deltas(inCost, new Dictionary<Guid, decimal>(), sold),
                reason ?? string.Empty, "charge_line", null, null, token);
            await RefreshDocumentAsync(line.DocumentType, line.DocumentId, token);
            await _audit.LogAsync("CHARGE_LINE_DELETED", nameof(AdditionalChargeLine), id.ToString(), description, token);
        }, ct);

    // ---------------- الداخلي ----------------
    private static bool EntersCost(ChargeStatus status, ChargeTreatment treatment)
        => status == ChargeStatus.Actual && treatment == ChargeTreatment.Capitalize;

    private static string Describe(AdditionalChargeLine l)
        => $"{l.ExpenseTypeNameAr} {l.Amount:0.00} {l.CurrencyCode} - {l.Treatment}/{l.Status} - {l.DocumentType} {l.DocumentNumber}";

    /// <summary>الأسطر اليدوية فقط تُعدَّل من هنا؛ أسطر الرصيد الافتتاحي وتم وسند التكاليف يديرها مصدرها.</summary>
    private async Task<AdditionalChargeLine> LoadEditableAsync(Guid id, CancellationToken ct)
    {
        var line = await _db.Set<AdditionalChargeLine>().Include(l => l.Allocations).FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NotFoundException(Messages.ChargeLineNotFound);
        if (line.Source != ChargeSource.Manual) throw new ConflictException(Messages.ChargeLineNotEditable);
        return line;
    }

    /// <summary>حصص السطر الداخلة حالياً في تكلفة السيارات (فارغ لسطر لا يدخل التكلفة).</summary>
    private static Dictionary<Guid, decimal> CostShares(AdditionalChargeLine line)
        => EntersCost(line.Status, line.Treatment) ? line.Allocations.ToDictionary(a => a.VehicleId, a => a.Amount) : new();

    private async Task RecomputeAsync(IReadOnlyCollection<Guid> vehicleIds, CancellationToken ct)
    {
        await VehicleCostLedger.RecomputeAsync(_db, vehicleIds, ct);
        await _db.SaveChangesAsync(ct);
    }

    private sealed record Resolved(CreateAdditionalChargeLineDto Dto, ExpenseType Type, string DocumentType, Guid? DocumentId, string? DocumentNumber,
        string CurrencyCode, decimal ExchangeRate, decimal BaseAmount, decimal CostAmount, ChargeTreatment Treatment, ChargeAllocationMethod Method,
        Supplier? Supplier, List<(Guid VehicleId, decimal Amount)> Shares);

    /// <summary>أسطر عقد بيع مفوتَر أو ملغى المحمّلة على العميل مقفلة: فاتورته صدرت بها.</summary>
    private async Task EnsureContractOpenAsync(string documentType, Guid? documentId, bool chargedToCustomer, CancellationToken ct)
    {
        if (!chargedToCustomer || documentType != ChargeDocumentTypes.SalesContract || !documentId.HasValue) return;
        var status = await _db.Set<CarSalesContract>().AsNoTracking().Where(c => c.Id == documentId).Select(c => (SalesContractStatus?)c.Status).FirstOrDefaultAsync(ct);
        if (status is SalesContractStatus.Invoiced or SalesContractStatus.Cancelled or SalesContractStatus.Returned) throw new ConflictException(Messages.ContractChargesLocked);
    }

    private async Task RefreshDocumentAsync(string documentType, Guid? documentId, CancellationToken ct)
    {
        if (documentType == ChargeDocumentTypes.SalesContract && documentId.HasValue) await SalesCharges.RefreshContractAsync(_db, documentId.Value, ct);
    }

    /// <summary>يتحقق من الطلب ويحسب أرقامه وحصص السيارات. قراءة فقط: لا يغيّر شيئاً في قاعدة البيانات.</summary>
    private async Task<Resolved> ResolveAsync(CreateAdditionalChargeLineDto d, AdditionalChargeLine? existing, CancellationToken ct)
    {
        var type = await _db.Set<ExpenseType>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == d.ExpenseTypeId, ct);
        // نوع معطَّل يبقى صالحاً لسطر قائم عليه، ولا يُختار لسطر جديد
        if (type == null || (!type.IsActive && existing?.ExpenseTypeId != type.Id)) throw new ValidationFailedException(Messages.ExpenseTypeNotFoundOrInactive);

        var errors = new List<string>();
        if (d.Amount <= 0) errors.Add(Messages.ChargeAmountMustBePositive);
        if (d.VatAmount < 0) errors.Add(Messages.ChargeVatCannotBeNegative);
        if (d.VatAmount > 0 && (type.IsInputTax || type.VatCategory != VatCategory.Standard)) errors.Add(Messages.ChargeVatNotAllowedForType);
        if (d.Status is not (ChargeStatus.Estimated or ChargeStatus.Actual)) errors.Add(Messages.ChargeStatusInvalid);
        if (d.Treatment.HasValue && !Enum.IsDefined(d.Treatment.Value)) errors.Add(Messages.ExpenseTypeValuesInvalid);
        if (d.AllocationMethod.HasValue && !Enum.IsDefined(d.AllocationMethod.Value)) errors.Add(Messages.ExpenseTypeValuesInvalid);
        if (d.Allocations.Any(a => a.Amount < 0)) errors.Add(Messages.ChargeAmountMustBePositive);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors.Distinct().ToList());

        // المعالجة تُورث من النوع؛ اختيار غيرها يتطلب صلاحية مستقلة
        // والرسم البيعي يتبع افتراضي إعدادات المعرض، ويُختار له «على العميل» أو «على المعرض» لكل سطر بلا صلاحية خاصة
        var defaultTreatment = SalesCharges.DefaultTreatment(type, await SalesCharges.SettingsAsync(_db, ct));
        var treatment = d.Treatment ?? (existing != null && existing.ExpenseTypeId == type.Id ? existing.Treatment : defaultTreatment);
        var alreadyOverridden = existing != null && existing.ExpenseTypeId == type.Id && existing.Treatment == treatment;
        if (d.SalePrice.HasValue && (d.SalePrice < 0 || treatment != ChargeTreatment.RechargeToCustomer))
            throw new ValidationFailedException(Messages.ChargeSalePriceInvalid);
        if (treatment != defaultTreatment && !alreadyOverridden && !SalesCharges.IsSaleFeeChoice(type, treatment)
            && !await _permissions.HasPermissionAsync(PermissionService.ChargeTreatmentOverrideScreenId, ScreenAction.Edit, ct))
            throw new ForbiddenException(Messages.ChargeTreatmentOverrideForbidden);

        var (currency, rate) = await ChargeShares.ResolveCurrencyAsync(_db, d.CurrencyCode, d.ExchangeRate, ct);
        var baseAmount = DocumentPricing.Round(d.Amount * rate);
        var costAmount = ChargeShares.CostAmount(type, baseAmount, DocumentPricing.Round(d.VatAmount * rate));

        if (!string.IsNullOrWhiteSpace(d.PaymentAccountCode) && !await _db.Set<Account>().AnyAsync(a => a.Code == d.PaymentAccountCode.Trim(), ct))
            throw new ValidationFailedException(Messages.ChargePaymentAccountInvalid);
        Supplier? supplier = null;
        if (d.SupplierId.HasValue)
            supplier = await _db.Set<Supplier>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == d.SupplierId, ct)
                ?? throw new ValidationFailedException(Messages.SupplierNotFound);

        var (documentType, documentId, documentNumber, documentVehicles) = await ResolveDocumentAsync(d, ct);
        var method = d.AllocationMethod ?? type.DefaultAllocationMethod;
        var shares = await AllocateAsync(d, documentType, documentVehicles, method, costAmount, ct);
        if (shares.Count == 0 && EntersCost(d.Status, treatment)) throw new ValidationFailedException(Messages.ChargeNeedsVehicle);

        return new Resolved(d, type, documentType, documentId, documentNumber, currency, rate, baseAmount, costAmount, treatment, method, supplier, shares);
    }

    /// <summary>المستند ورقمه والسيارات التي يصح توزيع السطر عليها (null = غير مقيَّدة بالمستند).</summary>
    private async Task<(string Type, Guid? Id, string? Number, List<Guid>? Vehicles)> ResolveDocumentAsync(CreateAdditionalChargeLineDto d, CancellationToken ct)
    {
        var type = (d.DocumentType ?? string.Empty).Trim().ToLowerInvariant();
        if (!ChargeDocumentTypes.All.Contains(type)) throw new ValidationFailedException(Messages.ChargeDocumentTypeInvalid + string.Join(" | ", ChargeDocumentTypes.All));
        if (!d.DocumentId.HasValue) throw new ValidationFailedException(Messages.ChargeDocumentNotFound);
        var id = d.DocumentId.Value;

        switch (type)
        {
            case ChargeDocumentTypes.ProcurementOrder:
            {
                var number = await _db.Set<CarProcurementOrder>().AsNoTracking().Where(o => o.Id == id).Select(o => o.OrderNumber).FirstOrDefaultAsync(ct)
                    ?? throw new ValidationFailedException(Messages.ChargeDocumentNotFound);
                return (type, id, number, await _db.Set<Vehicle>().AsNoTracking().Where(v => v.ProcurementOrderId == id).Select(v => v.Id).ToListAsync(ct));
            }
            case ChargeDocumentTypes.SalesContract:
            {
                var contract = await _db.Set<CarSalesContract>().AsNoTracking().Where(c => c.Id == id).Select(c => new { c.ContractNumber, c.VehicleId }).FirstOrDefaultAsync(ct)
                    ?? throw new ValidationFailedException(Messages.ChargeDocumentNotFound);
                return (type, id, contract.ContractNumber, new List<Guid> { contract.VehicleId });
            }
            case ChargeDocumentTypes.Invoice:
            {
                var number = await _db.Set<Invoice>().AsNoTracking().Where(i => i.Id == id).Select(i => i.InvoiceNumber).FirstOrDefaultAsync(ct)
                    ?? throw new ValidationFailedException(Messages.ChargeDocumentNotFound);
                return (type, id, number, await _db.Set<InvoiceVehicleLine>().AsNoTracking().Where(l => l.InvoiceId == id && l.VehicleId != null)
                    .Select(l => l.VehicleId!.Value).Distinct().ToListAsync(ct));
            }
            default:
            {
                var vin = await _db.Set<Vehicle>().AsNoTracking().Where(v => v.Id == id).Select(v => v.ChassisNumber).FirstOrDefaultAsync(ct)
                    ?? throw new ValidationFailedException(Messages.VehicleNotFound);
                return (type, id, vin, new List<Guid> { id });
            }
        }
    }

    private async Task<List<(Guid VehicleId, decimal Amount)>> AllocateAsync(CreateAdditionalChargeLineDto d, string documentType, List<Guid>? documentVehicles,
        ChargeAllocationMethod method, decimal costAmount, CancellationToken ct)
    {
        var requested = d.Allocations.Select(a => a.VehicleId).ToList();
        if (requested.Distinct().Count() != requested.Count) throw new ValidationFailedException(Messages.ChargeVehicleRepeated);
        // مستند بسيارة واحدة (عقد بيع، أو السيارة نفسها): السطر عليها دون حاجة لتحديدها
        if (requested.Count == 0 && documentVehicles is { Count: 1 } && documentType is ChargeDocumentTypes.SalesContract or ChargeDocumentTypes.Vehicle)
            requested = documentVehicles.ToList();
        if (requested.Count == 0) return new();
        if (documentVehicles != null && requested.Except(documentVehicles).Any()) throw new ValidationFailedException(Messages.ChargeVehicleNotInDocument);

        var vehicles = await ChargeShares.LoadAsync(_db, requested, ct);
        var amounts = ChargeShares.Compute(method, costAmount, requested.Select(id => vehicles[id]).ToList(), d.Allocations.Select(a => a.Amount).ToList());
        return requested.Select((id, i) => (id, amounts[i])).ToList();
    }

    private async Task SaveAsync(AdditionalChargeLine line, Resolved r, bool isNew, CancellationToken ct)
    {
        var before = line.Allocations.Select(a => a.VehicleId).ToList();
        var after = r.Shares.Select(s => s.VehicleId).ToList();
        var affected = before.Union(after).ToList();
        // تكلفة سيارة مباعة لا تتغيّر إلا بقيد تعديل موثق: صلاحية مستقلة وسبب، وأثرها على تكلفة المبيعات
        var costBefore = isNew ? new Dictionary<Guid, decimal>() : CostShares(line);
        var costAfter = EntersCost(r.Dto.Status, r.Treatment) ? r.Shares.ToDictionary(s => s.VehicleId, s => s.Amount) : new Dictionary<Guid, decimal>();
        var touched = costBefore.Keys.Union(costAfter.Keys).ToList();
        var sold = touched.Count > 0
            ? await SoldVehicleAdjustments.EnsureAllowedAsync(_db, _permissions, touched, r.Dto.AdjustmentReason, system: false, ct) : new();
        if (!isNew) await EnsureContractOpenAsync(line.DocumentType, line.DocumentId, line.Treatment == ChargeTreatment.RechargeToCustomer, ct);
        await EnsureContractOpenAsync(r.DocumentType, r.DocumentId, r.Treatment == ChargeTreatment.RechargeToCustomer, ct);
        var previousDocument = (line.DocumentType, line.DocumentId);

        // قبل أي تغيير: حقول التكلفة الحالية تُغطّى بأسطر افتتاحية، وإلا أسقطها الاشتقاق من الأسطر
        await VehicleCostLedger.EnsureOpeningAsync(_db, affected, ct);

        // سطر مرحَّل يُعدَّل: يُعكس قيده ثم يُرحَّل من جديد بأرقامه الجديدة
        if (!isNew) await ChargePosting.UnpostAsync(_db, _posting, line, "تعديل سطر المصروف", ct);

        var d = r.Dto;
        // التقديري نفسه يتحقق بمبلغ مختلف: تُحفظ قيمته التقديرية بجانب الفعلية
        if (!isNew && line.Status == ChargeStatus.Estimated && d.Status == ChargeStatus.Actual) line.EstimatedCostAmount = line.CostAmount;
        else if (d.Status == ChargeStatus.Estimated) line.EstimatedCostAmount = null;
        line.DocumentType = r.DocumentType; line.DocumentId = r.DocumentId; line.DocumentNumber = r.DocumentNumber;
        line.ExpenseTypeId = r.Type.Id; line.ExpenseTypeCode = r.Type.Code; line.ExpenseTypeNameAr = r.Type.NameAr; line.Stage = r.Type.Stage;
        line.Date = d.Date ?? (isNew ? DateTime.UtcNow : line.Date);
        line.Description = string.IsNullOrWhiteSpace(d.Description) ? null : d.Description.Trim();
        line.Amount = d.Amount; line.CurrencyCode = r.CurrencyCode; line.ExchangeRate = r.ExchangeRate; line.BaseAmount = r.BaseAmount;
        line.VatCategory = r.Type.VatCategory; line.VatAmount = d.VatAmount;
        line.IsVatRecoverable = r.Type.IsVatRecoverable; line.IsInputTax = r.Type.IsInputTax; line.CostAmount = r.CostAmount;
        line.Treatment = r.Treatment; line.Status = d.Status; line.AllocationMethod = r.Method;
        line.SalePrice = r.Treatment == ChargeTreatment.RechargeToCustomer ? d.SalePrice : null;
        line.SupplierId = r.Supplier?.Id; line.SupplierName = r.Supplier?.NameAr;
        line.PaymentAccountCode = string.IsNullOrWhiteSpace(d.PaymentAccountCode) ? null : d.PaymentAccountCode.Trim();
        line.SupplierInvoiceNumber = string.IsNullOrWhiteSpace(d.SupplierInvoiceNumber) ? null : d.SupplierInvoiceNumber.Trim();
        line.Notes = string.IsNullOrWhiteSpace(d.Notes) ? null : d.Notes.Trim();

        if (isNew) _db.Add(line);
        else
        {
            _db.RemoveRange(line.Allocations);
            line.Allocations.Clear();
        }
        foreach (var (vehicleId, amount) in r.Shares)
        {
            var allocation = new AdditionalChargeAllocation { VehicleId = vehicleId, Amount = amount };
            line.Allocations.Add(allocation);
            if (!isNew) _db.Add(allocation);
        }
        await _db.SaveChangesAsync(ct);
        await RecomputeAsync(affected, ct);
        await ChargePosting.PostAsync(_db, _posting, line, ct); // القيد للسطر الفعلي فقط؛ التقديري بلا أثر محاسبي
        await SoldVehicleAdjustments.RecordAsync(_db, _numbers, _user.Name, SoldVehicleAdjustments.Deltas(costBefore, costAfter, sold),
            d.AdjustmentReason ?? string.Empty, "charge_line", line.Id, line.JournalEntryId, ct);
        await RefreshDocumentAsync(line.DocumentType, line.DocumentId, ct);
        if (!isNew && previousDocument != (line.DocumentType, line.DocumentId)) await RefreshDocumentAsync(previousDocument.DocumentType, previousDocument.DocumentId, ct);
    }

    private async Task<List<AdditionalChargeLineDto>> ToDtosAsync(List<AdditionalChargeLine> lines, CancellationToken ct)
    {
        var vehicleIds = lines.SelectMany(l => l.Allocations).Select(a => a.VehicleId).Distinct().ToList();
        var vins = await _db.Set<Vehicle>().AsNoTracking().Where(v => vehicleIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.ChassisNumber, ct);
        var dtos = lines.Select(Mapper.Map<AdditionalChargeLineDto>).ToList();
        foreach (var a in dtos.SelectMany(l => l.Allocations)) a.ChassisNumber = vins.GetValueOrDefault(a.VehicleId);
        return dtos;
    }
}
