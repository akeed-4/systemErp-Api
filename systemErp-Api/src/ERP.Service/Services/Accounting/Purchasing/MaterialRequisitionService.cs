using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class MaterialRequisitionService : CrudService<MaterialRequisition, MaterialRequisitionDto, CreateMaterialRequisitionDto, UpdateMaterialRequisitionDto>, IMaterialRequisitionService
{
    private readonly INumberSequenceService _numbers;
    private readonly IInvoiceService _invoices;
    private readonly ICurrentUser _user;

    public MaterialRequisitionService(ErpDbContext db, INumberSequenceService numbers, IInvoiceService invoices, ICurrentUser user) : base(db)
    {
        _numbers = numbers; _invoices = invoices; _user = user;
    }

    protected override string Label => "طلب الشراء";
    protected override bool Transactional => true;

    protected override IQueryable<MaterialRequisition> ApplySearch(IQueryable<MaterialRequisition> q, string t)
        => q.Where(x => x.RequisitionNumber.Contains(t) || x.Department.Contains(t) || x.RequestedBy.Contains(t));

    protected override IQueryable<MaterialRequisition> ApplyFilters(IQueryable<MaterialRequisition> q, PaginationParams p)
        => string.IsNullOrWhiteSpace(p.Status) ? q : q.Where(x => x.Status == p.Status);

    protected override async Task ValidateAsync(CreateMaterialRequisitionDto d, MaterialRequisition? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        if (!TradeHelper.RequisitionStatuses.Contains(d.Status)) errors.Add("حالة غير صالحة.");
        if (d.Priority is not ("low" or "medium" or "high" or "urgent")) errors.Add("الأولوية: low | medium | high | urgent.");
        if (d.RequiredDate < d.RequestDate) errors.Add("تاريخ الاحتياج قبل تاريخ الطلب.");
        if (d.Items.Count == 0) errors.Add("يجب إدخال صنف واحد على الأقل.");
        if (d.Items.Any(i => i.RequestedQuantity <= 0)) errors.Add("الكميات المطلوبة يجب أن تكون موجبة.");
        var protectedStates = new[] { "approved", "converted_to_po", "converted_to_invoice" };
        if (protectedStates.Contains(d.Status) && d.Status != existing?.Status)
            errors.Add("الاعتماد والتحويل يتمان بعملياتهما المخصّصة فقط.");
        if (existing != null && existing.Status is "converted_to_po" or "converted_to_invoice") errors.Add("لا يمكن تعديل طلب تم تحويله.");
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
        if (d.SupplierId.HasValue && !await Db.Set<Supplier>().AnyAsync(s => s.Id == d.SupplierId, ct))
            throw new ValidationFailedException("المورد غير موجود.");
        if (d.WarehouseId.HasValue && !await Db.Set<Warehouse>().AnyAsync(w => w.Id == d.WarehouseId, ct))
            throw new ValidationFailedException("المستودع غير موجود.");
    }

    protected override async Task OnCreatingAsync(MaterialRequisition e, CreateMaterialRequisitionDto d, CancellationToken ct)
    {
        e.RequisitionNumber = await _numbers.NextAsync("material_requisition", "REQ-", ct);
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

    protected override Task OnDeletingAsync(MaterialRequisition e, CancellationToken ct)
        => e.Status is "converted_to_po" or "converted_to_invoice" ? throw new ConflictException("لا يمكن حذف طلب تم تحويله.") : Task.CompletedTask;

    public async Task<MaterialRequisitionDto> ApproveAsync(Guid id, CancellationToken ct = default)
    {
        var r = await Db.Set<MaterialRequisition>().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("طلب الشراء غير موجود");
        if (r.Status is not ("draft" or "pending_approval")) throw new ConflictException("لا يمكن اعتماد الطلب في حالته الحالية.");
        foreach (var i in r.Items) i.ApprovedQuantity ??= i.RequestedQuantity;
        r.Status = "approved"; r.ApprovedBy = _user.Name; r.ApprovalDate = DateTime.UtcNow;
        await Db.SaveChangesAsync(ct);
        return ToDto(r);
    }

    public async Task<InvoiceDto> ConvertToPurchaseInvoiceAsync(Guid id, CancellationToken ct = default)
        => await new TransactionRunner(Db).RunAsync(async token =>
        {
            var r = await Db.Set<MaterialRequisition>().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException("طلب الشراء غير موجود");
            if (r.Status != "approved") throw new ConflictException("يجب اعتماد الطلب قبل تحويله لفاتورة شراء.");
            if (r.SupplierId == null) throw new ValidationFailedException("حدّد المورد في الطلب قبل التحويل.");

            var ids = r.Items.Select(i => i.ItemId).ToList();
            var products = await Db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, token);
            var invoice = await _invoices.CreateAsync(new CreateInvoiceDto
            {
                Kind = InvoiceKind.Purchase, InvoiceType = InvoiceType.TaxInvoice, PartyId = r.SupplierId, PartyName = r.SupplierName ?? string.Empty,
                PaymentMethod = PaymentMethod.Credit, Status = "draft",
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
