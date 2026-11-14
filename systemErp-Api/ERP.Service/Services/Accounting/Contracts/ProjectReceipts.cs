using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// سند استلام مشتريات المشروع: سند الاستلام نفسه (<see cref="DeliveryNote"/> بنوع purchase_delivery) على طلب شراء معتمد لمشروع مقاولات.
/// المواد لا تدخل المستودع العام ولا متوسط تكلفة الصنف: تصير عهدة للمشروع بسعر شرائها (<see cref="ProjectSupplyService.PostPurchaseReceiptAsync"/>)،
/// وفاتورة المورد من السند تسوّي استحقاق الاستلام. هنا قواعد السند: الطلب والمورد وبنود جدول الكميات والكمية والسعر.
/// </summary>
internal static class ProjectReceipts
{
    public const string NoteType = "purchase_delivery";
    public const string ReturnType = "purchase_delivery_return";
    private const string ApprovedStatus = "approved";

    public static bool IsProjectReceipt(DeliveryNote note) => note.Type == NoteType && note.RequisitionId != null;

    /// <summary>
    /// يتحقق من سند استلام مشروع ويكمله: المشروع والمورد من الطلب، وبند جدول الكميات من سطر الطلب، والسعر من تقديره إن لم يُدخل،
    /// والمستلم لكل صنف ≤ المطلوب في الطلب − المستلم بسنداته الأخرى (صافي المرتجع)، فلا تُستلم الكمية مرتين.
    /// </summary>
    public static async Task PrepareAsync(ErpDbContext db, DeliveryNote note, CancellationToken ct, bool warehouseReceipt = false)
    {
        var requisition = await db.Set<MaterialRequisition>().AsNoTracking().Include(r => r.Items)
            .FirstOrDefaultAsync(r => r.Id == note.RequisitionId, ct) ?? throw new NotFoundException(Messages.PurchaseRequisitionNotFound);
        if (requisition.ContractId is not { } contractId) throw new ValidationFailedException(Messages.ProjectReceiptNeedsProjectRequisition);
        if (note.ContractId.HasValue && note.ContractId != contractId) throw new ValidationFailedException(Messages.ProjectReceiptRequisitionOtherProject);
        if (requisition.Status != ApprovedStatus) throw new ConflictException(string.Format(Messages.ProjectReceiptRequisitionNotApproved, requisition.RequisitionNumber));

        var contract = await db.Set<CommercialContract>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == contractId, ct)
            ?? throw new ValidationFailedException(Messages.ContractNotFoundDot);
        if (contract.Stage is not (ContractStageDefinition.Active or ContractStageDefinition.InitialAcceptance))
            throw new ConflictException(Messages.DeliveryContractNotActive);
        await ExecutionPlanService.EnsureExecutionAllowedAsync(db, contractId, ct);
        note.ContractId = contractId;
        note.ContractNumber = contract.ContractNumber;

