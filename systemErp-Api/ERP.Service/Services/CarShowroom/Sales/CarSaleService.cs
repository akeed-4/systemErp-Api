using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using ERP.Service.Data;
using ERP.Service.Services.Accounting;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

/// <summary>
/// دورة عقد بيع السيارة (5 مراحل). المنطق التشغيلي (حجز المركبة، التخصيص، التسليم، حساب ضريبة ZATCA بنمط
/// هامش الربح) هنا؛ والفوترة والقيد تمرّان عبر محرك الفوترة المركزي بحسابات إيراد/تكلفة/مخزون السيارات.
/// </summary>
public class CarSaleService : ICarSaleService
{
    private static readonly string[] PaymentMethods = { "cash", "credit", "bank_transfer", "bank_finance", "pos_mada" };

    private readonly ErpDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly IInvoiceService _invoices;
    private readonly ICurrentUser _user;
    private readonly ITransactionRunner _tx;
    private readonly IAuditService _audit;
    private readonly ICostVisibility _cost;
    private readonly IAccountingPostingService _posting;
    /// <summary>يُحمَّل في بداية كل عملية عامة؛ الافتراضي الحجب.</summary>
    private bool _canViewCost;

    public CarSaleService(ErpDbContext db, INumberSequenceService numbers, IInvoiceService invoices, ICurrentUser user, ITransactionRunner tx, IAuditService audit,
        ICostVisibility cost, IAccountingPostingService posting)
    {
        _db = db; _numbers = numbers; _invoices = invoices; _user = user; _tx = tx; _audit = audit; _cost = cost; _posting = posting;
    }

    private async Task LoadCostVisibilityAsync(CancellationToken ct) => _canViewCost = await _cost.CanViewAsync(ct);

    /// <summary>التكلفة والربح لمن يملك صلاحية car-cost-view فقط. في هامش الربح الضريبة والصافي يكشفان الهامش فيُحجبان معه.</summary>
    private CarSalesContractDto ToDto(CarSalesContract contract)
    {
        var dto = Mapper.Map<CarSalesContractDto>(contract);
        dto.PaymentSplits = UnpackSplits(contract.PaymentSplitsJson);
        if (_canViewCost) return dto;
        dto.CostPrice = null; dto.AdditionalCosts = null; dto.ProfitMargin = null; dto.ProfitMarginVat = null;
        if (CarVat.IsMarginScheme(contract.VatMode)) { dto.VatAmount = null; dto.NetPriceBeforeVat = null; }
        return dto;
    }

