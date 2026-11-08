using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

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
        var delivered = await DeliveredAsync(contractId, null, ct);

        var ids = required.Keys.Concat(requested.Keys).Concat(purchased.Keys).Concat(delivered.Keys).Distinct().ToList();
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
                DeliveredQuantity = d, RemainingToRequest = Math.Max(0, Math.Round(req - r, Decimals)),
                RemainingToDeliver = Math.Max(0, Math.Round(req - d, Decimals)), WarehouseStock = stock.GetValueOrDefault(id),
                UnitCost = unitCost, OutsidePlan = !required.ContainsKey(id),
            };
        }).OrderBy(l => l.OutsidePlan).ThenBy(l => l.Sku).ToList();

        var requiredValue = lines.Sum(l => l.RequiredQuantity * l.UnitCost);
        var deliveredValue = lines.Where(l => !l.OutsidePlan).Sum(l => Math.Min(l.DeliveredQuantity, l.RequiredQuantity) * l.UnitCost);
        return new ProjectMaterialTrackingDto
        {
            ContractId = contract.Id, ContractNumber = contract.ContractNumber, ProjectName = contract.ProjectName,
            ProjectLocation = contract.ProjectLocation, WarehouseId = wh, PlanStatus = planStatus,
            UnlinkedMaterials = approved ? plan.Lines.Count(l => !l.ProductId.HasValue) : 0,
            Lines = lines,
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

    private async Task<Dictionary<Guid, decimal>> PurchasedAsync(Guid contractId, CancellationToken ct)
    {
        var requisitions = await _db.Set<MaterialRequisition>().AsNoTracking().Where(r => r.ContractId == contractId).Select(r => (Guid?)r.Id).ToListAsync(ct);
        if (requisitions.Count == 0) return new();
        return (await _db.Set<PurchaseInvoice>().AsNoTracking()
                .Where(i => i.Status == "posted" && i.ReferenceType == "material_requisition" && requisitions.Contains(i.ReferenceId))
                .SelectMany(i => i.Items).Where(l => l.ItemId != Guid.Empty).Select(l => new { l.ItemId, l.Quantity }).ToListAsync(ct))
            .GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
    }

    private async Task<Dictionary<Guid, decimal>> DeliveredAsync(Guid contractId, Guid? exceptIssueId, CancellationToken ct)
        => (await _db.Set<ProjectMaterialIssueLine>().AsNoTracking()
                .Where(l => l.Issue.ContractId == contractId && l.Issue.Status == ProjectMaterialIssueStatuses.Posted
                    && (exceptIssueId == null || l.IssueId != exceptIssueId))
                .Select(l => new { l.ProductId, l.Quantity }).ToListAsync(ct))
            .GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

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
                       LineCount = i.Lines.Count, PostedAt = i.PostedAt, PostedBy = i.PostedBy, CreatedAt = i.CreatedAt,
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
            if (issue.Status != ProjectMaterialIssueStatuses.Posted) throw new ConflictException("يُلغى مستند الصرف المرحّل فقط.");
            // كميات مختارة في مستخلص غير ملغى: تكلفتها مرتبطة بإيراده، فلا يُعكس التسليم قبل إخراجها منه (أو إلغائه/إلغاء فاتورته)
            var certified = await _db.Set<ContractCertificateMaterial>().AsNoTracking()
                .Where(m => m.IssueId == issue.Id && m.Certificate.Status != ContractCertificateStatuses.Cancelled)
                .Select(m => m.Certificate.CertificateNumber).Distinct().ToListAsync(token);
            if (certified.Count > 0)
                throw new ConflictException($"كميات من الإذن {issue.IssueNumber} مختارة في المستخلص {string.Join("، ", certified)}: أخرجها منه أو ألغِه (أو ألغِ فاتورته) قبل إلغاء الإذن.");
            // رمز التزامن على الأسطر: اختيارها في مستخلص بالتوازي مع هذا الإلغاء يفشل أحدهما بدل أن يمرّا معاً
            foreach (var line in issue.Lines) _db.Entry(line).State = EntityState.Modified;
            var note = $"إلغاء صرف مواد {issue.IssueNumber}: {reason.Trim()}";
            if (issue.JournalEntryId is { } entryId) await _posting.ReverseAsync(entryId, note, token);
            decimal residual = 0;
            foreach (var line in issue.Lines.Where(l => l.Quantity > 0))
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
            issue.CancelReason = reason.Trim();
            issue.JournalEntryId = null;
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("PROJECT_MATERIAL_ISSUE_CANCELLED", nameof(ProjectMaterialIssue), issue.Id.ToString(), note, token);
            return ToDto(issue, null, await ExtrasAsync(contractId, token));
        }, ct);

    // ---------------- البناء والترحيل ----------------
    private async Task ApplyAsync(CommercialContract contract, ProjectMaterialIssue issue, SaveProjectMaterialIssueDto r, CancellationToken ct)
    {
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

    private async Task PostCoreAsync(CommercialContract contract, ProjectMaterialIssue issue, CancellationToken ct)
    {
        await EnsureWithinPlanAsync(contract.Id, issue, ct); // صرف آخر رُحِّل بعد حفظ المسودة
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
        ProjectHeader? Project, Dictionary<Guid, string> Warehouses);
    private sealed record BoqLabel(string? Code, string Description);
    private sealed record ProjectHeader(string ContractNumber, string? ProjectName, string? ProjectLocation, string? PartyName);

    private async Task<IssueExtras> ExtrasAsync(Guid contractId, CancellationToken ct)
    {
        var boq = await _db.Set<CommercialContractItem>().AsNoTracking().Where(i => i.ContractId == contractId)
            .Select(i => new { i.Id, i.LineCode, i.Description }).ToDictionaryAsync(i => i.Id, i => new BoqLabel(i.LineCode, i.Description), ct);
        var project = await _db.Set<CommercialContract>().AsNoTracking().Where(c => c.Id == contractId)
            .Select(c => new ProjectHeader(c.ContractNumber, c.ProjectName ?? c.Title, c.ProjectLocation, c.PartyName)).FirstOrDefaultAsync(ct);
        var warehouses = await _db.Set<Warehouse>().AsNoTracking()
            .Where(w => _db.Set<ProjectMaterialIssue>().Any(i => i.ContractId == contractId && i.WarehouseId == w.Id))
            .ToDictionaryAsync(w => w.Id, w => w.NameAr, ct);
        return new IssueExtras(boq, await CertifiedMaterials.ByIssueLineAsync(_db, contractId, null, ct), project, warehouses);
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
        var delivered = (await _db.Set<ProjectMaterialIssueLine>().AsNoTracking()
                .Where(l => l.Issue.ContractId == contractId && l.Issue.Status == ProjectMaterialIssueStatuses.Posted && l.BoqItemId != null)
                .GroupBy(l => new { Boq = l.BoqItemId!.Value, l.ProductId })
                .Select(g => new { g.Key.Boq, g.Key.ProductId, Q = g.Sum(l => l.Quantity), C = g.Sum(l => l.TotalCost) }).ToListAsync(ct))
            .ToDictionary(x => (x.Boq, x.ProductId), x => (Q: x.Q, C: x.C));
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
                    DeliveredCost = d.C, CertifiedQuantity = c.Q, CertifiedCost = c.C, InvoicedCost = c.Invoiced, OutstandingCost = d.C - c.Invoiced,
                    OverPlanned = d.Q > planQty + 0.0001m,
                };
            }).ToList();
    }

    private static ProjectMaterialIssueDocDto ToDto(ProjectMaterialIssue i, Dictionary<Guid, (decimal Required, decimal Before)>? columns, IssueExtras extras) => new()
    {
        Id = i.Id, ContractId = i.ContractId, ContractNumber = extras.Project?.ContractNumber, ProjectName = extras.Project?.ProjectName,
        ProjectLocation = extras.Project?.ProjectLocation, PartyName = extras.Project?.PartyName,
        WarehouseName = i.WarehouseId is { } wid ? extras.Warehouses.GetValueOrDefault(wid) : null,
        IssueNumber = i.IssueNumber, Date = i.Date, WarehouseId = i.WarehouseId, ReceiverType = i.ReceiverType,
        ReceiverName = i.ReceiverName, ReceiverEmployeeId = i.ReceiverEmployeeId, ReceiverSupplierId = i.ReceiverSupplierId, RequisitionId = i.RequisitionId,
        Status = i.Status, TotalCost = i.TotalCost, JournalEntryId = i.JournalEntryId, DebitAccountCode = i.DebitAccountCode, Notes = i.Notes,
        PostedAt = i.PostedAt, PostedBy = i.PostedBy, CancelReason = i.CancelReason,
        Lines = i.Lines.OrderBy(l => l.SortOrder).Select(l =>
        {
            var (req, before) = columns?.GetValueOrDefault(l.ProductId) ?? (0, 0);
            var boq = l.BoqItemId is { } b ? extras.Boq.GetValueOrDefault(b) : null;
            return new ProjectMaterialIssueLineDto
            {
                Id = l.Id, BoqItemId = l.BoqItemId, BoqLineCode = boq?.Code, BoqDescription = boq?.Description,
                ProductId = l.ProductId, Sku = l.Sku, ItemName = l.ItemName, Unit = l.Unit, Quantity = l.Quantity,
                UnitCost = l.UnitCost, TotalCost = l.TotalCost, Notes = l.Notes, CertifiedQuantity = extras.Certified.GetValueOrDefault(l.Id).Quantity,
                RequiredQuantity = Math.Round(req, Decimals), DeliveredBefore = before, RemainingBefore = Math.Max(0, Math.Round(req - before, Decimals)),
            };
        }).ToList(),
    };
}
