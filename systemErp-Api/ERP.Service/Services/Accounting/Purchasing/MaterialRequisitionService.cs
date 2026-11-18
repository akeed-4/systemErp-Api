using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class MaterialRequisitionService : CrudService<MaterialRequisition, MaterialRequisitionDto, CreateMaterialRequisitionDto, UpdateMaterialRequisitionDto>, IMaterialRequisitionService
{
    private readonly INumberSequenceService _numbers;
    private readonly IPurchaseInvoiceService _invoices;
    private readonly ICurrentUser _user;
    private readonly IProjectSupplyService _supply;

    public MaterialRequisitionService(ErpDbContext db, INumberSequenceService numbers, IPurchaseInvoiceService invoices, ICurrentUser user,
        IProjectSupplyService supply) : base(db)
    {
        _numbers = numbers; _invoices = invoices; _user = user; _supply = supply;
    }

    protected override string Label => Messages.LabelPurchaseRequisition;
    protected override bool Transactional => true;

    protected override IQueryable<MaterialRequisition> ApplySearch(IQueryable<MaterialRequisition> q, string t)
        => q.Where(x => x.RequisitionNumber.Contains(t) || x.Department.Contains(t) || x.RequestedBy.Contains(t));

    protected override IQueryable<MaterialRequisition> ApplyFilters(IQueryable<MaterialRequisition> q, PaginationParams p)
        => string.IsNullOrWhiteSpace(p.Status) ? q : q.Where(x => x.Status == p.Status);

    protected override async Task ValidateAsync(CreateMaterialRequisitionDto d, MaterialRequisition? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        if (!TradeHelper.RequisitionStatuses.Contains(d.Status)) errors.Add(Messages.InvalidStatus);
        if (d.Priority is not ("low" or "medium" or "high" or "urgent")) errors.Add(Messages.PriorityValues);
        if (d.RequiredDate < d.RequestDate) errors.Add(Messages.RequiredDateBeforeRequestDate);
        if (d.Items.Count == 0) errors.Add(Messages.AtLeastOneItemRequired);
        if (d.Items.Any(i => i.RequestedQuantity <= 0)) errors.Add(Messages.RequestedQuantitiesMustBePositive);
        var protectedStates = new[] { "approved", "converted_to_po", "converted_to_invoice" };
        if (protectedStates.Contains(d.Status) && d.Status != existing?.Status)
            errors.Add(Messages.ApprovalAndConversionUseDedicatedActions);
        if (existing != null && existing.Status is "converted_to_po" or "converted_to_invoice") errors.Add(Messages.CannotEditConvertedRequisition);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
        if (d.SupplierId.HasValue && !await Db.Set<Supplier>().AnyAsync(s => s.Id == d.SupplierId, ct))
            throw new ValidationFailedException(Messages.SupplierNotFound);
        if (d.WarehouseId.HasValue && !await Db.Set<Warehouse>().AnyAsync(w => w.Id == d.WarehouseId, ct))
            throw new ValidationFailedException(Messages.WarehouseNotFound);
        await ValidateProjectAsync(d, existing, ct);
    }

    /// <summary>
    /// طلب شراء لمشروع: العقد موجود، والكميات ضمن احتياجات المشروع المعتمدة ناقص ما طُلب في طلباته الأخرى، وبند جدول الكميات لكل سطر
    /// (إن حُدد) سطر قياس من العقد نفسه. يُستلم بسندات استلام المشروع (عهدة بسعر الشراء لا مستودع عام) ثم يُفوتر منها.
    /// خيار «موقع المشروع» كان وصفياً بلا أثر مخزني أو محاسبي فأُلغي؛ الطلبات القديمة تحتفظ بقيمتها للعرض فقط.
    /// </summary>
    private async Task ValidateProjectAsync(CreateMaterialRequisitionDto d, MaterialRequisition? existing, CancellationToken ct)
    {
        d.DeliveryLocation = RequisitionDeliveryLocations.Warehouse;
        d.DeliveryAddress = null;
        if (d.ContractId is not { } contractId)
        {
            d.PhaseId = null;
            foreach (var i in d.Items) i.BoqItemId = null;
            return;
        }
        if (existing != null)
        {
            // المرحلة تثبت بعد الإنشاء: تقدم المراحل لا يغيّر سجلات المراحل السابقة، والعميل الذي لا يرسلها يحتفظ بها
            if (d.PhaseId != null && d.PhaseId != existing.PhaseId) throw new ValidationFailedException(Messages.ProjectRequisitionPhaseLocked);
            d.PhaseId = existing.PhaseId;
        }
        var contract = await Db.Set<CommercialContract>().AsNoTracking().Where(c => c.Id == contractId)
            .Select(c => new { c.WarehouseId }).FirstOrDefaultAsync(ct)
            ?? throw new ValidationFailedException(Messages.ContractNotFound);
        d.WarehouseId ??= contract.WarehouseId;
        var boqIds = d.Items.Where(i => i.BoqItemId.HasValue).Select(i => i.BoqItemId!.Value).Distinct().ToList();
        if (boqIds.Count > 0)
        {
            var valid = await Db.Set<CommercialContractItem>().AsNoTracking()
                .Where(i => i.ContractId == contractId && !i.IsGroup && boqIds.Contains(i.Id)).Select(i => i.Id).ToListAsync(ct);
            if (boqIds.Except(valid).Any()) throw new ValidationFailedException(Messages.ProjectRequisitionBoqItemInvalid);
        }
        // ما استُلم على الطلب ثابت: لا يُرفض بعده (الرفض يُسقط كمياته من المحسوب فتُطلب مرة ثانية)، ولا تنزل كمية صنف عن مستلمه.
        // إنقاص الطلب إلى مستلمه إقفال له على ما وصل
        if (existing != null)
        {
            var received = (await ProjectSupplyService.ReceivedAsync(Db, contractId, ct)).Where(r => r.RequisitionId == existing.Id)
                .GroupBy(r => r.ItemId).ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));
            if (received.Values.Any(q => q > 0))
            {
                if (d.Status == "rejected") throw new ConflictException(Messages.ProjectRequisitionRejectAfterReceipt);
                foreach (var (itemId, qty) in received.Where(kv => kv.Value > 0))
                {
                    var lines = d.Items.Where(i => i.ItemId == itemId).ToList();
                    var requested = lines.Sum(i => i.ApprovedQuantity ?? i.RequestedQuantity);
                    if (requested < qty - 0.0001m)
                        throw new ValidationFailedException(string.Format(Messages.ProjectRequisitionBelowReceived,
                            lines.FirstOrDefault()?.ItemName ?? existing.Items.FirstOrDefault(i => i.ItemId == itemId)?.ItemName ?? "?", qty.ToString("0.####")));
                }
            }
        }
        // الرفض يحرر الكميات فلا حد عليه ولا تفويض
        if (d.Status == "rejected") return;
        // الطلب القديم الذي سبق المراحل يبقى على حد المشروع كله
        await _supply.EnsureRequisitionWithinPlanAsync(contractId, existing?.Id, d.PhaseId, allowWithoutPhase: existing is { PhaseId: null },
            d.Items.Select(i => (i.ItemId, i.ApprovedQuantity ?? i.RequestedQuantity)).ToList(), ct);
    }

    protected override MaterialRequisitionDto ToDto(MaterialRequisition entity)
    {
        var dto = base.ToDto(entity);
        if (entity.ContractId is { } id)
        {
            var c = Db.Set<CommercialContract>().AsNoTracking().Where(x => x.Id == id)
                .Select(x => new { x.ContractNumber, x.ProjectName, x.ProjectNameEn }).FirstOrDefault();
            dto.ContractNumber = c?.ContractNumber;
            dto.ProjectName = c?.ProjectName;
            dto.ProjectNameEn = c?.ProjectNameEn;
        }
        if (entity.PhaseId is { } phaseId)
            dto.PhaseName = Db.Set<ProjectPhase>().AsNoTracking().Where(p => p.Id == phaseId).Select(p => p.Name).FirstOrDefault();
        return dto;
    }

    protected override async Task OnCreatingAsync(MaterialRequisition e, CreateMaterialRequisitionDto d, CancellationToken ct)
    {
        e.RequisitionNumber = await _numbers.AssignAsync(new DocumentNumberRequest
        {
            Key = "material_requisition", DefaultPrefix = "REQ-", WarehouseId = e.WarehouseId, Date = e.RequestDate,
            ManualNumber = d.RequisitionNumber, UserEntered = true,
            Exists = (n, c) => Db.Set<MaterialRequisition>().AnyAsync(x => x.RequisitionNumber == n, c),
        }, ct);
        e.TotalEstimatedCost = e.Items.Sum(i => (i.EstimatedCost ?? 0) * i.RequestedQuantity);
    }

    protected override Task OnUpdatingAsync(MaterialRequisition e, UpdateMaterialRequisitionDto d, CancellationToken ct)
    {
        var o = Db.Entry(e).OriginalValues;
        e.RequisitionNumber = o.GetValue<string>(nameof(MaterialRequisition.RequisitionNumber));
        e.ApprovedBy = o.GetValue<string?>(nameof(MaterialRequisition.ApprovedBy));
        e.ApprovalDate = o.GetValue<DateTime?>(nameof(MaterialRequisition.ApprovalDate));
        e.ConvertedInvoiceId = o.GetValue<Guid?>(nameof(MaterialRequisition.ConvertedInvoiceId));
        e.TotalEstimatedCost = e.Items.Sum(i => (i.EstimatedCost ?? 0) * i.RequestedQuantity);
        return Task.CompletedTask;
    }

    protected override async Task OnDeletingAsync(MaterialRequisition e, CancellationToken ct)
    {
        if (e.Status is "converted_to_po" or "converted_to_invoice") throw new ConflictException(Messages.CannotDeleteConvertedRequisition);
        if (await Db.Set<DeliveryNote>().AnyAsync(n => n.RequisitionId == e.Id, ct)) throw new ConflictException(Messages.ProjectRequisitionHasReceipts);
    }

    /// <summary>
    /// الاعتماد. طلب مرحلة يُعاد فحصه عند الاعتماد (حد المرحلة بعد أي تعديل للخطة أو طلب متزامن، وتفويض المرحلة المستقبلية للمعتمِد)
    /// داخل معاملة تُعاد عند تعارض رمز التزامن.
    /// </summary>
    public Task<MaterialRequisitionDto> ApproveAsync(Guid id, CancellationToken ct = default)
        => new TransactionRunner(Db).RunAsync(async token =>
        {
            var r = await Db.Set<MaterialRequisition>().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException(Messages.PurchaseRequisitionNotFound);
            if (r.Status is not ("draft" or "pending_approval")) throw new ConflictException(Messages.CannotApproveRequisitionInCurrentState);
            foreach (var i in r.Items) i.ApprovedQuantity ??= i.RequestedQuantity;
            if (r.ContractId is Guid contractId && r.PhaseId is Guid phaseId)
                await _supply.EnsureRequisitionWithinPlanAsync(contractId, r.Id, phaseId, allowWithoutPhase: false,
                    r.Items.Select(i => (i.ItemId, i.ApprovedQuantity ?? i.RequestedQuantity)).ToList(), token);
            r.Status = "approved"; r.ApprovedBy = _user.Name; r.ApprovalDate = SaudiTime.Today;
            await Db.SaveChangesAsync(token);
            return ToDto(r);
        }, ct);

    public async Task<InvoiceDto> ConvertToPurchaseInvoiceAsync(Guid id, CancellationToken ct = default)
        => await new TransactionRunner(Db).RunAsync(async token =>
        {
            var r = await Db.Set<MaterialRequisition>().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException(Messages.PurchaseRequisitionNotFound);
            if (r.Status != "approved") throw new ConflictException(Messages.ApproveRequisitionBeforeConversion);
            // عزل مشتريات المشروع: طلبه لا يدخل المستودع العام بفاتورة مباشرة؛ يُستلم بسند استلام ثم يُفوتر منه
            if (r.ContractId != null) throw new ConflictException(Messages.ProjectRequisitionReceivedByGrn);
            if (r.SupplierId == null) throw new ValidationFailedException(Messages.SetSupplierBeforeConversion);

            var ids = r.Items.Select(i => i.ItemId).ToList();
            var products = await Db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, token);
            // الرقم الضريبي للمورد مطلوب في الفاتورة الضريبية: يُنقل من بطاقته فلا تُرفض المسودة عند تعديلها وترحيلها
            var supplierVat = await Db.Set<Supplier>().AsNoTracking().Where(s => s.Id == r.SupplierId).Select(s => s.VatNumber).FirstOrDefaultAsync(token);
            var invoice = await _invoices.CreateAsync(new CreateInvoiceDto
            {
                SystemGenerated = true, // مستند ينشئه النظام: ترقيمه تلقائي دائماً
                Kind = InvoiceKind.Purchase, InvoiceType = InvoiceType.TaxInvoice, PartyId = r.SupplierId, PartyName = r.SupplierName ?? string.Empty,
                PartyVatNumber = string.IsNullOrWhiteSpace(supplierVat) ? null : supplierVat,
                PaymentMethod = PaymentMethod.Credit, Status = "draft",
                // مستودع الطلب (مستودع المشروع في المقاولات): الترحيل يُدخل المواد إليه لا إلى المستودع الافتراضي
                WarehouseId = r.WarehouseId,
                Notes = $"فاتورة شراء محوّلة من طلب {r.RequisitionNumber}",
                ReferenceType = "material_requisition", ReferenceId = r.Id, ReferenceNumber = r.RequisitionNumber,
                Items = r.Items.Select(i => new InvoiceItemDto
                {
                    ItemId = i.ItemId, ItemName = i.ItemName, Sku = i.Sku, Unit = i.Unit,
                    Quantity = i.ApprovedQuantity ?? i.RequestedQuantity,
                    UnitPrice = i.EstimatedCost ?? products.GetValueOrDefault(i.ItemId)?.LastPurchaseCost ?? 0,
                    VatRate = products.GetValueOrDefault(i.ItemId)?.VatRate ?? 15,
                }).ToList(),
            }, token);
            r.ConvertedInvoiceId = invoice.Id;
            r.Status = "converted_to_invoice";
            await Db.SaveChangesAsync(token);
            return invoice;
        }, ct);
}