    public async Task<PagedResult<CarSalesContractDto>> ListAsync(PaginationParams p, CarSalesCycleType? cycleType = null, CancellationToken ct = default)
    {
        await LoadCostVisibilityAsync(ct);
        var q = _db.Set<CarSalesContract>().AsNoTracking().AsQueryable();
        if (cycleType.HasValue) q = q.Where(c => c.CycleType == cycleType);
        if (Enum.TryParse<SalesContractStatus>(p.Status, true, out var st)) q = q.Where(c => c.Status == st);
        if (p.StartDate.HasValue) q = q.Where(c => c.Date >= p.StartDate);
        if (p.EndDate.HasValue) q = q.Where(c => c.Date <= p.EndDate);
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(c => c.ContractNumber.Contains(t) || c.BuyerName.Contains(t) || c.Vin.Contains(t) || c.BuyerNationalIdOrCr.Contains(t));
        }
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(c => c.Date).ThenByDescending(c => c.ContractNumber)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);
        return new PagedResult<CarSalesContractDto>
        {
            Items = items.Select(ToDto).ToList(),
            TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    /// <summary>القائمة بخيارات DevExtreme: الفلترة والفرز والترقيم في SQL.</summary>
    public async Task<DevExtreme.AspNet.Data.ResponseModel.LoadResult> LoadAsync(DataSourceLoadOptions options, CarSalesCycleType? cycleType = null, CancellationToken ct = default)
    {
        await LoadCostVisibilityAsync(ct);
        var q = _db.Set<CarSalesContract>().AsNoTracking().AsQueryable();
        if (cycleType.HasValue) q = q.Where(c => c.CycleType == cycleType);
        return await EntityLoader.LoadAsync(q, options, ToDto, ct,
            EntityLoader.Desc(nameof(CarSalesContract.Date)), EntityLoader.Desc(nameof(CarSalesContract.ContractNumber)));
    }

    public async Task<CarSalesContractDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        await LoadCostVisibilityAsync(ct);
        return ToDto(await _db.Set<CarSalesContract>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException(Messages.SalesContractNotFound));
    }

    public Task<List<CarStageDefinitionDto>> GetStagesAsync(CancellationToken ct = default)
        => CarStageCatalog.ListAsync(_db, CarStageDefinition.SalesContract, ct);

    public Task<CarSalesContractDto> CreateAsync(CreateCarSalesContractDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            await LoadCostVisibilityAsync(token);
            var contract = Mapper.Map<CarSalesContract>(r);
            contract.PaymentSplitsJson = PackSplits(r.PaymentSplits);
            ResetSystemFields(contract);
            var vehicle = await LoadVehicleAsync(r.VehicleId, null, token, r.DepositVoucherId, r.CustomerId, r.BuyerName);
            await ValidateAsync(contract, token);
            ApplyVehicleAndPricing(contract, vehicle, await CarTax.LoadAsync(_db, token));
            await ValidateDepositAsync(contract, token);

            contract.ContractNumber = await _numbers.NextAsync("car_sales_contract", "CSC-", token);
            contract.Date = r.Date == default ? SaudiTime.Today : r.Date;
            contract.Status = SalesContractStatus.Draft;
            contract.SalespersonName ??= _user.Name;
            contract.CustomerChargesNet = 0; contract.CustomerChargesVat = 0;
            _db.Add(contract);
            await _db.SaveChangesAsync(token);
            await SalesCharges.SyncContractFieldsAsync(_db, contract, token);
            return ToDto(contract);
        }, ct);

    public Task<CarSalesContractDto> UpdateAsync(Guid id, UpdateCarSalesContractDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            await LoadCostVisibilityAsync(token);
            var contract = await _db.Set<CarSalesContract>().FirstOrDefaultAsync(c => c.Id == id, token) ?? throw new NotFoundException(Messages.SalesContractNotFound);
            if (contract.Status is SalesContractStatus.Invoiced or SalesContractStatus.Cancelled or SalesContractStatus.Delivered or SalesContractStatus.Returned)
                throw new ConflictException(Messages.CannotEditDeliveredInvoicedCancelledContract);
            if (r.VehicleId != contract.VehicleId && contract.Status != SalesContractStatus.Draft)
                throw new ConflictException(Messages.VehicleChangeViaAllocation);

            var keep = _db.Entry(contract).CurrentValues.Clone();
            var previousVehicle = contract.VehicleId;
            Mapper.Apply(r, contract);
            RestoreSystemFields(contract, keep);
            contract.PaymentSplitsJson = PackSplits(r.PaymentSplits);
            var vehicle = await LoadVehicleAsync(contract.VehicleId, contract.Id, token, contract.DepositVoucherId, contract.CustomerId, contract.BuyerName);
            await ValidateAsync(contract, token);
            ApplyVehicleAndPricing(contract, vehicle, await CarTax.LoadAsync(_db, token));
            await ValidateDepositAsync(contract, token);
            await _db.SaveChangesAsync(token);
            if (contract.VehicleId != previousVehicle) await SalesCharges.MoveEstimatesAsync(_db, contract.Id, contract.VehicleId, token);
            await SalesCharges.SyncContractFieldsAsync(_db, contract, token);
            return ToDto(contract);
        }, ct);

    public async Task<CarSalesContractDto> AdvanceStatusAsync(Guid id, AdvanceSalesContractRequestDto r, CancellationToken ct = default)
        => await SendToZatcaAsync(await _tx.RunAsync(async token =>
        {
            await LoadCostVisibilityAsync(token);
            var c = await _db.Set<CarSalesContract>().FirstOrDefaultAsync(x => x.Id == id, token) ?? throw new NotFoundException(Messages.SalesContractNotFound);
            if (c.Status is SalesContractStatus.Invoiced or SalesContractStatus.Cancelled or SalesContractStatus.Returned)
                throw new ConflictException(Messages.ContractFinished);
            if (r.TargetStatus == SalesContractStatus.Cancelled) throw new ValidationFailedException(Messages.UseCancelEndpoint);
            if (r.TargetStatus != c.Status + 1)
                throw new ConflictException(string.Format(Messages.NextAllowedStage, c.Status + 1));

            switch (r.TargetStatus)
            {
                case SalesContractStatus.Approved: await ApproveAsync(c, token); break;
                case SalesContractStatus.Allocated: await AllocateAsync(c, r, token); break;
                case SalesContractStatus.Delivered: Deliver(c, r); break;
                case SalesContractStatus.Invoiced: await InvoiceAsync(c, token); break;
            }
            if (!string.IsNullOrWhiteSpace(r.Notes)) c.Notes = string.IsNullOrWhiteSpace(c.Notes) ? r.Notes : $"{c.Notes}\n{r.Notes}";
            var from = c.Status;
            c.Status = r.TargetStatus;
            await _audit.LogAsync("STATUS_ADVANCED", nameof(CarSalesContract), c.Id.ToString(), $"من {from} إلى {c.Status}", token);
            await _db.SaveChangesAsync(token);
            return ToDto(c);
        }, ct), ct);

    public async Task<CarSalesContractDto> CompleteAsync(Guid id, CompleteSalesContractRequestDto? h, CancellationToken ct = default)
        => await SendToZatcaAsync(await _tx.RunAsync(async token =>
        {
            await LoadCostVisibilityAsync(token);
            var current = await _db.Set<CarSalesContract>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, token) ?? throw new NotFoundException(Messages.SalesContractNotFound);
            if (current.Status is SalesContractStatus.Invoiced or SalesContractStatus.Cancelled or SalesContractStatus.Returned)
                throw new ConflictException(Messages.ContractFinished);

            var status = current.Status;
            CarSalesContractDto last = ToDto(current);
            while (status != SalesContractStatus.Invoiced)
            {
                var next = status + 1;
                var step = new AdvanceSalesContractRequestDto { TargetStatus = next };
                if (next == SalesContractStatus.Allocated) { step.PdiInspectionNotes = h?.PdiInspectionNotes; }
                if (next == SalesContractStatus.Delivered)
                {
                    step.HandoverProtocolNumber = string.IsNullOrWhiteSpace(h?.HandoverProtocolNumber) ? $"HO-{current.ContractNumber}" : h!.HandoverProtocolNumber;
                    step.HandoverSignee = string.IsNullOrWhiteSpace(h?.HandoverSignee) ? current.BuyerName : h!.HandoverSignee;
                    step.HandoverSigneeNationalId = string.IsNullOrWhiteSpace(h?.HandoverSigneeNationalId) ? current.BuyerNationalIdOrCr : h!.HandoverSigneeNationalId;
                    step.DeliveryDate = h?.DeliveryDate; step.DeliveryLocation = h?.DeliveryLocation;
                }
                if (next == SalesContractStatus.Invoiced) step.Notes = h?.Notes;
                last = await AdvanceStatusAsync(id, step, token);
                status = last.Status;
            }
            return last;
        }, ct), ct);

    public async Task<CarSalesContractDto> QuickSaleAsync(QuickSaleRequestDto r, CancellationToken ct = default)
        => await SendToZatcaAsync(await _tx.RunAsync(async token =>
        {
            var created = await CreateAsync(r.Contract, token);
            var done = await CompleteAsync(created.Id, r.Handover, token);
            await _audit.LogAsync("QUICK_SALE", nameof(CarSalesContract), created.Id.ToString(), $"بيع سريع: عقد {done.ContractNumber} — VIN {done.Vin}", token);
            return done;
        }, ct), ct);

    public Task<CarSalesContractDto> CancelAsync(Guid id, string? reason, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            await LoadCostVisibilityAsync(token);
            var c = await _db.Set<CarSalesContract>().FirstOrDefaultAsync(x => x.Id == id, token) ?? throw new NotFoundException(Messages.SalesContractNotFound);
            if (c.Status is SalesContractStatus.Invoiced or SalesContractStatus.Returned) throw new ConflictException(Messages.CannotCancelInvoicedContract);
            if (c.Status == SalesContractStatus.Cancelled) throw new ConflictException(Messages.ContractAlreadyCancelled);
            var vehicle = await _db.Set<Vehicle>().FirstOrDefaultAsync(v => v.Id == c.VehicleId, token);
            // يبقى محجوزاً إن كان عليه عربون مفتوح (العربون يحجزه حتى يُحذف أو يُخصم من عقد)
            var heldByDeposit = await _db.Set<Voucher>().AnyAsync(v => v.DepositVehicleId == c.VehicleId && v.DepositStatus == "open", token);
            // أو حجز يدوي ما زال فعّالاً
            var stillReserved = await VehicleReservations.UnholdAsync(_db, c.VehicleId, c.Id, null, "إلغاء عقد البيع", token);
            if (vehicle is { Status: VehicleStatus.Reserved } && !heldByDeposit && !stillReserved) vehicle.Status = VehicleStatus.Available; // فك الحجز
            await SalesCharges.CancelEstimatesAsync(_db, c.Id, token);
            c.Status = SalesContractStatus.Cancelled;
            if (!string.IsNullOrWhiteSpace(reason)) c.Notes = string.IsNullOrWhiteSpace(c.Notes) ? $"سبب الإلغاء: {reason}" : $"{c.Notes}\nسبب الإلغاء: {reason}";
            await _audit.LogAsync("CONTRACT_CANCELLED", nameof(CarSalesContract), c.Id.ToString(), reason ?? "بدون سبب", token);
            await _db.SaveChangesAsync(token);
            return ToDto(c);
        }, ct);

    /// <summary>
    /// مرتجع البيع: إشعار دائن يعكس فاتورة العقد، والسيارة ترجع للمخزون بتكلفتها الأصلية (تكلفة المبيعات وقت البيع).
    /// مصاريف وصلت بعد البيع (حُمّلت على تكلفة المبيعات) تبقى مصروفاً ولا تعود للمخزون. الرسوم المحمّلة على العميل تُرد له أو لا حسب
    /// إعداد المعرض (أو اختيار المرتجع)؛ وما رُدّ منها وكان المعرض دفعه فعلاً (رسوم تم غير مستردة) يتحول لمصروف على المعرض.
    /// </summary>
    public async Task<CarSalesContractDto> ReturnAsync(Guid id, ReturnCarSaleDto r, CancellationToken ct = default)
        => await SendToZatcaAsync(await _tx.RunAsync(async token =>
        {
            await LoadCostVisibilityAsync(token);
            var c = await _db.Set<CarSalesContract>().FirstOrDefaultAsync(x => x.Id == id, token) ?? throw new NotFoundException(Messages.SalesContractNotFound);
            if (c.Status != SalesContractStatus.Invoiced || c.InvoiceId == null) throw new ConflictException(Messages.ContractNotInvoicedForReturn);
            if (string.IsNullOrWhiteSpace(r.Reason)) throw new ValidationFailedException(Messages.ReturnReasonRequired);
            var original = await _db.Set<Invoice>().AsNoTracking().Include(i => i.Items).FirstAsync(i => i.Id == c.InvoiceId, token);
            var settings = await SalesCharges.SettingsAsync(_db, token);
            await ChargePosting.EnsureAccountsAsync(_db, settings, token);
            var refundCharges = r.RefundCustomerCharges ?? settings.RefundCustomerChargesOnReturn;
            if (r.RefundPaymentMethod.HasValue)
                r.RefundPaymentMethod = await PaymentMethodRules.MethodForAsync(_db, r.RefundPaymentMethod.Value, r.RefundPaymentMethodCode, token);

            // بند السيارة يحمل ضريبتها المحسوبة بنمطها؛ الباقي رسوم وإضافات على العميل
            var vehicleItem = original.Items.FirstOrDefault(i => i.VatAmountOverride.HasValue) ?? original.Items.OrderByDescending(i => i.UnitCost).First();
            var returned = refundCharges ? original.Items.OrderByDescending(i => i.Id == vehicleItem.Id).ToList() : new List<InvoiceItem> { vehicleItem };
            var dto = new CreateInvoiceDto
            {
                Kind = InvoiceKind.SalesReturn, InvoiceType = InvoiceType.CreditNote, IsReturn = true,
                OriginalInvoiceId = original.Id, OriginalInvoiceNumber = original.InvoiceNumber, ReturnReason = r.Reason.Trim(),
                PartyId = original.PartyId, PartyName = original.PartyName, PartyVatNumber = original.PartyVatNumber, PartyCrNumber = original.PartyCrNumber,
                PartyIdType = original.PartyIdType, PartyIdNumber = original.PartyIdNumber, PartyAddress = original.PartyAddress, PartyPhone = original.PartyPhone, PartyEmail = original.PartyEmail,
                PartyStreet = original.PartyStreet, PartyBuildingNo = original.PartyBuildingNo, PartyDistrict = original.PartyDistrict,
                PartyCity = original.PartyCity, PartyPostalCode = original.PartyPostalCode, PartyAdditionalNo = original.PartyAdditionalNo, PartyCountry = original.PartyCountry,
                PaymentMethod = r.RefundPaymentMethod ?? (original.PaymentMethod == PaymentMethod.Credit ? PaymentMethod.Credit : PaymentMethod.Cash),
                PaymentMethodCode = r.RefundPaymentMethod == null ? null : r.RefundPaymentMethodCode,
                CurrencyCode = original.CurrencyCode, ExchangeRate = original.ExchangeRate, Status = "posted",
                SendToZatca = TradeInvoiceEngine.ReturnSendsToZatca(original),
                Notes = $"مرتجع بيع سيارة - عقد {c.ContractNumber} - VIN {c.Vin} - {r.Reason.Trim()}",
                ReferenceType = "car_sales_contract", ReferenceId = c.Id, ReferenceNumber = c.ContractNumber,
                RevenueAccountCode = original.RevenueAccountCode, CogsAccountCode = original.CogsAccountCode, InventoryAccountCode = original.InventoryAccountCode,
                Items = returned.Select(i => new InvoiceItemDto
                {
                    ItemId = Guid.Empty, ItemName = i.ItemName, Sku = i.Sku, Unit = i.Unit, Quantity = i.Quantity, UnitPrice = i.UnitPrice, UnitCost = i.UnitCost,
                    Discount = i.Discount, VatRate = i.VatRate, VatAmountOverride = i.VatAmountOverride, VatCategory = i.VatCategory,
                    VatExemptionReasonCode = i.VatExemptionReasonCode, RevenueAccountCode = i.RevenueAccountCode, CostCenterId = i.CostCenterId,
                }).ToList(),
            };
            if (r.ReturnDate.HasValue) dto.IssueDate = r.ReturnDate.Value;
            if (r.RefundSplits.Count > 0) await TradeInvoiceEngine.ApplyRefundSplitsAsync(_db, dto, r.RefundSplits, token);
            var creditNote = await _invoices.CreateAsync(dto, token);

            var vehicle = await _db.Set<Vehicle>().FirstAsync(v => v.Id == c.VehicleId, token);
            vehicle.Status = VehicleStatus.Available;

            // مصاريف ما بعد البيع: بقيت على تكلفة المبيعات، فتخرج من تكلفة السيارة وتصير مصروفاً
            await SoldVehicleAdjustments.ExpenseLateChargesAsync(_db, new[] { vehicle.Id }, token);

            // رسوم على العميل رُدّت له وكان المعرض دفعها فعلاً نيابة عنه: لم يعد من يتحملها غير المعرض
            var contractLines = await SalesCharges.ContractLines(_db, c.Id)
                .Where(l => l.Status == ChargeStatus.Actual && l.Treatment == ChargeTreatment.RechargeToCustomer).ToListAsync(token);
            if (refundCharges && contractLines.Count > 0)
            {
                var typeIds = contractLines.Select(l => l.ExpenseTypeId).Distinct().ToList();
                var disbursements = await _db.Set<ExpenseType>().AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.IsDisbursement, token);
                var passThrough = contractLines.Where(l => l.JournalEntryId != null && (!l.SalePrice.HasValue || disbursements.GetValueOrDefault(l.ExpenseTypeId))).ToList();
                var unrecovered = passThrough.Sum(l => l.SalePrice ?? l.CostAmount);
                if (unrecovered > 0)
                    await _posting.PostAsync(new GenericPostingRequest
                    {
                        Date = dto.IssueDate == default ? SaudiTime.Today : dto.IssueDate,
                        Description = $"رسوم غير مستردة رُدّت للعميل عند مرتجع عقد {c.ContractNumber}: مصروف على المعرض",
                        SourceType = "car_sale_return_fees", SourceId = c.Id, SourceNumber = c.ContractNumber,
                        Lines = new List<PostingLine> { new(settings.VehicleExpenseAccount, unrecovered, 0), new(settings.CustomerRechargeAccount, 0, unrecovered) },
                    }, token);
                foreach (var line in passThrough) { line.Treatment = ChargeTreatment.Expense; line.SalePrice = null; }
            }
            await _db.SaveChangesAsync(token);
            await SalesCharges.CancelEstimatesAsync(_db, c.Id, token);
            await VehicleCostLedger.RecomputeAsync(_db, new[] { vehicle.Id }, token);

            c.Status = SalesContractStatus.Returned;
            c.Notes = string.IsNullOrWhiteSpace(c.Notes) ? $"سبب المرتجع: {r.Reason.Trim()}" : $"{c.Notes}\nسبب المرتجع: {r.Reason.Trim()}";
            await _audit.LogAsync("CONTRACT_RETURNED", nameof(CarSalesContract), c.Id.ToString(),
                $"مرتجع عقد {c.ContractNumber} بإشعار دائن {creditNote.InvoiceNumber}" + (refundCharges ? " مع رد الرسوم المحمّلة على العميل" : " دون رد الرسوم المحمّلة على العميل"), token);
            await _db.SaveChangesAsync(token);
            return ToDto(c);
        }, ct), ct);

    /// <summary>
    /// المحرك لا يرسل للهيئة فاتورة أُنشئت داخل معاملة العقد (قد تُلغى معه)، فتُرسل هنا بعد اكتمالها: فاتورة العقد وإشعاره الدائن
    /// المطلوب إرسالهما ولم يُرسلا، الأصل قبل المرتجع. الرفض أو تعذّر الاتصال يُحفظ على الفاتورة ولا يُفشل العقد (يُعاد من الشاشة).
    /// </summary>
    private async Task<CarSalesContractDto> SendToZatcaAsync(CarSalesContractDto contract, CancellationToken ct)
    {
        if (_db.Database.CurrentTransaction != null) return contract; // داخل معاملة أوسع (البيع السريع): تُرسل عند اكتمالها
        var pending = await ContractDocuments(contract.Id)
            .Where(i => i.SendToZatca && i.Status == "posted" && i.ZatcaStatus == ZatcaSubmissionStatus.NotSubmitted)
            .Select(i => i.Id).ToListAsync(ct);
        foreach (var invoiceId in pending) await _invoices.SubmitToZatcaIfRequestedAsync(invoiceId, ct);
        return contract;
    }

    /// <summary>فاتورة العقد وإشعاراته الدائنة بحالة الهيئة (لعرضها وإعادة إرسالها من تفاصيل العقد).</summary>
    public async Task<List<InvoiceDto>> GetZatcaDocumentsAsync(Guid id, CancellationToken ct = default)
    {
        var ids = await ContractDocuments(id).Select(i => i.Id).ToListAsync(ct);
        var documents = new List<InvoiceDto>();
        foreach (var invoiceId in ids) documents.Add(await _invoices.GetAsync(invoiceId, ct));
        return documents;
    }

    private IQueryable<Invoice> ContractDocuments(Guid contractId) => _db.Set<Invoice>().AsNoTracking()
        .Where(i => i.ReferenceType == "car_sales_contract" && i.ReferenceId == contractId && (i.Kind == InvoiceKind.Sales || i.Kind == InvoiceKind.SalesReturn))
        .OrderBy(i => i.IsReturn).ThenBy(i => i.InvoiceNumber);

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var c = await _db.Set<CarSalesContract>().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(Messages.SalesContractNotFound);
        if (c.Status is not (SalesContractStatus.Draft or SalesContractStatus.Cancelled)) throw new ConflictException(Messages.OnlyDraftOrCancelledContractDeleted);
        var charges = await SalesCharges.ContractLines(_db, c.Id).Include(l => l.Allocations).ToListAsync(ct);
        if (charges.Any(l => l.Status == ChargeStatus.Actual)) throw new ConflictException(Messages.ContractHasActualCharges);
        _db.RemoveRange(charges.SelectMany(l => l.Allocations));
        _db.RemoveRange(charges);
        _db.Remove(c);
        await _db.SaveChangesAsync(ct);
    }

    // ---------------- الانتقالات ----------------
    private async Task ApproveAsync(CarSalesContract c, CancellationToken ct)
    {
        var errors = new List<string>();
        if (c.CycleType == CarSalesCycleType.BankLease)
        {
            if (string.IsNullOrWhiteSpace(c.FinancingBankName)) errors.Add(Messages.BankLeaseRequiresBankName);
            if ((c.FinancedAmount ?? 0) <= 0) errors.Add(Messages.FinancingAmountRequired);
        }
        if (c.CycleType == CarSalesCycleType.Installment)
        {
            if ((c.MonthlyInstallment ?? 0) <= 0 || (c.FinanceTenorMonths ?? 0) <= 0) errors.Add(Messages.InstallmentDetailsRequired);
        }
        if ((c.PaymentMethod is "credit" or "bank_finance" || UnpackSplits(c.PaymentSplitsJson).Any(x => x.Method == PaymentMethod.Credit)) && c.CustomerId == null) errors.Add(Messages.CreditSaleRequiresCustomerLink);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        var vehicle = await LoadVehicleAsync(c.VehicleId, c.Id, ct, c.DepositVoucherId, c.CustomerId, c.BuyerName);
        vehicle.Status = VehicleStatus.Reserved; // حجز لهذا العقد
        await VehicleReservations.HoldAsync(_db, vehicle.Id, c.CustomerId, c.BuyerName, c.Id, null, ct);
    }

    private async Task AllocateAsync(CarSalesContract c, AdvanceSalesContractRequestDto r, CancellationToken ct)
    {
        var vehicle = await _db.Set<Vehicle>().FirstAsync(v => v.Id == c.VehicleId, ct);
        if (r.VehicleId.HasValue && r.VehicleId != c.VehicleId)
        {
            var replacement = await LoadVehicleAsync(r.VehicleId.Value, c.Id, ct, null, c.CustomerId, c.BuyerName);
            var oldStillReserved = await VehicleReservations.UnholdAsync(_db, vehicle.Id, c.Id, null, "تبديل سيارة العقد", ct);
            if (vehicle.Status == VehicleStatus.Reserved && !oldStillReserved) vehicle.Status = VehicleStatus.Available; // فك حجز المركبة القديمة
            replacement.Status = VehicleStatus.Reserved;
            c.VehicleId = replacement.Id;
            ApplyVehicleAndPricing(c, replacement, await CarTax.LoadAsync(_db, ct));
            await VehicleReservations.HoldAsync(_db, replacement.Id, c.CustomerId, c.BuyerName, c.Id, null, ct);
            await SalesCharges.MoveEstimatesAsync(_db, c.Id, replacement.Id, ct);
            vehicle = replacement;
        }
        if (vehicle.Status != VehicleStatus.Reserved) vehicle.Status = VehicleStatus.Reserved;

        c.AllocatedVin = vehicle.ChassisNumber; c.AllocatedAt = DateTime.UtcNow;
        c.PdiInspectionNotes = r.PdiInspectionNotes ?? c.PdiInspectionNotes;
        if (r.PdiChecklist != null)
        {
            var pdi = Mapper.Map<VehiclePdiChecklist>(r.PdiChecklist);
            pdi.InspectedAt = DateTime.UtcNow; pdi.InspectedBy = _user.Name;
            vehicle.PdiChecklist = pdi;
        }
    }

    private static void Deliver(CarSalesContract c, AdvanceSalesContractRequestDto r)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(r.HandoverProtocolNumber)) errors.Add(Messages.DeliveryReportNumberRequired);
        if (string.IsNullOrWhiteSpace(r.HandoverSignee)) errors.Add(Messages.RecipientNameRequired);
        if (string.IsNullOrWhiteSpace(r.HandoverSigneeNationalId)) errors.Add(Messages.RecipientIdRequired);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
        c.HandoverProtocolNumber = r.HandoverProtocolNumber; c.HandoverSignee = r.HandoverSignee;
        c.HandoverSigneeNationalId = r.HandoverSigneeNationalId;
        c.DeliveredAt = DateTime.UtcNow; c.DeliveryDate = r.DeliveryDate ?? SaudiTime.Today; c.DeliveryLocation = r.DeliveryLocation ?? c.DeliveryLocation;
    }

    private async Task InvoiceAsync(CarSalesContract c, CancellationToken ct)
    {
        if (c.InvoiceId != null) throw new ConflictException(Messages.ContractAlreadyInvoiced);
        var vehicle = await _db.Set<Vehicle>().FirstAsync(v => v.Id == c.VehicleId, ct);
        var customer = c.CustomerId.HasValue ? await _db.Set<Customer>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == c.CustomerId, ct) : null;

        // تكلفة المبيعات = تكلفة السيارة الكلية وقت الفوترة (بما أُضيف لها من مصاريف بعد إنشاء العقد)؛ سعر العميل لا يتغيّر
        var settings = await SalesCharges.SettingsAsync(_db, ct);
        await ChargePosting.EnsureAccountsAsync(_db, settings, ct);
        var tax = new CarTax(settings.StandardVatRate, settings.MarginBasis);
        c.CostPrice = vehicle.TotalCost;
        c.MarginBaseCost = tax.MarginBase(vehicle.PurchasePrice, vehicle.TotalCost);
        var pricing = tax.Calculate(vehicle.PurchasePrice, vehicle.TotalCost, c.SellingPrice - (c.DiscountAmount ?? 0), c.VatMode);
        // الهامش المعروض ربح العقد الفعلي (السعر − التكلفة الكلية)؛ أما الضريبة فعلى الهامش الخاضع حسب أساسه في الإعدادات
        c.NetPriceBeforeVat = pricing.NetBeforeVat; c.ProfitMargin = Math.Max(0, c.SellingPrice - (c.DiscountAmount ?? 0) - c.CostPrice); c.ProfitMarginVat = pricing.ProfitMarginVat;
        c.VatAmount = pricing.VatAmount; c.TotalWithVat = pricing.PriceWithVat;
        // الرسوم والإضافات المحمّلة على العميل: بنود مستقلة في فاتورته
        await SalesCharges.RefreshContractAsync(_db, c.Id, ct);
        var chargeItems = await SalesCharges.InvoiceItemsAsync(_db, c.Id, ct);
        var net = c.NetPriceBeforeVat ?? c.SellingPrice - (c.DiscountAmount ?? 0);

        // العربون يُخصم أولاً: جزء الدفع به يقفل حساب عربونات العملاء (216) والباقي بحسب خطة الدفع
        Voucher? deposit = c.DepositVoucherId.HasValue ? await _db.Set<Voucher>().FirstOrDefaultAsync(v => v.Id == c.DepositVoucherId, ct) : null;
        var depositAmount = deposit == null ? 0m : Math.Min(deposit.Amount, Total(c));
        var (method, splits) = PaymentPlan(c, depositAmount, deposit?.VoucherNumber);
        var invoice = await _invoices.CreateAsync(new CreateInvoiceDto
        {
            Kind = InvoiceKind.Sales,
            InvoiceType = customer?.IsVatRegistered == true ? InvoiceType.TaxInvoice : InvoiceType.Simplified, // من الحالة الضريبية للعميل (الافتراضي مبسطة)
            PartyId = c.CustomerId, PartyName = c.BuyerName, PartyVatNumber = customer?.VatNumber, PartyPhone = c.BuyerPhone, PartyEmail = c.BuyerEmail,
            PartyAddress = c.BuyerAddress, PaymentMethod = method, IsSplitPayment = splits.Count > 0, PaymentSplits = splits,
            PaymentMethodCode = splits.Count == 0 && method != PaymentMethod.Credit ? c.PaymentMethodCode : null,
            Status = "posted",
            Notes = $"فاتورة مبيعات سيارة - عقد {c.ContractNumber} - VIN {c.Vin}",
            ReferenceType = "car_sales_contract", ReferenceId = c.Id, ReferenceNumber = c.ContractNumber,
            RevenueAccountCode = settings.CarSalesRevenueAccount, CogsAccountCode = settings.CarCogsAccount, InventoryAccountCode = settings.VehicleInventoryAccount,
            Items = new List<InvoiceItemDto>
            {
                new()
                {
                    ItemId = Guid.Empty, ItemName = $"{c.VehicleDescription} (VIN: {c.Vin})", Unit = "سيارة", Quantity = 1,
                    UnitPrice = net, UnitCost = c.CostPrice,
                    VatRate = tax.RateFor(c.VatMode),
                    VatAmountOverride = c.VatAmount, // الضريبة المحسوبة بنمط الاحتساب (قياسي/هامش الربح/معفى)
                    VatCategory = CarVat.LineCategoryOf(c.VatMode),
                },
            }.Concat(chargeItems).ToList(),
        }, ct);

        c.InvoiceId = invoice.Id;
        vehicle.Status = VehicleStatus.Sold;
        await VehicleReservations.ConvertAsync(_db, vehicle.Id, ct);
        if (deposit != null)
        {
            deposit.DepositStatus = "applied"; deposit.DepositContractId = c.Id;
            c.DepositAppliedAmount = depositAmount;
        }
    }

    /// <summary>طريقة الدفع في الفاتورة: التمويل/الآجل مع دفعة مقدمة تُقسَّم إلى نقد + ذمم.</summary>
    /// <summary>ما يدفعه العميل: السيارة بضريبتها + الرسوم والإضافات المحمّلة عليه بضريبتها.</summary>
    private static decimal Total(CarSalesContract c) => c.TotalWithVat + c.CustomerChargesNet + c.CustomerChargesVat;

    private static string? PackSplits(List<InvoicePaymentSplitDto>? splits)
        => splits == null || splits.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(splits.Where(x => x.Amount > 0).Select(x => new { x.Method, x.PaymentMethodCode, x.Amount, x.Reference }));

    private static List<InvoicePaymentSplitDto> UnpackSplits(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return System.Text.Json.JsonSerializer.Deserialize<List<InvoicePaymentSplitDto>>(json) ?? new(); } catch { return new(); }
    }

    private static (PaymentMethod Method, List<InvoicePaymentSplitDto> Splits) PaymentPlan(CarSalesContract c, decimal deposit = 0, string? depositRef = null)
    {
        var total = Total(c);
        var chosen = UnpackSplits(c.PaymentSplitsJson);
        if (chosen.Count > 0)
        {
            // دفع متعدد: العربون أولاً ثم الطرق المختارة، ومجموعها يطابق المتبقي تماماً
            var parts = new List<InvoicePaymentSplitDto>();
            if (deposit > 0) parts.Add(new() { Method = PaymentMethod.CustomerDeposit, Amount = deposit, Reference = depositRef });
            parts.AddRange(chosen.Where(x => x.Amount > 0 && x.Method != PaymentMethod.CustomerDeposit));
            if (Math.Abs(parts.Sum(x => x.Amount) - total) > 0.005m)
                throw new ValidationFailedException("مجموع طرق الدفع لا يساوي إجمالي العقد بعد العربون؛ عدّل طرق الدفع ثم أعد الفوترة.");
            var main = parts.Any(x => x.Method == PaymentMethod.Credit) ? PaymentMethod.Credit : parts[0].Method;
            return (main, parts);
        }
        if (deposit > 0)
        {
            var parts = new List<InvoicePaymentSplitDto> { new() { Method = PaymentMethod.CustomerDeposit, Amount = deposit, Reference = depositRef } };
            var remaining = total - deposit;
            if (remaining <= 0) return (PaymentMethod.CustomerDeposit, parts);
            var downNow = Math.Min(c.DownPaymentAmount ?? 0, remaining);
            if (c.PaymentMethod is "credit" or "bank_finance")
            {
                if (downNow >= remaining) { parts.Add(new() { Method = PaymentMethod.Cash, Amount = remaining, Reference = c.DownPaymentReceiptNo }); return (PaymentMethod.Cash, parts); }
                if (downNow > 0) parts.Add(new() { Method = PaymentMethod.Cash, Amount = downNow, Reference = c.DownPaymentReceiptNo });
                parts.Add(new() { Method = PaymentMethod.Credit, Amount = remaining - downNow, Reference = c.BankApprovalNumber });
                return (PaymentMethod.Credit, parts);
            }
            var rest = c.PaymentMethod switch
            {
                "cash" => PaymentMethod.Cash, "bank_transfer" => PaymentMethod.BankTransfer, "pos_mada" => PaymentMethod.BankCard, _ => PaymentMethod.Credit,
            };
            parts.Add(new() { Method = rest, PaymentMethodCode = rest == PaymentMethod.Credit ? null : c.PaymentMethodCode, Amount = remaining });
            return (rest, parts);
        }
        var down = c.DownPaymentAmount ?? 0;
        if (c.PaymentMethod is "credit" or "bank_finance" && down > 0 && down < total)
            return (PaymentMethod.Credit, new List<InvoicePaymentSplitDto>
            {
                new() { Method = PaymentMethod.Cash, Amount = down, Reference = c.DownPaymentReceiptNo },
                new() { Method = PaymentMethod.Credit, Amount = total - down, Reference = c.BankApprovalNumber },
            });
        return (c.PaymentMethod switch
        {
            "cash" => PaymentMethod.Cash,
            "bank_transfer" => PaymentMethod.BankTransfer,
            "pos_mada" => PaymentMethod.BankCard,
            _ => PaymentMethod.Credit,
        }, new List<InvoicePaymentSplitDto>());
    }

    // ---------------- التحقق والتسعير ----------------
    private async Task<Vehicle> LoadVehicleAsync(Guid vehicleId, Guid? contractId, CancellationToken ct, Guid? depositVoucherId = null,
        Guid? customerId = null, string? buyerName = null)
    {
        await VehicleReservations.ExpireDueAsync(_db, ct); // حجز انتهت مدته بلا عربون ولا عقد لا يمنع البيع
        var vehicle = await _db.Set<Vehicle>().FirstOrDefaultAsync(v => v.Id == vehicleId, ct) ?? throw new ValidationFailedException(Messages.VehicleNotFound);
        if (vehicle.Status == VehicleStatus.Sold) throw new ConflictException(Messages.VehicleSold);
        if (vehicle.Status == VehicleStatus.WrittenOff) throw new ConflictException(Messages.VehicleWrittenOffNotSellable);
        if (vehicle.Status is VehicleStatus.InTransit or VehicleStatus.Cancelled or VehicleStatus.Draft) throw new ConflictException(string.Format(Messages.VehicleNotInStock, vehicle.ChassisNumber));
        if (vehicle.IsTemporaryChassis) throw new ConflictException(string.Format(Messages.VehicleChassisTemporary, vehicle.ChassisNumber));
        if (vehicle.Status == VehicleStatus.Reserved)
        {
            var reservedByThis = contractId.HasValue && await _db.Set<CarSalesContract>().AnyAsync(x => x.Id == contractId && x.VehicleId == vehicleId
                && x.Status != SalesContractStatus.Cancelled && x.Status != SalesContractStatus.Returned, ct);
            var reservedByOther = await _db.Set<CarSalesContract>().AnyAsync(x => x.VehicleId == vehicleId && x.Id != contractId
                && x.Status != SalesContractStatus.Cancelled && x.Status != SalesContractStatus.Draft && x.Status != SalesContractStatus.Returned, ct);
            var reservedByDeposit = depositVoucherId.HasValue && await _db.Set<Voucher>().AnyAsync(v => v.Id == depositVoucherId && v.DepositVehicleId == vehicleId, ct);
            // حجز قائم: يُباع لصاحبه فقط (العميل نفسه، أو الاسم نفسه لحجز بلا عميل معرَّف)
            var reservation = await VehicleReservations.ActiveAsync(_db, vehicleId, ct);
            var reservedForBuyer = reservation != null && (reservation.CustomerId.HasValue
                ? reservation.CustomerId == customerId
                : string.Equals(reservation.CustomerName.Trim(), buyerName?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (reservedByOther || (!reservedByThis && !contractId.HasValue && !reservedByDeposit && !reservedForBuyer)) throw new ConflictException(Messages.VehicleReservedForAnotherContract);
            if (reservation != null && !reservedForBuyer && !reservedByDeposit && reservation.ContractId != contractId)
                throw new ConflictException(string.Format(Messages.VehicleAlreadyReservedFor, reservation.CustomerName, reservation.ExpiresAt));
        }
        return vehicle;
    }

    /// <summary>العربون المختار: سند عربون مفتوح لنفس المركبة والعميل، غير مربوط بعقد آخر، ولا يتجاوز إجمالي العقد.</summary>
    private async Task ValidateDepositAsync(CarSalesContract c, CancellationToken ct)
    {
        if (!c.DepositVoucherId.HasValue) return;
        var voucher = await _db.Set<Voucher>().AsNoTracking().FirstOrDefaultAsync(v => v.Id == c.DepositVoucherId, ct)
            ?? throw new ValidationFailedException("سند العربون غير موجود.");
        if (voucher.DepositVehicleId != c.VehicleId) throw new ValidationFailedException("سند العربون لمركبة أخرى.");
        if (voucher.DepositStatus != "open" && voucher.DepositContractId != c.Id) throw new ConflictException("سند العربون مخصوم من عقد آخر.");
        if (voucher.DepositCustomerId.HasValue && c.CustomerId.HasValue && voucher.DepositCustomerId != c.CustomerId)
            throw new ValidationFailedException("سند العربون لعميل آخر.");
        if (await _db.Set<CarSalesContract>().AnyAsync(x => x.DepositVoucherId == c.DepositVoucherId && x.Id != c.Id && x.Status != SalesContractStatus.Cancelled, ct))
            throw new ConflictException("سند العربون مربوط بعقد آخر.");
        if (voucher.Amount > Total(c)) throw new ValidationFailedException("العربون يتجاوز إجمالي العقد.");
    }

    private async Task ValidateAsync(CarSalesContract c, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(c.BuyerName)) errors.Add(Messages.BuyerNameRequired);
        if (string.IsNullOrWhiteSpace(c.BuyerNationalIdOrCr)) errors.Add(Messages.BuyerIdRequired);
        if (string.IsNullOrWhiteSpace(c.BuyerPhone)) errors.Add(Messages.BuyerMobileRequired);
        if (!PaymentMethods.Contains(c.PaymentMethod)) errors.Add(Messages.PaymentMethodPrefix + string.Join(" | ", PaymentMethods));
        if (c.SellingPrice < 0 || (c.DiscountAmount ?? 0) < 0 || (c.DiscountAmount ?? 0) > c.SellingPrice) errors.Add(Messages.PriceAndDiscountInvalid);
        if (!string.IsNullOrWhiteSpace(c.BuyerEmail) && !c.BuyerEmail.Contains('@')) errors.Add(Messages.EmailInvalid);
        if ((c.DownPaymentAmount ?? 0) < 0) errors.Add(Messages.DownPaymentCannotBeNegative);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        // طرق الدفع المختارة تُعتمد (فعّالة)؛ ونوعها يحدّد نوع الدفع حين تُفوتر
        await PaymentMethodRules.RequireAsync(_db, c.PaymentMethodCode, ct);
        foreach (var split in UnpackSplits(c.PaymentSplitsJson))
            await PaymentMethodRules.RequireAsync(_db, split.PaymentMethodCode, ct);
        if (c.CustomerId.HasValue && !await _db.Set<Customer>().AnyAsync(x => x.Id == c.CustomerId, ct))
            throw new ValidationFailedException(Messages.CustomerNotFound);
        if (c.FinancingBankId.HasValue && !await _db.Set<BankEntity>().AnyAsync(x => x.Id == c.FinancingBankId, ct))
            throw new ValidationFailedException(Messages.FinancingBankNotFound);
    }

    /// <summary>ينسخ بيانات المركبة ويحسب التسعير والضريبة في السرفر (التكلفة من المركبة لا من العميل).</summary>
    private static void ApplyVehicleAndPricing(CarSalesContract c, Vehicle v, CarTax tax)
    {
        c.VehicleId = v.Id; c.Vin = v.ChassisNumber; c.EngineNumber = v.EngineNumber; c.CustomsCardNumber = v.CustomsCardNumber;
        c.VehicleDescription = $"{v.BrandNameAr} {v.ModelNameAr} {v.TrimNameAr} {v.Year}".Replace("  ", " ").Trim();
        c.BrandNameAr = v.BrandNameAr; c.ModelNameAr = v.ModelNameAr; c.Year = v.Year;
        c.ColorExterior = v.ColorExterior; c.ColorInterior = v.ColorInterior; c.Condition = v.Condition;
        c.Mileage = v.MileageKm; c.FuelType = v.FuelType.ToString().ToLowerInvariant();
        c.Transmission = v.Transmission.ToString().ToLowerInvariant(); c.Location = v.Location;

        if (c.SellingPrice <= 0) c.SellingPrice = v.SellingPrice;
        var net = c.SellingPrice - (c.DiscountAmount ?? 0);
        if (v.MinSellingPrice.HasValue && net < v.MinSellingPrice)
            throw new ValidationFailedException(string.Format(Messages.SellingPriceBelowMinimum, net, v.MinSellingPrice));

        c.CostPrice = v.TotalCost;
        c.VatMode = v.VatMode; // نمط الضريبة من بطاقة المركبة لا من العميل
        c.MarginBaseCost = tax.MarginBase(v.PurchasePrice, v.TotalCost); // أساس هامش الربح حسب إعداد المعرض (يحدده المحاسب)
        var vat = tax.Calculate(v.PurchasePrice, v.TotalCost, net, c.VatMode);
        // الهامش المعروض ربح العقد الفعلي (السعر − التكلفة الكلية)؛ أما الضريبة فعلى الهامش الخاضع حسب أساسه في الإعدادات
        c.NetPriceBeforeVat = vat.NetBeforeVat; c.ProfitMargin = Math.Max(0, net - c.CostPrice); c.ProfitMarginVat = vat.ProfitMarginVat;
        c.VatAmount = vat.VatAmount; c.TotalWithVat = vat.PriceWithVat;
        if ((c.DownPaymentAmount ?? 0) > c.TotalWithVat) throw new ValidationFailedException(Messages.DownPaymentExceedsTotal);
    }

    private static void ResetSystemFields(CarSalesContract c)
    {
        c.Status = SalesContractStatus.Draft; c.AllocatedVin = null; c.AllocatedAt = null; c.DeliveredAt = null;
        c.HandoverProtocolNumber = null; c.HandoverSignee = null; c.HandoverSigneeNationalId = null; c.InvoiceId = null; c.DepositAppliedAmount = null;
    }

    private static void RestoreSystemFields(CarSalesContract c, Microsoft.EntityFrameworkCore.ChangeTracking.PropertyValues o)
    {
        c.ContractNumber = (string)o[nameof(CarSalesContract.ContractNumber)]!;
        c.Status = (SalesContractStatus)o[nameof(CarSalesContract.Status)]!;
        c.AllocatedVin = (string?)o[nameof(CarSalesContract.AllocatedVin)];
        c.AllocatedAt = (DateTime?)o[nameof(CarSalesContract.AllocatedAt)];
        c.DeliveredAt = (DateTime?)o[nameof(CarSalesContract.DeliveredAt)];
        c.HandoverProtocolNumber = (string?)o[nameof(CarSalesContract.HandoverProtocolNumber)];
        c.HandoverSignee = (string?)o[nameof(CarSalesContract.HandoverSignee)];
        c.HandoverSigneeNationalId = (string?)o[nameof(CarSalesContract.HandoverSigneeNationalId)];
        c.InvoiceId = (Guid?)o[nameof(CarSalesContract.InvoiceId)];
        c.DepositAppliedAmount = (decimal?)o[nameof(CarSalesContract.DepositAppliedAmount)];
    }
}
