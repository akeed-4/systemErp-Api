using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using K = ERP.Core.Models.Accounting.ProjectMaterialIssueKinds;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// توريد مواد المشروع (العقد هو المشروع).
/// - المطلوب: الاحتياجات المعتمدة (دراسة الجدوى) محوّلة لوحدة الصنف المخزني لكل مادة مربوطة، مجمّعة لكل صنف.
/// - المطلوب شراؤه: أسطر طلبات الشراء المرتبطة بالعقد (غير المرفوضة) بالكمية المعتمدة ثم المطلوبة.
/// - المشترى: فواتير الشراء المرحّلة المحوّلة من تلك الطلبات.
/// - المسلَّم: أذونات تسليم المواد المرحّلة للمشروع، كل سطر على بند في جدول الكميات.
/// التسليم يحمّل التكلفة الفعلية على «مواد مشاريع تحت التنفيذ» (أصل) ولا يعترف بها مصروفاً؛ الاعتراف عند فوترة المستخلص الذي يختار الكميات.
/// طلب الشراء لا يتجاوز (المطلوب − ما طُلب في غيره)، والصرف لا يتجاوز (المطلوب − ما سُلِّم سابقاً)، وكلاهما لأصناف الاحتياجات وحدها.
/// </summary>
public class ProjectSupplyService : IProjectSupplyService
{
    private const string SourceType = "project_material_issue";
    private const int Decimals = 4;
    private readonly ErpDbContext _db;
    private readonly IProjectMaterialService _materials;
    private readonly IInventoryService _inventory;
    private readonly IAccountingPostingService _posting;
    private readonly INumberSequenceService _numbers;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;

    public ProjectSupplyService(ErpDbContext db, IProjectMaterialService materials, IInventoryService inventory, IAccountingPostingService posting,
        INumberSequenceService numbers, ITransactionRunner tx, ICurrentUser user, IAuditService audit)
    {
        _db = db; _materials = materials; _inventory = inventory; _posting = posting; _numbers = numbers; _tx = tx; _user = user; _audit = audit;
    }

    // ---------------- المتابعة ----------------
    public async Task<ProjectMaterialTrackingDto> TrackingAsync(Guid contractId, Guid? warehouseId = null, Guid? exceptRequisitionId = null, CancellationToken ct = default)
    {
        var contract = await _db.Set<CommercialContract>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == contractId, ct)
            ?? throw new NotFoundException(Messages.ContractNotFound);
        var plan = await _materials.GetAsync(contractId, ct);
        var planStatus = plan.Id == null ? "none" : plan.Status;
        var approved = planStatus == ProjectMaterialPlanStatuses.Approved;

        var required = new Dictionary<Guid, (decimal Qty, decimal Cost)>();
        if (approved)
            foreach (var l in plan.Lines.Where(l => l.ProductId.HasValue && l.ProductQuantity.HasValue))
            {
                var cur = required.GetValueOrDefault(l.ProductId!.Value);
                required[l.ProductId.Value] = (cur.Qty + l.ProductQuantity!.Value, cur.Cost + l.EstimatedCost);
            }