        // المورد: المختار، وإلا مورد الطلب (فاتورته منه)
        note.PartyId ??= requisition.SupplierId;
        // طلب أو سند بالاسم وحده (بلا معرّف): يُطابَق بمورد واحد بالاسم نفسه تماماً، وإلا يُطلب اختياره
        if (note.PartyId == null)
            foreach (var name in new[] { note.PartyName, requisition.SupplierName }.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim()).Distinct())
            {
                var matches = await db.Set<Supplier>().AsNoTracking().Where(s => s.NameAr == name || s.NameEn == name).Select(s => s.Id).Take(2).ToListAsync(ct);
                if (matches.Count != 1) continue;
                note.PartyId = matches[0];
                break;
            }
        if (note.PartyId is not { } supplierId) throw new ValidationFailedException(Messages.ProjectReceiptSupplierRequired);
        var supplier = await db.Set<Supplier>().AsNoTracking().Where(s => s.Id == supplierId)
            .Select(s => new { s.NameAr, s.VatNumber }).FirstOrDefaultAsync(ct) ?? throw new ValidationFailedException(Messages.SupplierNotFound);
        note.PartyType = "supplier";
        if (string.IsNullOrWhiteSpace(note.PartyName)) note.PartyName = supplier.NameAr;
        if (string.IsNullOrWhiteSpace(note.PartyTaxNumber)) note.PartyTaxNumber = supplier.VatNumber;
        // الاستلام في مستودع المشروع (من شاشة استلام المقاولات)، وإلا عهدة مباشرة للمشروع بلا مستودع
        if (warehouseReceipt)
        {
            var warehouseId = requisition.WarehouseId ?? contract.WarehouseId;
            if (warehouseId is null) throw new ValidationFailedException(Messages.ProjectReceiptNoProjectWarehouse);
            note.WarehouseId = warehouseId;
        }
        else note.WarehouseId = null;

        var boq = await db.Set<CommercialContractItem>().AsNoTracking().Where(i => i.ContractId == contractId)
            .Select(i => new { i.Id, i.IsGroup }).ToDictionaryAsync(i => i.Id, i => i.IsGroup, ct);
        var ids = note.Items.Select(i => i.ItemId).Distinct().ToList();
        var products = await db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        // بنود القياس لكل صنف من احتياجات المشروع المعتمدة (للطلب القديم أو المُنشأ بلا بند): الصنف من السطر أو من ربط مادته الحالي
        var planned = (await (from l in db.Set<ProjectMaterialRequirement>().AsNoTracking()
                              join p in db.Set<ProjectMaterialPlan>().AsNoTracking() on l.PlanId equals p.Id
                              join r in db.Set<ConstructionResource>().AsNoTracking() on l.ResourceId equals r.Id into rs
                              from r in rs.DefaultIfEmpty()
                              where p.ContractId == contractId && p.Status == ProjectMaterialPlanStatuses.Approved
                              select new { ProductId = l.ProductId ?? (r == null ? null : r.ProductId), Line = l.SubItemId ?? l.BoqItemId })
                             .ToListAsync(ct))
            .Where(x => x.ProductId.HasValue && x.Line.HasValue)
            .GroupBy(x => x.ProductId!.Value).ToDictionary(g => g.Key, g => g.Select(x => x.Line!.Value).Distinct().ToList());
        var errors = new List<string>();
        foreach (var item in note.Items)
        {
            var lines = requisition.Items.Where(r => r.ItemId == item.ItemId).ToList();
            if (!products.TryGetValue(item.ItemId, out var product) || lines.Count == 0)
            {
                errors.Add(string.Format(Messages.ProjectReceiptItemNotInRequisition, item.ItemName, requisition.RequisitionNumber));
                continue;
            }
            item.ItemName = string.IsNullOrWhiteSpace(item.ItemName) ? product.NameAr : item.ItemName;
            item.Sku ??= product.Sku;
            item.Unit ??= product.Unit;
            // بند جدول الكميات: المختار، وإلا بند سطر الطلب إن كان واحداً، وإلا بند الصنف الوحيد في احتياجات المشروع المعتمدة
            var boqIds = lines.Where(l => l.BoqItemId.HasValue).Select(l => l.BoqItemId!.Value).Distinct().ToList();
            item.BoqItemId ??= boqIds.Count == 1 ? boqIds[0] : null;
            if (item.BoqItemId == null && boqIds.Count == 0 && planned.TryGetValue(item.ItemId, out var planLines) && planLines.Count == 1)
                item.BoqItemId = planLines[0];
            if (item.BoqItemId is not { } boqItemId) errors.Add(string.Format(Messages.ProjectReceiptBoqItemRequired, item.ItemName));
            else if (!boq.TryGetValue(boqItemId, out var isGroup)) errors.Add(string.Format(Messages.ProjectReceiptBoqItemOtherProject, item.ItemName));
            else if (isGroup) errors.Add(string.Format(Messages.ProjectReceiptBoqItemIsGroup, item.ItemName));
            if (item.UnitPrice <= 0) item.UnitPrice = lines.Select(l => l.EstimatedCost ?? 0).FirstOrDefault(c => c > 0);
            if (item.UnitPrice <= 0) errors.Add(string.Format(Messages.ProjectReceiptPriceRequired, item.ItemName));
        }
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        // منع الاستلام المكرر: صافي المستلم بسندات الطلب الأخرى + هذا السند ≤ كمية الطلب لكل صنف
        var others = (await ProjectSupplyService.ReceivedAsync(db, contractId, ct, exceptNoteId: note.Id == Guid.Empty ? null : note.Id))
            .Where(r => r.RequisitionId == requisition.Id).GroupBy(r => r.ItemId).ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));
        foreach (var g in note.Items.GroupBy(i => i.ItemId))
        {
            var ordered = requisition.Items.Where(r => r.ItemId == g.Key).Sum(r => r.ApprovedQuantity ?? r.RequestedQuantity);
            var before = others.GetValueOrDefault(g.Key);
            var qty = g.Sum(i => i.DeliveredQty);
            if (qty > ordered - before + 0.00005m)
                throw new ValidationFailedException(string.Format(Messages.ProjectReceiptExceedsRequisition,
                    g.First().ItemName, qty.ToString("0.####"), Math.Max(0, ordered - before).ToString("0.####"), ordered.ToString("0.####"), before.ToString("0.####")));
        }
    }
}