        var requested = await RequestedAsync(contractId, exceptRequisitionId, ct);
        var purchased = await PurchasedAsync(contractId, ct);
        var pipeline = await PipelineAsync(contractId, exceptRequisitionId, ct);
        var movements = await MovementsAsync(contractId, null, ct);
        var delivered = movements.GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(x => Net(x.Kind, x.Quantity)));
        var returned = movements.Where(x => x.Kind is K.Return or K.ReceiptReturn).GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        var wasted = movements.Where(x => x.Kind == K.Waste).GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        var consumed = await ConsumedAsync(contractId, ct);

        var ids = required.Keys.Concat(requested.Keys).Concat(purchased.Keys).Concat(delivered.Keys)
            .Concat(pipeline.Pending.Keys).Concat(pipeline.Ordered.Keys).Distinct().ToList();
        var products = await _db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Sku, p.NameAr, p.Unit, p.AverageCost, p.StandardCost, p.LastPurchaseCost }).ToDictionaryAsync(p => p.Id, ct);
        var wh = warehouseId ?? contract.WarehouseId;
        var stock = wh is Guid w
            ? await _db.Set<WarehouseStock>().AsNoTracking().Where(s => s.WarehouseId == w && ids.Contains(s.ItemId)).ToDictionaryAsync(s => s.ItemId, s => s.Quantity, ct)
            : await _db.Set<WarehouseStock>().AsNoTracking().Where(s => ids.Contains(s.ItemId)).GroupBy(s => s.ItemId)
                .Select(g => new { g.Key, Q = g.Sum(s => s.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Q, ct);

        var lines = ids.Where(products.ContainsKey).Select(id =>
        {
            var p = products[id];
            var (req, reqCost) = required.GetValueOrDefault(id);
            var unitCost = req > 0 && reqCost > 0 ? Math.Round(reqCost / req, Decimals)
                : p.AverageCost > 0 ? p.AverageCost : p.StandardCost ?? p.LastPurchaseCost;
            var r = requested.GetValueOrDefault(id); var d = delivered.GetValueOrDefault(id);
            return new ProjectMaterialTrackingLineDto
            {
                ProductId = id, Sku = p.Sku, ItemName = p.NameAr, Unit = p.Unit,
                RequiredQuantity = Math.Round(req, Decimals), RequestedQuantity = r, PurchasedQuantity = purchased.GetValueOrDefault(id),
                DeliveredQuantity = d, ReturnedQuantity = returned.GetValueOrDefault(id), WastedQuantity = wasted.GetValueOrDefault(id),
                ConsumedQuantity = Math.Round(consumed.GetValueOrDefault(id), Decimals),
                CustodyQuantity = Math.Round(d - wasted.GetValueOrDefault(id) - consumed.GetValueOrDefault(id), Decimals),
                RemainingToRequest = Math.Max(0, Math.Round(req - r, Decimals)),
                RemainingToDeliver = Math.Max(0, Math.Round(req - d, Decimals)), WarehouseStock = stock.GetValueOrDefault(id),
                UnitCost = unitCost, OutsidePlan = !required.ContainsKey(id),
                PendingRequestQuantity = Math.Round(pipeline.Pending.GetValueOrDefault(id), Decimals),
                ApprovedRequestQuantity = Math.Round(pipeline.Approved.GetValueOrDefault(id), Decimals),
                OrderedQuantity = Math.Round(pipeline.Ordered.GetValueOrDefault(id), Decimals),
                ReceivedQuantity = purchased.GetValueOrDefault(id),
            };
        }).OrderBy(l => l.OutsidePlan).ThenBy(l => l.Sku).ToList();
        foreach (var l in lines) MaterialCoverage.Apply(l, approved);
        var unverifiable = approved ? await UnverifiableAsync(plan, ct) : new List<ProjectMaterialUnverifiableDto>();

        var requiredValue = lines.Sum(l => l.RequiredQuantity * l.UnitCost);
        var deliveredValue = lines.Where(l => !l.OutsidePlan).Sum(l => Math.Min(l.DeliveredQuantity, l.RequiredQuantity) * l.UnitCost);
        return new ProjectMaterialTrackingDto
        {
            ContractId = contract.Id, ContractNumber = contract.ContractNumber, ProjectName = contract.ProjectName,
            ProjectLocation = contract.ProjectLocation, WarehouseId = wh, PlanStatus = planStatus,
            UnlinkedMaterials = approved ? plan.Lines.Count(l => !l.ProductId.HasValue) : 0,
            Lines = lines, Unverifiable = unverifiable,
            Totals = new ProjectMaterialTrackingTotalsDto
            {
                Items = lines.Count(l => !l.OutsidePlan), RequiredValue = DocumentPricing.Round(requiredValue), DeliveredValue = DocumentPricing.Round(deliveredValue),
                DeliveredPercent = requiredValue > 0 ? Math.Round(deliveredValue / requiredValue * 100m, 1) : 0,
                FullyDelivered = lines.Count(l => !l.OutsidePlan && l.RemainingToDeliver == 0 && l.RequiredQuantity > 0),
                PendingRequest = lines.Count(l => !l.OutsidePlan && l.RemainingToRequest > 0),
            },
        };
    }

    /// <summary>ما طُلب شراؤه لكل صنف في طلبات المشروع غير المرفوضة (عدا طلب بعينه عند تعديله).</summary>
    private async Task<Dictionary<Guid, decimal>> RequestedAsync(Guid contractId, Guid? exceptRequisitionId, CancellationToken ct)
        => (await _db.Set<MaterialRequisitionItem>().AsNoTracking()
                .Where(i => i.Requisition.ContractId == contractId && i.Requisition.Status != "rejected"
                    && (exceptRequisitionId == null || i.RequisitionId != exceptRequisitionId))
                .Select(i => new { i.ItemId, Q = i.ApprovedQuantity ?? i.RequestedQuantity }).ToListAsync(ct))
            .GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Q));

    /// <summary>
    /// المشترى لكل صنف: فواتير الشراء المرحّلة المحوّلة من طلبات المشروع (الدورة السابقة عبر المستودع)، والمستلم الصافي بسندات استلام
    /// المشروع (الدورة المعزولة: الاستلام من المورد هو الشراء، وفاتورته تسوية مالية لا تضيف كمية).
    /// </summary>
    private async Task<Dictionary<Guid, decimal>> PurchasedAsync(Guid contractId, CancellationToken ct)
    {
        var requisitions = await _db.Set<MaterialRequisition>().AsNoTracking().Where(r => r.ContractId == contractId).Select(r => (Guid?)r.Id).ToListAsync(ct);
        if (requisitions.Count == 0) return new();
        var purchased = (await _db.Set<PurchaseInvoice>().AsNoTracking()
                .Where(i => i.Status == "posted" && i.ReferenceType == "material_requisition" && requisitions.Contains(i.ReferenceId))
                .SelectMany(i => i.Items).Where(l => l.ItemId != Guid.Empty).Select(l => new { l.ItemId, l.Quantity }).ToListAsync(ct))
            .GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        foreach (var r in await ReceivedAsync(_db, contractId, ct))
            purchased[r.ItemId] = purchased.GetValueOrDefault(r.ItemId) + r.Quantity;
        // Legacy receipts created from the delivery-note form could be invoiced without carrying RequisitionId.
        // They are still project-specific by ContractId and must satisfy the project's purchase tracking.
        var unlinkedPostedReceipts = await _db.Set<DeliveryNoteItem>().AsNoTracking()
            .Where(i => i.DeliveryNote.ContractId == contractId && i.DeliveryNote.RequisitionId == null
                && i.DeliveryNote.Type == ProjectReceipts.NoteType && i.DeliveryNote.InvoiceId != null
                && _db.Set<PurchaseInvoice>().Any(invoice => invoice.Id == i.DeliveryNote.InvoiceId && invoice.Status == "posted"))
            .Select(i => new { i.ItemId, Quantity = i.DeliveredQty - i.ReturnedQty })
            .ToListAsync(ct);
        foreach (var receipt in unlinkedPostedReceipts)
            purchased[receipt.ItemId] = purchased.GetValueOrDefault(receipt.ItemId) + receipt.Quantity;
        return purchased;
    }

    public sealed record ProjectReceiptLine(Guid RequisitionId, Guid ItemId, decimal Quantity);

    /// <summary>صافي المستلم بسندات استلام المشروع لكل طلب وصنف (المستلم − المرتجع للمورد)، عدا سند بعينه عند تعديله.</summary>
    internal static async Task<List<ProjectReceiptLine>> ReceivedAsync(ErpDbContext db, Guid contractId, CancellationToken ct, Guid? exceptNoteId = null)
        => (await db.Set<DeliveryNoteItem>().AsNoTracking()
                .Where(i => i.DeliveryNote.ContractId == contractId && i.DeliveryNote.RequisitionId != null
                    && i.DeliveryNote.Type == ProjectReceipts.NoteType && (exceptNoteId == null || i.DeliveryNoteId != exceptNoteId))
                .Select(i => new { R = i.DeliveryNote.RequisitionId!.Value, i.ItemId, Q = i.DeliveredQty - i.ReturnedQty }).ToListAsync(ct))
            .Select(x => new ProjectReceiptLine(x.R, x.ItemId, x.Q)).ToList();

    /// <summary>
    /// الكميات في الطريق لكل صنف، كلٌّ في مرحلة واحدة من دورة الشراء القائمة (طلب ← فاتورة شراء مسودة ← ترحيلها = استلام):
    /// المعلّق = طلبات المشروع غير المرفوضة وغير المحوّلة (والمعتمد منها)، والمطلوب غير المستلم = أسطر فواتير الشراء غير المرحّلة
    /// المحوّلة من طلبات المشروع. الطلب المحوَّل لا يُعدّ معلّقاً، والفاتورة المرحّلة استلام داخل الرصيد، فلا تتكرر كمية.
    /// أوامر الشراء العامة لا ترتبط بالمشروع فلا تُنسب له.
    /// </summary>
    private async Task<(Dictionary<Guid, decimal> Pending, Dictionary<Guid, decimal> Approved, Dictionary<Guid, decimal> Ordered)> PipelineAsync(
        Guid contractId, Guid? exceptRequisitionId, CancellationToken ct)
    {
        var reqs = await _db.Set<MaterialRequisition>().AsNoTracking()
            .Where(r => r.ContractId == contractId && r.Status != "rejected" && (exceptRequisitionId == null || r.Id != exceptRequisitionId))
            .Select(r => new { r.Id, r.Status, Items = r.Items.Select(i => new { i.ItemId, Q = i.ApprovedQuantity ?? i.RequestedQuantity }).ToList() })
            .ToListAsync(ct);
        static bool Converted(string s) => s is "converted_to_invoice" or "converted_to_po";
        // المستلم بسندات استلام المشروع يخرج من المعلّق (صار عهدة): المعلّق ما بقي من كل طلب
        var received = (await ReceivedAsync(_db, contractId, ct)).GroupBy(x => (x.RequisitionId, x.ItemId)).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        var pending = new Dictionary<Guid, decimal>();
        var approved = new Dictionary<Guid, decimal>();
        foreach (var r in reqs.Where(r => !Converted(r.Status)))
            foreach (var g in r.Items.GroupBy(i => i.ItemId))
            {
                var open = Math.Max(0, g.Sum(i => i.Q) - received.GetValueOrDefault((r.Id, g.Key)));
                pending[g.Key] = pending.GetValueOrDefault(g.Key) + open;
                if (r.Status == "approved") approved[g.Key] = approved.GetValueOrDefault(g.Key) + open;
            }
        var converted = reqs.Where(r => Converted(r.Status)).Select(r => (Guid?)r.Id).ToList();
        var ordered = converted.Count == 0 ? new Dictionary<Guid, decimal>() : (await _db.Set<PurchaseInvoice>().AsNoTracking()
                .Where(i => i.ReferenceType == "material_requisition" && converted.Contains(i.ReferenceId) && i.Status != "posted" && i.Status != "cancelled")
                .SelectMany(i => i.Items).Where(l => l.ItemId != Guid.Empty).Select(l => new { l.ItemId, l.Quantity }).ToListAsync(ct))
            .GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        return (pending, approved, ordered);
    }

    /// <summary>مواد معتمدة بلا صنف مخزني أو بلا تحويل لوحدة صنفها: لا تُحسب تغطيتها، وتظهر بسببها لا كنقص ولا كاكتمال.</summary>
    private static Task<List<ProjectMaterialUnverifiableDto>> UnverifiableAsync(ProjectMaterialPlanDto plan, CancellationToken ct)
        => Task.FromResult(plan.Lines.Where(l => !l.ProductId.HasValue || !l.ProductQuantity.HasValue)
            .GroupBy(l => (l.ResourceId, l.Unit, l.ProductId))
            .Select(g => new ProjectMaterialUnverifiableDto
            {
                ResourceId = g.Key.ResourceId, Name = g.First().ResourceName ?? g.First().ProductName ?? string.Empty, Unit = g.Key.Unit,
                Quantity = Math.Round(g.Sum(l => l.ApprovedQuantity), Decimals), ProductId = g.Key.ProductId, ProductUnit = g.First().ProductUnit,
                Alert = new ProjectSupplyAlertDto
                {
                    Code = g.Key.ProductId.HasValue ? MaterialCoverage.UnitMismatch : MaterialCoverage.NotStocked, Severity = MaterialCoverage.Warning,
                    Quantity = Math.Round(g.Sum(l => l.ApprovedQuantity), Decimals),
                },
            }).OrderBy(u => u.Name).ToList());

    /// <summary>المسلَّم الصافي لكل صنف = التسليم (من المستودع أو استلام المورد) − الإرجاع (للمستودع أو للمورد)؛ الهدر يبقى مسلَّماً.</summary>
    private async Task<Dictionary<Guid, decimal>> DeliveredAsync(Guid contractId, Guid? exceptIssueId, CancellationToken ct)
        => (await MovementsAsync(contractId, exceptIssueId, ct))
            .GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.Sum(x => Net(x.Kind, x.Quantity)));

    /// <summary>أثر سطر على العهدة الصافية: التسليم والاستلام يزيدانها، والإرجاع للمستودع أو المورد ينقصها، والهدر لا يغيّرها.</summary>
    private static decimal Net(string kind, decimal value)
        => K.IsDelivery(kind) ? value : kind is K.Return or K.ReceiptReturn ? -value : 0;

    private sealed record Movement(Guid ProductId, string Kind, decimal Quantity, decimal Cost, Guid? BoqItemId);

    /// <summary>أسطر مستندات المواد المرحّلة للمشروع بأنواعها (المستند الأقدم بلا نوع = تسليم).</summary>
    private async Task<List<Movement>> MovementsAsync(Guid contractId, Guid? exceptIssueId, CancellationToken ct)
        => await _db.Set<ProjectMaterialIssueLine>().AsNoTracking()
            .Where(l => l.Issue.ContractId == contractId && l.Issue.Status == ProjectMaterialIssueStatuses.Posted
                && (exceptIssueId == null || l.IssueId != exceptIssueId))
            .Select(l => new Movement(l.ProductId, l.Issue.Kind, l.Quantity, l.TotalCost, l.BoqItemId)).ToListAsync(ct);

    /// <summary>المستهلك لكل صنف في التقارير اليومية المعتمدة، محوّلاً من وحدة المادة لوحدة الصنف (غير القابل للتحويل يُتخطى).</summary>
    private async Task<Dictionary<Guid, decimal>> ConsumedAsync(Guid contractId, CancellationToken ct)
    {
        var rows = await (from m in _db.Set<SiteMaterialUsage>().AsNoTracking()
                          join r in _db.Set<SiteDailyReport>().AsNoTracking() on m.ReportId equals r.Id
                          join p in _db.Set<Product>().AsNoTracking() on m.ProductId equals p.Id
                          where r.ContractId == contractId && r.Status == SiteDailyReportStatuses.Approved
                          select new { ProductId = p.Id, m.Quantity, m.Unit, ProductUnit = p.Unit }).ToListAsync(ct);
        if (rows.Count == 0) return new();
        var units = await BoqUnits.LoadAsync(_db, ct);
        return rows.Select(x => new { x.ProductId, Q = string.Equals(x.Unit, x.ProductUnit, StringComparison.OrdinalIgnoreCase) ? x.Quantity : units.Convert(x.Quantity, x.Unit, x.ProductUnit) })
            .Where(x => x.Q.HasValue).GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.Sum(x => x.Q!.Value));
    }

    /// <summary>المطلوب لكل صنف من الاحتياجات المعتمدة؛ يرفض إن لم تُعتمد.</summary>
    private async Task<Dictionary<Guid, decimal>> RequiredAsync(Guid contractId, CancellationToken ct)
    {
        var plan = await _materials.GetAsync(contractId, ct);
        if (plan.Id == null || plan.Status != ProjectMaterialPlanStatuses.Approved)
            throw new ValidationFailedException("اعتمد احتياجات مواد المشروع (دراسة الجدوى) أولاً: منها تُحسب الكميات المسموح طلبها وصرفها.");
        return plan.Lines.Where(l => l.ProductId.HasValue && l.ProductQuantity.HasValue)
            .GroupBy(l => l.ProductId!.Value).ToDictionary(g => g.Key, g => g.Sum(l => l.ProductQuantity!.Value));
    }

    public async Task EnsureRequisitionWithinPlanAsync(Guid contractId, Guid? requisitionId, IReadOnlyList<(Guid ItemId, decimal Quantity)> lines, CancellationToken ct = default)
    {
        await ExecutionPlanService.EnsureExecutionAllowedAsync(_db, contractId, ct);
        var required = await RequiredAsync(contractId, ct);
        var others = await RequestedAsync(contractId, requisitionId, ct);
        var names = await NamesAsync(lines.Select(l => l.ItemId), ct);
        foreach (var g in lines.GroupBy(l => l.ItemId))
        {
            var name = names.GetValueOrDefault(g.Key, "?");
            if (!required.TryGetValue(g.Key, out var req))
                throw new ValidationFailedException($"«{name}» ليس ضمن احتياجات المشروع المعتمدة: أضفه للاحتياجات (تعديل معتمد) قبل طلب شرائه.");
            var remaining = Math.Max(0, req - others.GetValueOrDefault(g.Key));
            var qty = g.Sum(l => l.Quantity);
            if (qty > remaining + 0.0001m)
                throw new ValidationFailedException($"«{name}»: الكمية المطلوبة {qty:0.####} تتجاوز المتبقي للطلب {remaining:0.####} (المطلوب في دراسة الجدوى {req:0.####} ناقص ما طُلب سابقاً {others.GetValueOrDefault(g.Key):0.####}).");
        }
    }

    // ---------------- مستندات الصرف ----------------
    public async Task<List<ProjectMaterialIssueDocDto>> ListIssuesAsync(Guid contractId, CancellationToken ct = default)
    {
        var rows = await _db.Set<ProjectMaterialIssue>().AsNoTracking().Include(i => i.Lines).Where(i => i.ContractId == contractId)
            .OrderByDescending(i => i.Date).ThenByDescending(i => i.CreatedAt).ToListAsync(ct);
        var extras = await ExtrasAsync(contractId, ct);
        return rows.Select(i => ToDto(i, null, extras)).ToList();
    }

    public Task<DevExtreme.AspNet.Data.ResponseModel.LoadResult> LoadIssuesAsync(DataSourceLoadOptions options, CancellationToken ct = default)
    {
        // العقد من مجموعته العامة: مرشّح نطاق العقود يسري، فلا يظهر إذن مشروع خارج نطاق المستخدم
        var rows = from i in _db.Set<ProjectMaterialIssue>().AsNoTracking()
                   join c in _db.Set<CommercialContract>().AsNoTracking() on i.ContractId equals c.Id
                   select new ProjectMaterialIssueRowDto
                   {
                       Id = i.Id, ContractId = i.ContractId, ContractNumber = c.ContractNumber, ProjectName = c.ProjectName ?? c.Title,
                       IssueNumber = i.IssueNumber, Date = i.Date, WarehouseId = i.WarehouseId,
                       WarehouseName = _db.Set<Warehouse>().Where(w => w.Id == i.WarehouseId).Select(w => w.NameAr).FirstOrDefault(),
                       ReceiverType = i.ReceiverType, ReceiverName = i.ReceiverName, Status = i.Status, TotalCost = i.TotalCost,
                       LineCount = i.Lines.Count, Kind = i.Kind, PostedAt = i.PostedAt, PostedBy = i.PostedBy, CreatedAt = i.CreatedAt,
                   };
        return EntityLoader.LoadRowsAsync(rows, options, nameof(ProjectMaterialIssueRowDto.Id), ct,
            EntityLoader.Desc(nameof(ProjectMaterialIssueRowDto.Date)), EntityLoader.Desc(nameof(ProjectMaterialIssueRowDto.CreatedAt)));
    }

    public async Task<ProjectMaterialIssueDocDto> GetIssueAsync(Guid contractId, Guid id, CancellationToken ct = default)
    {
        var issue = await LoadAsync(contractId, id, track: false, ct);
        return ToDto(issue, await ColumnsAsync(contractId, issue, ct), await ExtrasAsync(contractId, ct));
    }

    public Task<ProjectMaterialIssueDocDto> CreateIssueAsync(Guid contractId, SaveProjectMaterialIssueDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var contract = await ContractAsync(contractId, token);
            await ExecutionPlanService.EnsureExecutionAllowedAsync(_db, contractId, token);
            var issue = new ProjectMaterialIssue { Id = Guid.NewGuid(), ContractId = contractId, CostCenterId = contract.CostCenterId };
            await ApplyAsync(contract, issue, r, token);
            issue.IssueNumber = await _numbers.AssignAsync(new DocumentNumberRequest
            {
                Key = SourceType, DefaultPrefix = "PMI-", WarehouseId = issue.WarehouseId, Date = issue.Date,
                ManualNumber = r.ManualNumber, UserEntered = true,
                Exists = (n, c) => _db.Set<ProjectMaterialIssue>().AnyAsync(x => x.IssueNumber == n, c),
            }, token);
            _db.Add(issue);
            await _db.SaveChangesAsync(token);
            if (r.Post) await PostCoreAsync(contract, issue, token);
            return ToDto(issue, await ColumnsAsync(contractId, issue, token), await ExtrasAsync(contractId, token));
        }, ct);

    public Task<ProjectMaterialIssueDocDto> UpdateIssueAsync(Guid contractId, Guid id, SaveProjectMaterialIssueDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var contract = await ContractAsync(contractId, token);
            await ExecutionPlanService.EnsureExecutionAllowedAsync(_db, contractId, token);
            var issue = await LoadAsync(contractId, id, track: true, token);
            if (issue.Status != ProjectMaterialIssueStatuses.Draft) throw new ConflictException("مستند الصرف المرحّل لا يُعدَّل: ألغه وأنشئ غيره.");
            _db.RemoveRange(issue.Lines.ToList());
            issue.Lines.Clear();
            await ApplyAsync(contract, issue, r, token);
            await _db.SaveChangesAsync(token);
            if (r.Post) await PostCoreAsync(contract, issue, token);
            return ToDto(issue, await ColumnsAsync(contractId, issue, token), await ExtrasAsync(contractId, token));
        }, ct);

    public Task DeleteIssueAsync(Guid contractId, Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var issue = await LoadAsync(contractId, id, track: true, token);
            if (issue.Status != ProjectMaterialIssueStatuses.Draft) throw new ConflictException("مستند الصرف المرحّل لا يُحذف: يُلغى فيُعكس أثره.");
            _db.RemoveRange(issue.Lines.ToList());
            _db.Remove(issue);
            await _db.SaveChangesAsync(token);
        }, ct);

    public Task<ProjectMaterialIssueDocDto> PostIssueAsync(Guid contractId, Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var contract = await ContractAsync(contractId, token);
            await ExecutionPlanService.EnsureExecutionAllowedAsync(_db, contractId, token);
            var issue = await LoadAsync(contractId, id, track: true, token);
            if (issue.Status != ProjectMaterialIssueStatuses.Draft) throw new ConflictException("مستند الصرف مرحّل أو ملغى.");
            await PostCoreAsync(contract, issue, token);
            return ToDto(issue, await ColumnsAsync(contractId, issue, token), await ExtrasAsync(contractId, token));
        }, ct);

    public Task<ProjectMaterialIssueDocDto> CancelIssueAsync(Guid contractId, Guid id, string reason, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            if (string.IsNullOrWhiteSpace(reason)) throw new ValidationFailedException("سبب الإلغاء مطلوب.");
            var issue = await LoadAsync(contractId, id, track: true, token);
            // عهدة سند الاستلام وإرجاعها تتبعان السند: تُلغى بحذفه أو بحذف مرتجعه
            if (K.IsSystem(issue.Kind)) throw new ConflictException(Messages.ProjectReceiptCustodyManagedByNote);
            await CancelCoreAsync(issue, reason.Trim(), token);
            return ToDto(issue, null, await ExtrasAsync(contractId, token));
        }, ct);

    /// <summary>
    /// عكس مستند مواد مرحّل: يمنعه اختيار كمياته في مستخلص غير ملغى أو سحب مرحّل منه، ثم يعكس قيده وحركات مخزونه (للتسليم والإرجاع)
    /// ويعلّمه ملغى. يستدعيه الإلغاء من الشاشة، وسند الاستلام ومرتجعه لعهدتهما (داخل معاملة المستدعي).
    /// </summary>
    private async Task CancelCoreAsync(ProjectMaterialIssue issue, string reason, CancellationToken token)
    {
        if (issue.Status != ProjectMaterialIssueStatuses.Posted) throw new ConflictException("يُلغى مستند الصرف المرحّل فقط.");
        // كميات مختارة في مستخلص غير ملغى: تكلفتها مرتبطة بإيراده، فلا يُعكس التسليم قبل إخراجها منه (أو إلغائه/إلغاء فاتورته)
        var certified = await _db.Set<ContractCertificateMaterial>().AsNoTracking()
            .Where(m => m.IssueId == issue.Id && m.Certificate.Status != ContractCertificateStatuses.Cancelled)
            .Select(m => m.Certificate.CertificateNumber).Distinct().ToListAsync(token);
        if (certified.Count > 0)
            throw new ConflictException($"كميات من الإذن {issue.IssueNumber} مختارة في المستخلص {string.Join("، ", certified)}: أخرجها منه أو ألغِه (أو ألغِ فاتورته) قبل إلغاء الإذن.");
        // تسليم سُحب منه بإرجاع أو هدر مرحّل: يُلغى ذلك المستند أولاً
        var lineIds = issue.Lines.Select(l => l.Id).ToList();
        var withdrawals = await _db.Set<ProjectMaterialIssueLine>().AsNoTracking()
            .Where(l => l.SourceLineId != null && lineIds.Contains(l.SourceLineId.Value) && l.Issue.Status == ProjectMaterialIssueStatuses.Posted)
            .Select(l => l.Issue.IssueNumber).Distinct().ToListAsync(token);
        if (withdrawals.Count > 0)
            throw new ConflictException($"من الإذن {issue.IssueNumber} كميات مُرجعة أو مهدرة بالمستند {string.Join("، ", withdrawals)}: ألغِه أولاً.");
        // رمز التزامن على الأسطر: اختيارها في مستخلص بالتوازي مع هذا الإلغاء يفشل أحدهما بدل أن يمرّا معاً
        foreach (var line in issue.Lines) _db.Entry(line).State = EntityState.Modified;
        var note = $"إلغاء مستند مواد {issue.IssueNumber}: {reason}";
        if (issue.JournalEntryId is { } entryId) await _posting.ReverseAsync(entryId, note, token);
        decimal residual = 0;
        // الإرجاع يُعكس بحركة صادر من المستودع (والفرق عن تكلفة الإرجاع إعادة تقييم)، والهدر بلا حركة مخزون
        if (issue.Kind == K.Return)
            foreach (var line in issue.Lines.Where(l => l.Quantity > 0))
            {
                var outMove = await _inventory.RecordMovementAsync(new RecordStockMovementDto
                {
                    ItemId = line.ProductId, WarehouseId = issue.WarehouseId, Quantity = line.Quantity, Type = StockMovementType.AdjustmentOut,
                    ReferenceNumber = issue.IssueNumber, Date = issue.Date, Notes = note,
                    SourceType = SourceType + "_reversal", SourceId = issue.Id, SourceLineId = line.Id,
                }, token);
                residual += line.TotalCost + outMove.ValueChange;
            }
        foreach (var line in issue.Lines.Where(l => l.Quantity > 0 && issue.Kind == K.Issue))
        {
            var back = await _inventory.RecordMovementAsync(new RecordStockMovementDto
            {
                ItemId = line.ProductId, WarehouseId = issue.WarehouseId, Quantity = line.Quantity, Type = StockMovementType.AdjustmentIn,
                UnitCost = line.UnitCost, ReferenceNumber = issue.IssueNumber, Date = issue.Date, Notes = note,
                SourceType = SourceType + "_reversal", SourceId = issue.Id, SourceLineId = line.Id,
                BatchAllocations = await ItemBatches.OfDocumentAsync(_db, SourceType, issue.Id, line.ProductId, line.Id, token),
            }, token);
            residual += back.ValueChange - line.TotalCost;
        }
        await ItemBatches.MarkReversedAsync(_db, SourceType, issue.Id, token);
        await _db.SaveChangesAsync(token);
        if (issue.JournalEntryId != null)
        {
            var accounts = await PostingAccounts.SettingsAsync(_db, token);
            await InventoryValuation.PostRevaluationAsync(_db, _posting, residual, await accounts.InventoryAsync(_db, issue.WarehouseId, token), note, issue.Id, token);
        }
        issue.Status = ProjectMaterialIssueStatuses.Cancelled;
        issue.CancelReason = reason;
        issue.JournalEntryId = null;
        await _db.SaveChangesAsync(token);
        await _audit.LogAsync("PROJECT_MATERIAL_ISSUE_CANCELLED", nameof(ProjectMaterialIssue), issue.Id.ToString(), note, token);
    }

    // ---------------- عهدة سند استلام المشروع ----------------

    public async Task<Guid> PostPurchaseReceiptAsync(DeliveryNote note, CancellationToken ct = default)
    {
        var contract = await ContractAsync(note.ContractId!.Value, ct);
        var doc = new ProjectMaterialIssue
        {
            Id = Guid.NewGuid(), ContractId = contract.Id, IssueNumber = note.DeliveryNumber, Date = note.Date.Date, Kind = K.PurchaseReceipt,
            ReceiverType = ProjectIssueReceiverTypes.Storekeeper, ReceiverName = note.PartyName, ReceiverSupplierId = note.PartyId,
            RequisitionId = note.RequisitionId, DeliveryNoteId = note.Id, CostCenterId = contract.CostCenterId, Notes = note.Notes,
        };
        var sort = 0;
        foreach (var item in note.Items.Where(i => i.DeliveredQty > 0))
            doc.Lines.Add(new ProjectMaterialIssueLine
            {
                BoqItemId = item.BoqItemId, ProductId = item.ItemId, Sku = item.Sku ?? string.Empty, ItemName = item.ItemName, Unit = item.Unit ?? string.Empty,
                Quantity = item.DeliveredQty, UnitCost = item.UnitPrice, TotalCost = DocumentPricing.Round(item.DeliveredQty * item.UnitPrice), SortOrder = sort++,
            });
        doc.TotalCost = doc.Lines.Sum(l => l.TotalCost);
        _db.Add(doc);

        var accounts = await PostingAccounts.SettingsAsync(_db, ct);
        if (!accounts.IsPeriodic(doc.Date) && doc.TotalCost > 0)
        {
            // المواد تُحمَّل على المشروع مباشرة بسعر شرائها، ومقابلها التزام حتى تصل فاتورة المورد (لا مخزون عام ولا متوسط تكلفة)
            await ContractAccountsSetup.EnsureMissingAsync(_db, ct);
            accounts = await PostingAccounts.SettingsAsync(_db, ct);
            var wip = accounts.ContractAccount(nameof(AccountingSettings.ProjectMaterialsWipAccountCode));
            var grni = accounts.ContractAccount(nameof(AccountingSettings.ProjectGoodsReceivedNotInvoicedAccountCode));
            var text = $"استلام مواد للمشروع {contract.ContractNumber} من المورد {note.PartyName} - {note.DeliveryNumber}";
            doc.JournalEntryId = (await _posting.PostAsync(new GenericPostingRequest
            {
                Date = doc.Date, Description = text, SourceType = SourceType, SourceId = doc.Id, SourceNumber = doc.IssueNumber,
                Lines = new List<PostingLine> { new(wip, doc.TotalCost, 0, text, doc.CostCenterId), new(grni, 0, doc.TotalCost, text, doc.CostCenterId) },
            }, ct)).JournalEntryId;
            doc.DebitAccountCode = wip;
        }
        doc.Status = ProjectMaterialIssueStatuses.Posted;
        doc.PostedAt = SaudiTime.Now;
        doc.PostedBy = _user.Name;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("PROJECT_PURCHASE_RECEIPT_POSTED", nameof(ProjectMaterialIssue), doc.Id.ToString(),
            $"عهدة سند الاستلام {note.DeliveryNumber} للمشروع {contract.ContractNumber} بتكلفة {doc.TotalCost:0.00}", ct);
        return doc.Id;
    }

    public async Task<Guid> PostReceiptReturnAsync(DeliveryNote note, DeliveryReturnNote returnNote, CancellationToken ct = default)
    {
        var contract = await ContractAsync(note.ContractId!.Value, ct);
        var receipt = await LoadAsync(contract.Id, note.CustodyIssueId!.Value, track: false, ct);
        var withdrawn = await CertifiedMaterials.WithdrawnByLineAsync(_db, contract.Id, null, ct);
        var certified = await CertifiedMaterials.ByIssueLineAsync(_db, contract.Id, null, ct);
        var doc = new ProjectMaterialIssue
        {
            Id = Guid.NewGuid(), ContractId = contract.Id, IssueNumber = returnNote.ReturnNumber, Date = returnNote.Date.Date, Kind = K.ReceiptReturn,
            ReceiverType = ProjectIssueReceiverTypes.Storekeeper, ReceiverName = note.PartyName, ReceiverSupplierId = note.PartyId,
            DeliveryNoteId = note.Id, CostCenterId = receipt.CostCenterId, Notes = returnNote.ReturnReason,
        };
        var sort = 0;
        // كل صنف مرتجع يُسحب من أسطر العهدة بترتيبها بقدر المتاح فيها (غير المسحوب ولا المختار في مستخلص)
        foreach (var item in returnNote.Items.Where(i => i.Quantity > 0))
        {
            var left = item.Quantity;
            foreach (var src in receipt.Lines.Where(l => l.ProductId == item.ItemId).OrderBy(l => l.SortOrder))
            {
                var available = src.Quantity - withdrawn.GetValueOrDefault(src.Id).Quantity - certified.GetValueOrDefault(src.Id).Quantity;
                var take = Math.Min(left, Math.Max(0, available));
                if (take <= 0) continue;
                doc.Lines.Add(new ProjectMaterialIssueLine
                {
                    SourceLineId = src.Id, BoqItemId = src.BoqItemId, ProductId = src.ProductId, Sku = src.Sku, ItemName = src.ItemName, Unit = src.Unit,
                    Quantity = take, UnitCost = src.UnitCost, TotalCost = DocumentPricing.Round(take * src.UnitCost), SortOrder = sort++,
                });
                left -= take;
                if (left <= 0) break;
            }
            if (left > 0.00005m)
                throw new ConflictException(string.Format(Messages.ProjectReceiptReturnExceedsCustody, item.ItemName, note.DeliveryNumber));
        }
        _db.Add(doc);
        await PostWithdrawalAsync(contract, doc, ct);
        return doc.Id;
    }

    public async Task CancelReceiptCustodyAsync(Guid contractId, Guid custodyIssueId, string reason, CancellationToken ct = default)
    {
        var issue = await LoadAsync(contractId, custodyIssueId, track: true, ct);
        if (issue.Status == ProjectMaterialIssueStatuses.Posted) await CancelCoreAsync(issue, reason, ct);
    }

    // ---------------- البناء والترحيل ----------------
    private async Task ApplyAsync(CommercialContract contract, ProjectMaterialIssue issue, SaveProjectMaterialIssueDto r, CancellationToken ct)
    {
        var kind = string.IsNullOrWhiteSpace(r.Kind) ? (issue.Kind ?? K.Issue) : r.Kind.Trim();
        if (!K.IsValid(kind)) throw new ValidationFailedException("نوع مستند المواد غير صالح.");
        issue.Kind = kind;
        if (kind != K.Issue)
        {
            await ApplyWithdrawalAsync(contract, issue, r, ct);
            return;
        }
        if (!ProjectIssueReceiverTypes.IsValid(r.ReceiverType)) throw new ValidationFailedException("جهة الاستلام غير صالحة.");
        if (string.IsNullOrWhiteSpace(r.ReceiverName)) throw new ValidationFailedException("اسم المستلم في الموقع مطلوب.");
        var lines = r.Lines.Where(l => l.ProductId != Guid.Empty && l.Quantity > 0).ToList();
        if (lines.Count == 0) throw new ValidationFailedException("أضف صنفاً واحداً على الأقل بكمية تسليم موجبة.");
        if (r.Lines.Any(l => l.Quantity < 0)) throw new ValidationFailedException(Messages.QuantityMustBePositive);
        var warehouse = await WarehouseStocks.ResolveAsync(_db, r.WarehouseId ?? contract.WarehouseId, ct);
        if (r.ReceiverSupplierId.HasValue && !await _db.Set<Supplier>().AnyAsync(s => s.Id == r.ReceiverSupplierId, ct))
            throw new ValidationFailedException(Messages.SupplierNotFound);
        if (r.RequisitionId.HasValue && !await _db.Set<MaterialRequisition>().AnyAsync(q => q.Id == r.RequisitionId && q.ContractId == contract.Id, ct))
            throw new ValidationFailedException("طلب الشراء ليس من طلبات هذا المشروع.");

        issue.Date = (r.Date ?? SaudiTime.Today).Date;
        issue.WarehouseId = warehouse.Id;
        issue.ReceiverType = r.ReceiverType;
        issue.ReceiverName = r.ReceiverName.Trim();
        issue.ReceiverEmployeeId = r.ReceiverEmployeeId;
        issue.ReceiverSupplierId = r.ReceiverSupplierId;
        issue.RequisitionId = r.RequisitionId;
        issue.Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();

        var ids = lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        // بنود العقد نفسه (والمنشأة من مرشّح الاستعلام): المادة تُسلَّم لسطر قياس، لا لبند رئيسي تُقاس فروعه
        var boq = await _db.Set<CommercialContractItem>().AsNoTracking().Where(i => i.ContractId == contract.Id)
            .Select(i => new { i.Id, i.IsGroup }).ToDictionaryAsync(i => i.Id, i => i.IsGroup, ct);
        var sort = 0;
        foreach (var l in lines)
        {
            if (!products.TryGetValue(l.ProductId, out var p)) throw new ValidationFailedException(Messages.ProductNotFound);
            if (!ProductItemTypes.TracksStock(p.ItemType)) throw new ValidationFailedException($"«{p.NameAr}» صنف بلا رصيد مخزني: لا يُصرف من المستودع.");
            if (l.BoqItemId is not { } boqItemId) throw new ValidationFailedException($"«{p.NameAr}»: اختر بند جدول الكميات الذي تُسلَّم له المادة.");
            if (!boq.TryGetValue(boqItemId, out var isGroup)) throw new ValidationFailedException($"«{p.NameAr}»: البند المختار ليس من جدول كميات هذا المشروع.");
            if (isGroup) throw new ValidationFailedException($"«{p.NameAr}»: البند الرئيسي ذو الفروع لا تُسلَّم له مواد؛ اختر أحد فروعه.");
            var line = new ProjectMaterialIssueLine
            {
                Id = Guid.NewGuid(), IssueId = issue.Id, BoqItemId = boqItemId, ProductId = p.Id, Sku = p.Sku, ItemName = p.NameAr, Unit = p.Unit,
                Quantity = l.Quantity, Notes = string.IsNullOrWhiteSpace(l.Notes) ? null : l.Notes.Trim(), SortOrder = sort++,
            };
            issue.Lines.Add(line);
            // المستند الجديد يُضاف بأسطره لاحقاً (بعد ترقيمه)؛ إضافة السطر قبله تجعل حفظ عدّاد الترقيم يُدرجه بلا رأس
            if (_db.Entry(issue).State != EntityState.Detached) _db.Add(line);
        }
        await EnsureWithinPlanAsync(contract.Id, issue, ct);
        await EnsureInStockAsync(issue, ct);
    }

    /// <summary>
    /// الإرجاع والهدر: كل سطر يسحب كمية من سطر تسليم مرحّل لهذا المشروع، فيأخذ صنفه وبنده وتكلفته الفعلية. المستودع للإرجاع هو المستودع
    /// المستلِم (المختار، وإلا مستودع العقد). جهة «الاستلام» هنا من أعاد المواد أو أثبت الهدر.
    /// </summary>
    private async Task ApplyWithdrawalAsync(CommercialContract contract, ProjectMaterialIssue issue, SaveProjectMaterialIssueDto r, CancellationToken ct)
    {
        if (!ProjectIssueReceiverTypes.IsValid(r.ReceiverType)) throw new ValidationFailedException("جهة المُرجِع غير صالحة.");
        if (string.IsNullOrWhiteSpace(r.ReceiverName)) throw new ValidationFailedException("اسم المسؤول في الموقع مطلوب.");
        if (r.Lines.Any(l => l.Quantity < 0)) throw new ValidationFailedException(Messages.QuantityMustBePositive);
        var lines = r.Lines.Where(l => l.SourceLineId.HasValue && l.Quantity > 0).ToList();
        if (lines.Count == 0) throw new ValidationFailedException("اختر سطراً واحداً على الأقل من أذونات التسليم بكمية موجبة.");
        if (lines.GroupBy(l => l.SourceLineId).Any(g => g.Count() > 1)) throw new ValidationFailedException("سطر التسليم مكرر في المستند.");
        var warehouse = await WarehouseStocks.ResolveAsync(_db, r.WarehouseId ?? contract.WarehouseId, ct);

        issue.Date = (r.Date ?? SaudiTime.Today).Date;
        issue.WarehouseId = warehouse.Id;
        issue.ReceiverType = r.ReceiverType;
        issue.ReceiverName = r.ReceiverName.Trim();
        issue.ReceiverEmployeeId = r.ReceiverEmployeeId;
        issue.ReceiverSupplierId = r.ReceiverSupplierId;
        issue.RequisitionId = null;
        issue.Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();

        var ids = lines.Select(l => l.SourceLineId!.Value).ToList();
        var sources = await _db.Set<ProjectMaterialIssueLine>().AsNoTracking().Include(l => l.Issue).Where(l => ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var sort = 0;
        foreach (var l in lines)
        {
            if (!sources.TryGetValue(l.SourceLineId!.Value, out var src) || src.Issue.ContractId != contract.Id || !K.IsDelivery(src.Issue.Kind))
                throw new ValidationFailedException("السطر المختار ليس من أذونات تسليم هذا المشروع.");
            // عزل المشروع: ما استُلم من المورد للمشروع لا يدخل المستودع العام؛ يُرجع للمورد بمرتجع سند الاستلام (أو يُهدر)
            if (issue.Kind == K.Return && src.Issue.Kind == K.PurchaseReceipt)
                throw new ValidationFailedException(string.Format(Messages.ProjectReceiptNotReturnableToWarehouse, src.ItemName, src.Issue.IssueNumber));
            if (issue.Kind == K.ReceiptReturn && src.Issue.Kind != K.PurchaseReceipt)
                throw new ValidationFailedException("السطر المختار ليس من عهدة سند الاستلام.");
            var line = new ProjectMaterialIssueLine
            {
                Id = Guid.NewGuid(), IssueId = issue.Id, SourceLineId = src.Id, BoqItemId = src.BoqItemId, ProductId = src.ProductId, Sku = src.Sku,
                ItemName = src.ItemName, Unit = src.Unit, Quantity = l.Quantity, UnitCost = src.UnitCost,
                TotalCost = DocumentPricing.Round(l.Quantity * src.UnitCost), Notes = string.IsNullOrWhiteSpace(l.Notes) ? null : l.Notes.Trim(), SortOrder = sort++,
            };
            issue.Lines.Add(line);
            if (_db.Entry(issue).State != EntityState.Detached) _db.Add(line);
        }
        await EnsureWithdrawableAsync(contract.Id, issue, ct);
    }

    /// <summary>
    /// المسحوب من كل سطر تسليم ≤ كميته − المسحوب بمستندات مرحّلة أخرى − المختار في مستخلصات غير ملغاة؛ والإذن المصدر مرحّل
    /// (والقديم المرحّل مصروفاً لا يُسحب منه في الجرد المستمر: تكلفته اعتُرفت عند تسليمه). يُعاد الفحص عند الترحيل.
    /// </summary>
    private async Task EnsureWithdrawableAsync(Guid contractId, ProjectMaterialIssue doc, CancellationToken ct)
    {
        var ids = doc.Lines.Select(l => l.SourceLineId!.Value).ToList();
        var sources = await _db.Set<ProjectMaterialIssueLine>().AsNoTracking().Include(l => l.Issue).Where(l => ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var withdrawn = await CertifiedMaterials.WithdrawnByLineAsync(_db, contractId, doc.Id, ct);
        var certified = await CertifiedMaterials.ByIssueLineAsync(_db, contractId, null, ct);
        var periodic = (await PostingAccounts.SettingsAsync(_db, ct)).IsPeriodic(doc.Date);
        foreach (var line in doc.Lines)
        {
            var src = sources[line.SourceLineId!.Value];
            if (src.Issue.Status != ProjectMaterialIssueStatuses.Posted)
                throw new ValidationFailedException($"«{line.ItemName}»: الإذن {src.Issue.IssueNumber} غير مرحّل أو ملغى.");
            if (!periodic && src.Issue.DebitAccountCode == null)
                throw new ValidationFailedException($"«{line.ItemName}»: الإذن {src.Issue.IssueNumber} قديم رُحِّل مصروفاً؛ لا يُرجع منه ولا يُهدر بهذا المستند.");
            var available = src.Quantity - withdrawn.GetValueOrDefault(src.Id).Quantity - certified.GetValueOrDefault(src.Id).Quantity;
            if (line.Quantity > available + 0.00005m)
                throw new ValidationFailedException($"«{line.ItemName}» ({src.Issue.IssueNumber}): الكمية {line.Quantity:0.####} تتجاوز المتاح {Math.Max(0, available):0.####} (المسلَّم − المُرجَع والمهدر − المختار في المستخلصات).");
        }
    }

    /// <summary>
    /// ترحيل الإرجاع والهدر. الإرجاع: حركة وارد للمستودع بتكلفة التسليم، وقيد مدين المخزون / دائن المواد تحت التنفيذ. الهدر: بلا حركة،
    /// وقيد مدين تكلفة إيرادات العقود بمركز المشروع / دائن المواد تحت التنفيذ. الحساب الدائن هو حساب الإذن المصدر نفسه. الجرد الدوري بلا قيد.
    /// </summary>
    private async Task PostWithdrawalAsync(CommercialContract contract, ProjectMaterialIssue doc, CancellationToken ct)
    {
        await EnsureWithdrawableAsync(contract.Id, doc, ct);
        var isReturn = doc.Kind == K.Return;
        var toSupplier = doc.Kind == K.ReceiptReturn;
        var action = isReturn ? "إرجاع مواد من موقع" : toSupplier ? "إرجاع مواد للمورد من عهدة" : "هدر مواد في موقع";
        var note = $"{action} مشروع {contract.ContractNumber} - {doc.IssueNumber} - {doc.ReceiverName}";
        if (isReturn)
            foreach (var line in doc.Lines.OrderBy(l => l.SortOrder))
                await _inventory.RecordMovementAsync(new RecordStockMovementDto
                {
                    ItemId = line.ProductId, WarehouseId = doc.WarehouseId, Quantity = line.Quantity, Type = StockMovementType.AdjustmentIn,
                    UnitCost = line.UnitCost, ReferenceNumber = doc.IssueNumber, Date = doc.Date, Notes = note,
                    SourceType = SourceType, SourceId = doc.Id, SourceLineId = line.Id,
                }, ct);
        doc.TotalCost = doc.Lines.Sum(l => l.TotalCost);
        doc.DebitAccountCode = null;
        var accounts = await PostingAccounts.SettingsAsync(_db, ct);
        if (!accounts.IsPeriodic(doc.Date) && doc.TotalCost > 0)
        {
            var srcIds = doc.Lines.Select(l => l.SourceLineId!.Value).ToList();
            var wipOf = await _db.Set<ProjectMaterialIssueLine>().AsNoTracking().Where(l => srcIds.Contains(l.Id))
                .Select(l => new { l.Id, Wip = l.Issue.DebitAccountCode! }).ToDictionaryAsync(x => x.Id, x => x.Wip, ct);
            string debit;
            if (isReturn)
            {
                debit = await accounts.InventoryAsync(_db, doc.WarehouseId, ct);
                await DefaultAccounts.EnsureAsync(_db, ct, debit);
            }
            else
            {
                await ContractAccountsSetup.EnsureMissingAsync(_db, ct);
                accounts = await PostingAccounts.SettingsAsync(_db, ct);
                // الهدر تكلفة إيرادات، والإرجاع للمورد يعكس استحقاق الاستلام (البضاعة المستلمة غير المفوترة)
                debit = accounts.ContractAccount(toSupplier
                    ? nameof(AccountingSettings.ProjectGoodsReceivedNotInvoicedAccountCode)
                    : nameof(AccountingSettings.ContractCostOfRevenueAccountCode));
            }
            var lines = new List<PostingLine> { new(debit, doc.TotalCost, 0, note, doc.CostCenterId) };
            lines.AddRange(doc.Lines.GroupBy(l => wipOf[l.SourceLineId!.Value])
                .Select(g => new PostingLine(g.Key, 0, g.Sum(l => l.TotalCost), note, doc.CostCenterId)));
            doc.JournalEntryId = (await _posting.PostAsync(new GenericPostingRequest
            {
                Date = doc.Date, Description = note, SourceType = SourceType, SourceId = doc.Id, SourceNumber = doc.IssueNumber, Lines = lines,
            }, ct)).JournalEntryId;
            doc.DebitAccountCode = debit;
        }
        doc.Status = ProjectMaterialIssueStatuses.Posted;
        doc.PostedAt = SaudiTime.Now;
        doc.PostedBy = _user.Name;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(isReturn ? "PROJECT_MATERIAL_RETURN_POSTED" : toSupplier ? "PROJECT_RECEIPT_RETURN_POSTED" : "PROJECT_MATERIAL_WASTE_POSTED", nameof(ProjectMaterialIssue), doc.Id.ToString(),
            $"{note} بتكلفة {doc.TotalCost:0.00}", ct);
    }

    /// <summary>التسليم الحالي لكل صنف ≤ المطلوب في دراسة الجدوى − المسلَّم سابقاً (المستندات المرحّلة الأخرى).</summary>
    private async Task EnsureWithinPlanAsync(Guid contractId, ProjectMaterialIssue issue, CancellationToken ct)
    {
        var required = await RequiredAsync(contractId, ct);
        var before = await DeliveredAsync(contractId, issue.Id, ct);
        foreach (var g in issue.Lines.GroupBy(l => l.ProductId))
        {
            var name = g.First().ItemName;
            if (!required.TryGetValue(g.Key, out var req))
                throw new ValidationFailedException($"«{name}» ليس ضمن احتياجات المشروع المعتمدة: لا يُصرف للمشروع.");
            var remaining = Math.Max(0, req - before.GetValueOrDefault(g.Key));
            var qty = g.Sum(l => l.Quantity);
            if (qty > remaining + 0.0001m)
                throw new ValidationFailedException($"«{name}»: التسليم الحالي {qty:0.####} يتجاوز المتبقي للتسليم {remaining:0.####} (المطلوب {req:0.####} − المسلَّم سابقاً {before.GetValueOrDefault(g.Key):0.####}).");
        }
    }

    /// <summary>
    /// التسليم للمشروع خروج فعلي من المستودع: لا يتجاوز رصيد الصنف في مستودع الإذن أياً كانت سياسة المخزون السالب
    /// (السالب مقبول لمزامنة البيع غير المتصل فقط). المادة تُطلب وتُشترى وتُستلم في المستودع أولاً.
    /// </summary>
    private async Task EnsureInStockAsync(ProjectMaterialIssue issue, CancellationToken ct)
    {
        // المستودع نفسه الذي تُسجَّل منه حركة الصرف عند الترحيل (المحدد، وإلا الافتراضي)
        var warehouseId = (await WarehouseStocks.ResolveAsync(_db, issue.WarehouseId, ct)).Id;
        foreach (var g in issue.Lines.GroupBy(l => l.ProductId))
        {
            var qty = g.Sum(l => l.Quantity);
            var stock = await WarehouseStocks.QuantityAsync(_db, g.Key, warehouseId, ct);
            if (qty > stock + 0.0001m)
                throw new ValidationFailedException($"«{g.First().ItemName}»: التسليم {qty:0.####} يتجاوز رصيد المستودع {Math.Max(0, stock):0.####}. اطلب المادة واشترِها واستلمها في المستودع أولاً.");
        }
    }

    private async Task PostCoreAsync(CommercialContract contract, ProjectMaterialIssue issue, CancellationToken ct)
    {
        if (issue.Kind is K.Return or K.Waste)
        {
            await PostWithdrawalAsync(contract, issue, ct);
            return;
        }
        await EnsureWithinPlanAsync(contract.Id, issue, ct); // صرف آخر رُحِّل بعد حفظ المسودة
        await EnsureInStockAsync(issue, ct); // رصيد صرفه مستند آخر بعد حفظ المسودة
        var note = $"صرف مواد {issue.IssueNumber} لمشروع {contract.ContractNumber} - {issue.ReceiverName}";
        decimal total = 0;
        foreach (var line in issue.Lines.OrderBy(l => l.SortOrder))
        {
            var recorded = await _inventory.RecordMovementAsync(new RecordStockMovementDto
            {
                ItemId = line.ProductId, WarehouseId = issue.WarehouseId, Quantity = line.Quantity, Type = StockMovementType.AdjustmentOut,
                ReferenceNumber = issue.IssueNumber, Date = issue.Date, Notes = note,
                SourceType = SourceType, SourceId = issue.Id, SourceLineId = line.Id,
            }, ct);
            line.UnitCost = recorded.UnitCost;
            line.TotalCost = -recorded.ValueChange;
            total += line.TotalCost;
        }
        issue.TotalCost = total;
        issue.DebitAccountCode = null;
        var accounts = await PostingAccounts.SettingsAsync(_db, ct);
        if (!accounts.IsPeriodic(issue.Date) && total != 0)
        {
            // التسليم لا يعترف بتكلفة: المخزون يتحول أصلاً «مواد مشاريع تحت التنفيذ» حتى يُفوتر مستخلصه. الحساب من الإعدادات بلا افتراضي:
            // الناقص يُهيَّأ (الإعداد المحدد لا يُمس)، وإلا يُرفض الترحيل برسالة الإعداد الناقص بدل حساب مُخمَّن
            await ContractAccountsSetup.EnsureMissingAsync(_db, ct);
            accounts = await PostingAccounts.SettingsAsync(_db, ct);
            var wip = accounts.ContractAccount(nameof(AccountingSettings.ProjectMaterialsWipAccountCode));
            var inventory = await accounts.InventoryAsync(_db, issue.WarehouseId, ct);
            await DefaultAccounts.EnsureAsync(_db, ct, inventory);
            var amount = DocumentPricing.Round(Math.Abs(total));
            var lines = total > 0
                ? new List<PostingLine> { new(wip, amount, 0, note, issue.CostCenterId), new(inventory, 0, amount, note, issue.CostCenterId) }
                : new List<PostingLine> { new(inventory, amount, 0, note, issue.CostCenterId), new(wip, 0, amount, note, issue.CostCenterId) };
            issue.JournalEntryId = (await _posting.PostAsync(new GenericPostingRequest
            {
                Date = issue.Date, Description = note, SourceType = SourceType, SourceId = issue.Id, SourceNumber = issue.IssueNumber, Lines = lines,
            }, ct)).JournalEntryId;
            issue.DebitAccountCode = wip;
        }
        issue.Status = ProjectMaterialIssueStatuses.Posted;
        issue.PostedAt = SaudiTime.Now;
        issue.PostedBy = _user.Name;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("PROJECT_MATERIAL_ISSUE_POSTED", nameof(ProjectMaterialIssue), issue.Id.ToString(), $"{note} بتكلفة {total:0.00}", ct);
    }

    // ---------------- مساعدات ----------------
    private async Task<CommercialContract> ContractAsync(Guid contractId, CancellationToken ct)
        => await _db.Set<CommercialContract>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == contractId, ct)
           ?? throw new NotFoundException(Messages.ContractNotFound);

    private async Task<ProjectMaterialIssue> LoadAsync(Guid contractId, Guid id, bool track, CancellationToken ct)
    {
        var q = _db.Set<ProjectMaterialIssue>().Include(i => i.Lines);
        return await (track ? q : q.AsNoTracking()).FirstOrDefaultAsync(i => i.Id == id && i.ContractId == contractId, ct)
               ?? throw new NotFoundException("مستند صرف المواد غير موجود.");
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await _db.Set<Product>().AsNoTracking().Where(p => list.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.NameAr, ct);
    }

    /// <summary>أعمدة المستند: المطلوب، والمسلَّم قبله (بلا هذا المستند)، والمتبقي قبله.</summary>
    private async Task<Dictionary<Guid, (decimal Required, decimal Before)>?> ColumnsAsync(Guid contractId, ProjectMaterialIssue issue, CancellationToken ct)
    {
        var plan = await _materials.GetAsync(contractId, ct);
        var required = plan.Lines.Where(l => l.ProductId.HasValue && l.ProductQuantity.HasValue)
            .GroupBy(l => l.ProductId!.Value).ToDictionary(g => g.Key, g => g.Sum(l => l.ProductQuantity!.Value));
        var before = await DeliveredAsync(contractId, issue.Id, ct);
        return issue.Lines.Select(l => l.ProductId).Distinct()
            .ToDictionary(id => id, id => (required.GetValueOrDefault(id), before.GetValueOrDefault(id)));
    }

    /// <summary>بيانات القراءة المشتركة لأذونات العقد: وصف بنود جدول الكميات، والمختار من كل سطر في مستخلصات غير ملغاة (استعلامان للعقد كله).</summary>
    private sealed record IssueExtras(Dictionary<Guid, BoqLabel> Boq, Dictionary<Guid, (decimal Quantity, decimal Cost)> Certified,
        Dictionary<Guid, (decimal Quantity, decimal Cost)> Withdrawn,
        ProjectHeader? Project, Dictionary<Guid, string> Warehouses, Dictionary<Guid, string> ProductNamesEn);
    private sealed record BoqLabel(string? Code, string Description, string? DescriptionEn);
    private sealed record ProjectHeader(string ContractNumber, string? ProjectName, string? ProjectNameEn, string? ProjectLocation, string? PartyName);

    private async Task<IssueExtras> ExtrasAsync(Guid contractId, CancellationToken ct)
    {
        var boq = await _db.Set<CommercialContractItem>().AsNoTracking().Where(i => i.ContractId == contractId)
            .Select(i => new { i.Id, i.LineCode, i.Description, i.DescriptionEn })
            .ToDictionaryAsync(i => i.Id, i => new BoqLabel(i.LineCode, i.Description, i.DescriptionEn), ct);
        var project = await _db.Set<CommercialContract>().AsNoTracking().Where(c => c.Id == contractId)
            .Select(c => new ProjectHeader(c.ContractNumber, c.ProjectName ?? c.Title, c.ProjectNameEn ?? c.TitleEn, c.ProjectLocation, c.PartyName)).FirstOrDefaultAsync(ct);
        var warehouses = await _db.Set<Warehouse>().AsNoTracking()
            .Where(w => _db.Set<ProjectMaterialIssue>().Any(i => i.ContractId == contractId && i.WarehouseId == w.Id))
            .ToDictionaryAsync(w => w.Id, w => w.NameAr, ct);
        // الأسماء الإنجليزية لأصناف أذونات العقد (طباعة الإذن بالإنجليزية؛ السطر يحفظ الاسم العربي وقت إنشائه)
        var productNamesEn = await _db.Set<Product>().AsNoTracking()
            .Where(p => p.NameEn != "" && _db.Set<ProjectMaterialIssueLine>().Any(l => l.ProductId == p.Id && l.Issue.ContractId == contractId))
            .ToDictionaryAsync(p => p.Id, p => p.NameEn, ct);
        return new IssueExtras(boq, await CertifiedMaterials.ByIssueLineAsync(_db, contractId, null, ct),
            await CertifiedMaterials.WithdrawnByLineAsync(_db, contractId, null, ct), project, warehouses, productNamesEn);
    }

    // ---------- مواد البنود ----------
    public async Task<List<ProjectBoqMaterialLineDto>> BoqMaterialsAsync(Guid contractId, CancellationToken ct = default)
    {
        await ContractAsync(contractId, ct);
        var items = await _db.Set<CommercialContractItem>().AsNoTracking().Where(i => i.ContractId == contractId)
            .Select(i => new { i.Id, i.LineCode, i.Description, i.SortOrder }).ToDictionaryAsync(i => i.Id, ct);

        // المخطط على سطر القياس (الفرعي، وإلا البند نفسه) من الاحتياجات المعتمدة بوحدة الصنف
        var plan = await _materials.GetAsync(contractId, ct);
        var planned = plan.Id != null && plan.Status == ProjectMaterialPlanStatuses.Approved
            ? plan.Lines.Where(l => l.ProductId.HasValue && l.ProductQuantity.HasValue && (l.SubItemId ?? l.BoqItemId).HasValue)
                .GroupBy(l => ((l.SubItemId ?? l.BoqItemId)!.Value, l.ProductId!.Value)).ToDictionary(g => g.Key, g => g.Sum(l => l.ProductQuantity!.Value))
            : new Dictionary<(Guid, Guid), decimal>();
        // المسلَّم الصافي (التسليم − الإرجاع) وتكلفته، والهدر بتكلفته (اعتُرف به عند ترحيله فلا يبقى تحت التنفيذ)
        var moves = (await MovementsAsync(contractId, null, ct)).Where(m => m.BoqItemId != null).ToList();
        var delivered = moves.GroupBy(m => (m.BoqItemId!.Value, m.ProductId)).ToDictionary(g => g.Key, g => (
            Q: g.Sum(m => Net(m.Kind, m.Quantity)),
            C: g.Sum(m => Net(m.Kind, m.Cost)),
            W: g.Where(m => m.Kind == K.Waste).Sum(m => m.Cost)));
        var certified = (await CertifiedMaterials.Active(_db, contractId)
                .GroupBy(m => new { m.ContractItemId, m.ProductId, Invoiced = m.Certificate.Status == ContractCertificateStatuses.Invoiced })
                .Select(g => new { g.Key.ContractItemId, g.Key.ProductId, g.Key.Invoiced, Q = g.Sum(m => m.Quantity), C = g.Sum(m => m.TotalCost) }).ToListAsync(ct))
            .GroupBy(x => (x.ContractItemId, x.ProductId))
            .ToDictionary(g => g.Key, g => (Q: g.Sum(x => x.Q), C: g.Sum(x => x.C), Invoiced: g.Where(x => x.Invoiced).Sum(x => x.C)));

        var keys = planned.Keys.Concat(delivered.Keys).Concat(certified.Keys).Distinct().Where(k => items.ContainsKey(k.Item1)).ToList();
        var productIds = keys.Select(k => k.Item2).Distinct().ToList();
        var products = await _db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Sku, p.NameAr, p.Unit }).ToDictionaryAsync(p => p.Id, ct);
        return keys.Where(k => products.ContainsKey(k.Item2))
            .OrderBy(k => items[k.Item1].SortOrder).ThenBy(k => products[k.Item2].Sku)
            .Select(k =>
            {
                var item = items[k.Item1]; var p = products[k.Item2];
                var planQty = planned.GetValueOrDefault(k);
                var d = delivered.GetValueOrDefault(k);
                var c = certified.GetValueOrDefault(k);
                return new ProjectBoqMaterialLineDto
                {
                    ContractItemId = k.Item1, LineCode = item.LineCode, BoqDescription = item.Description, ProductId = p.Id, Sku = p.Sku, ItemName = p.NameAr, Unit = p.Unit,
                    PlannedQuantity = Math.Round(planQty, Decimals), DeliveredQuantity = d.Q, RemainingQuantity = Math.Max(0, Math.Round(planQty - d.Q, Decimals)),
                    DeliveredCost = d.C, CertifiedQuantity = c.Q, CertifiedCost = c.C, InvoicedCost = c.Invoiced, OutstandingCost = d.C - c.Invoiced - d.W,
                    OverPlanned = d.Q > planQty + 0.0001m,
                };
            }).ToList();
    }

    private static ProjectMaterialIssueDocDto ToDto(ProjectMaterialIssue i, Dictionary<Guid, (decimal Required, decimal Before)>? columns, IssueExtras extras) => new()
    {
        Id = i.Id, ContractId = i.ContractId, ContractNumber = extras.Project?.ContractNumber, ProjectName = extras.Project?.ProjectName,
        ProjectNameEn = extras.Project?.ProjectNameEn, ProjectLocation = extras.Project?.ProjectLocation, PartyName = extras.Project?.PartyName,
        WarehouseName = i.WarehouseId is { } wid ? extras.Warehouses.GetValueOrDefault(wid) : null,
        IssueNumber = i.IssueNumber, Date = i.Date, WarehouseId = i.WarehouseId, ReceiverType = i.ReceiverType,
        ReceiverName = i.ReceiverName, ReceiverEmployeeId = i.ReceiverEmployeeId, ReceiverSupplierId = i.ReceiverSupplierId, RequisitionId = i.RequisitionId,
        Status = i.Status, Kind = i.Kind, TotalCost = i.TotalCost, JournalEntryId = i.JournalEntryId, DebitAccountCode = i.DebitAccountCode, Notes = i.Notes,
        PostedAt = i.PostedAt, PostedBy = i.PostedBy, CancelReason = i.CancelReason,
        Lines = i.Lines.OrderBy(l => l.SortOrder).Select(l =>
        {
            var (req, before) = columns?.GetValueOrDefault(l.ProductId) ?? (0, 0);
            var boq = l.BoqItemId is { } b ? extras.Boq.GetValueOrDefault(b) : null;
            return new ProjectMaterialIssueLineDto
            {
                Id = l.Id, BoqItemId = l.BoqItemId, BoqLineCode = boq?.Code, BoqDescription = boq?.Description, BoqDescriptionEn = boq?.DescriptionEn,
                ProductId = l.ProductId, Sku = l.Sku, ItemName = l.ItemName, ItemNameEn = extras.ProductNamesEn.GetValueOrDefault(l.ProductId),
                Unit = l.Unit, Quantity = l.Quantity,
                UnitCost = l.UnitCost, TotalCost = l.TotalCost, Notes = l.Notes, CertifiedQuantity = extras.Certified.GetValueOrDefault(l.Id).Quantity,
                SourceLineId = l.SourceLineId, WithdrawnQuantity = extras.Withdrawn.GetValueOrDefault(l.Id).Quantity,
                RequiredQuantity = Math.Round(req, Decimals), DeliveredBefore = before, RemainingBefore = Math.Max(0, Math.Round(req - before, Decimals)),
            };
        }).ToList(),
    };
}
