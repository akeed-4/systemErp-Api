using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting.Contracts;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using K = ERP.Core.Models.Accounting.ProjectMaterialIssueKinds;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// حجز مخزون الموقع للمشاريع (<see cref="StockReservation"/>). القواعد:
/// الحجز لمشروع ساري لصنف في احتياجاته المعتمدة، ولا يتجاوز المتاح (الرصيد الفعلي − حجوزات المشاريع الأخرى − حجزه القائم)،
/// ولا يتجاوز مع المسلَّم للمشروع المطلوبَ المعتمد (منع الحجز المكرر). التحويل لمشروع آخر طلب يعتمده غير مقدّمه.
/// كل تغيير يقفل الصنف في الموقع (<see cref="StockReservations.LockAsync"/>) فلا يتجاوز حجزان أو حجز وصرف متزامنان الرصيد.
/// </summary>
public class StockReservationService : IStockReservationService
{
    private const decimal Tolerance = 0.0001m;
    private readonly ErpDbContext _db;
    private readonly IProjectMaterialService _materials;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;

    public StockReservationService(ErpDbContext db, IProjectMaterialService materials, ITransactionRunner tx, ICurrentUser user, IAuditService audit)
    {
        _db = db; _materials = materials; _tx = tx; _user = user; _audit = audit;
    }

    // ---------------- الحجوزات ----------------

    public async Task<List<StockReservationDto>> ListAsync(Guid contractId, CancellationToken ct = default)
    {
        await ContractAsync(contractId, ct);
        var rows = await _db.Set<StockReservation>().AsNoTracking().Where(r => r.ContractId == contractId)
            .OrderByDescending(r => r.Status == StockReservationStatuses.Active).ThenByDescending(r => r.CreatedAt).ToListAsync(ct);
        return await ToDtosAsync(rows, ct);
    }

    public Task<StockReservationDto> ReserveAsync(Guid contractId, SaveStockReservationDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            if (request.Quantity <= 0) throw new ValidationFailedException(Messages.StockReservationQtyPositive);
            var contract = await ContractAsync(contractId, token);
            EnsureExecuting(contract.Stage);
            var product = await _db.Set<Product>().AsNoTracking().Where(p => p.Id == request.ProductId)
                .Select(p => new { p.Id, p.NameAr }).FirstOrDefaultAsync(token)
                ?? throw new ValidationFailedException(Messages.ProductNotFound);
            var warehouseId = (await WarehouseStocks.ResolveAsync(_db, request.WarehouseId ?? contract.WarehouseId, token)).Id;
            await StockReservations.LockAsync(_db, product.Id, warehouseId, token);
            await EnsureWithinRequirementAsync(contractId, product.Id, product.NameAr, request.Quantity, token);
            await EnsureFreeAsync(product.Id, product.NameAr, warehouseId, request.Quantity, token);

            var reservation = new StockReservation
            {
                ContractId = contractId, ProductId = product.Id, WarehouseId = warehouseId, Quantity = request.Quantity,
                CreatedByUserId = _user.UserId, CreatedBy = _user.Name, Notes = Trim(request.Notes),
            };
            _db.Add(reservation);
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("STOCK_RESERVED", nameof(StockReservation), reservation.Id.ToString(),
                $"حجز {request.Quantity:0.####} من «{product.NameAr}» لمشروع {contract.ContractNumber}", token);
            return (await ToDtosAsync(new() { reservation }, token))[0];
        }, ct);

    public Task<StockReservationDto> ReleaseAsync(Guid contractId, Guid reservationId, ReleaseStockReservationDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var reservation = await LoadAsync(contractId, reservationId, token);
            await StockReservations.LockAsync(_db, reservation.ProductId, reservation.WarehouseId, token);
            await _db.Entry(reservation).ReloadAsync(token);
            var free = StockReservations.Remaining(reservation) - await PendingTransfersAsync(reservation.Id, token);
            var qty = request.Quantity ?? free;
            if (reservation.Status != StockReservationStatuses.Active || qty <= 0)
                throw new ConflictException(Messages.StockReservationNothingToRelease);
            if (qty > free + Tolerance)
                throw new ValidationFailedException(string.Format(Messages.StockReservationReleaseExceeds, qty, Math.Max(0, free)));
            reservation.ReleasedQuantity += qty;
            if (StockReservations.Remaining(reservation) <= Tolerance) reservation.Status = StockReservationStatuses.Closed;
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("STOCK_RESERVATION_RELEASED", nameof(StockReservation), reservation.Id.ToString(),
                $"تحرير {qty:0.####} من الحجز{(string.IsNullOrWhiteSpace(request.Reason) ? "" : ": " + request.Reason.Trim())}", token);
            return (await ToDtosAsync(new() { reservation }, token))[0];
        }, ct);

    // ---------------- تحويل التخصيص ----------------

    public Task<StockReservationTransferDto> RequestTransferAsync(Guid contractId, Guid reservationId, RequestReservationTransferDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            if (request.Quantity <= 0) throw new ValidationFailedException(Messages.StockReservationTransferQtyPositive);
            if (string.IsNullOrWhiteSpace(request.Reason)) throw new ValidationFailedException(Messages.StockReservationTransferReasonRequired);
            if (request.TargetContractId == contractId) throw new ValidationFailedException(Messages.StockReservationTransferSameProject);
            var reservation = await LoadAsync(contractId, reservationId, token);
            var target = await ContractAsync(request.TargetContractId, token);
            EnsureExecuting(target.Stage);
            await StockReservations.LockAsync(_db, reservation.ProductId, reservation.WarehouseId, token);
            await _db.Entry(reservation).ReloadAsync(token);
            var free = StockReservations.Remaining(reservation) - await PendingTransfersAsync(reservation.Id, token);
            if (reservation.Status != StockReservationStatuses.Active || request.Quantity > free + Tolerance)
                throw new ValidationFailedException(string.Format(Messages.StockReservationTransferExceeds, request.Quantity, Math.Max(0, free)));
            var name = await ProductNameAsync(reservation.ProductId, token);
            await EnsureWithinRequirementAsync(target.Id, reservation.ProductId, name, request.Quantity, token);

            var transfer = new StockReservationTransfer
            {
                SourceReservationId = reservation.Id, SourceContractId = contractId, TargetContractId = target.Id,
                ProductId = reservation.ProductId, WarehouseId = reservation.WarehouseId, Quantity = request.Quantity,
                Reason = request.Reason.Trim()[..Math.Min(500, request.Reason.Trim().Length)],
                RequestedByUserId = _user.UserId, RequestedBy = _user.Name,
            };
            _db.Add(transfer);
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("STOCK_RESERVATION_TRANSFER_REQUESTED", nameof(StockReservationTransfer), transfer.Id.ToString(),
                $"طلب تحويل {request.Quantity:0.####} من «{name}» إلى مشروع {target.ContractNumber}: {transfer.Reason}", token);
            return (await TransferDtosAsync(new() { transfer }, token))[0];
        }, ct);

    public async Task<List<StockReservationTransferDto>> TransfersAsync(string? status, Guid? contractId, CancellationToken ct = default)
    {
        // في نطاق وصول المستخدم إلى العقود: طلب لا يظهر مصدره ولا مستلمه له لا يُعرض
        var visible = _db.Set<CommercialContract>().Select(c => c.Id);
        var rows = await _db.Set<StockReservationTransfer>().AsNoTracking()
            .Where(t => (status == null || t.Status == status)
                && (contractId == null || t.SourceContractId == contractId || t.TargetContractId == contractId)
                && (visible.Contains(t.SourceContractId) || visible.Contains(t.TargetContractId)))
            .OrderByDescending(t => t.CreatedAt).Take(500).ToListAsync(ct);
        return await TransferDtosAsync(rows, ct);
    }

    public Task<StockReservationTransferDto> ApproveTransferAsync(Guid transferId, ReservationTransferDecisionDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var transfer = await PendingTransferAsync(transferId, token);
            await StockReservations.LockAsync(_db, transfer.ProductId, transfer.WarehouseId, token);
            await _db.Entry(transfer).ReloadAsync(token);
            if (transfer.Status != StockReservationTransferStatuses.Pending) throw new ConflictException(Messages.StockReservationTransferNotPending);
            var source = await _db.Set<StockReservation>().FirstAsync(r => r.Id == transfer.SourceReservationId, token);
            // باقي الحجز الآن (قد يكون صُرف منه بعد الطلب)، والمعلّق من غيره من الطلبات على الحجز نفسه
            var otherPending = await PendingTransfersAsync(source.Id, token, exceptTransferId: transfer.Id);
            if (source.Status != StockReservationStatuses.Active || transfer.Quantity > StockReservations.Remaining(source) - otherPending + Tolerance)
                throw new ConflictException(Messages.StockReservationTransferSourceShort);
            var target = await ContractAsync(transfer.TargetContractId, token);
            EnsureExecuting(target.Stage);
            var name = await ProductNameAsync(transfer.ProductId, token);
            await EnsureWithinRequirementAsync(target.Id, transfer.ProductId, name, transfer.Quantity, token);

            source.ReleasedQuantity += transfer.Quantity;
            if (StockReservations.Remaining(source) <= Tolerance) source.Status = StockReservationStatuses.Closed;
            var created = new StockReservation
            {
                ContractId = target.Id, ProductId = transfer.ProductId, WarehouseId = transfer.WarehouseId, Quantity = transfer.Quantity,
                Source = StockReservationSources.Transfer, SourceTransferId = transfer.Id,
                CreatedByUserId = _user.UserId, CreatedBy = _user.Name, Notes = transfer.Reason,
            };
            _db.Add(created);
            await _db.SaveChangesAsync(token);
            Decide(transfer, StockReservationTransferStatuses.Approved, request.Note);
            transfer.TargetReservationId = created.Id;
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("STOCK_RESERVATION_TRANSFER_APPROVED", nameof(StockReservationTransfer), transfer.Id.ToString(),
                $"اعتماد تحويل {transfer.Quantity:0.####} من «{name}» إلى مشروع {target.ContractNumber}", token);
            return (await TransferDtosAsync(new() { transfer }, token))[0];
        }, ct);

    public Task<StockReservationTransferDto> RejectTransferAsync(Guid transferId, ReservationTransferDecisionDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            if (string.IsNullOrWhiteSpace(request.Note)) throw new ValidationFailedException(Messages.StockReservationRejectReasonRequired);
            var transfer = await PendingTransferAsync(transferId, token);
            Decide(transfer, StockReservationTransferStatuses.Rejected, request.Note);
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("STOCK_RESERVATION_TRANSFER_REJECTED", nameof(StockReservationTransfer), transfer.Id.ToString(), request.Note!.Trim(), token);
            return (await TransferDtosAsync(new() { transfer }, token))[0];
        }, ct);

    // ---------------- المتاح والمحجوز ----------------

    public async Task<List<StockAvailabilityDto>> AvailabilityAsync(IReadOnlyCollection<Guid> productIds, Guid? warehouseId, Guid? contractId,
        CancellationToken ct = default)
    {
        var ids = productIds.Distinct().Take(500).ToList();
        if (ids.Count == 0) return new();
        var products = await _db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Sku, p.NameAr, p.Unit }).ToListAsync(ct);

        var onHand = await _db.Set<WarehouseStock>().AsNoTracking()
            .Where(s => ids.Contains(s.ItemId) && (warehouseId == null || s.WarehouseId == warehouseId))
            .GroupBy(s => s.ItemId).Select(g => new { g.Key, Q = g.Sum(s => s.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Q, ct);

        var reservations = await _db.Set<StockReservation>().AsNoTracking()
            .Where(r => ids.Contains(r.ProductId) && r.Status == StockReservationStatuses.Active && (warehouseId == null || r.WarehouseId == warehouseId))
            .Select(r => new { r.ProductId, r.ContractId, Q = r.Quantity - r.IssuedQuantity - r.ReleasedQuantity }).ToListAsync(ct);

        // المصروف للمشاريع من الموقع: تسليم − إرجاع للموقع في الأذونات المرحّلة (عهدة سند الاستلام لا تمر بالموقع)
        var issued = (await _db.Set<ProjectMaterialIssueLine>().AsNoTracking()
                .Where(l => ids.Contains(l.ProductId) && l.Issue.Status == ProjectMaterialIssueStatuses.Posted
                    && (l.Issue.Kind == K.Issue || l.Issue.Kind == K.Return)
                    && (warehouseId == null || l.Issue.WarehouseId == warehouseId)
                    && (contractId == null || l.Issue.ContractId == contractId))
                .Select(l => new { l.ProductId, l.Issue.Kind, l.Quantity }).ToListAsync(ct))
            .GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.Sum(x => x.Kind == K.Return ? -x.Quantity : x.Quantity));

        var incoming = await IncomingAsync(ids, warehouseId, ct);

        return products.OrderBy(p => p.Sku).Select(p =>
        {
            var stock = onHand.GetValueOrDefault(p.Id);
            var reserved = reservations.Where(r => r.ProductId == p.Id).Sum(r => r.Q);
            var available = Math.Max(0, stock - reserved);
            decimal? own = contractId == null ? null : reservations.Where(r => r.ProductId == p.Id && r.ContractId == contractId).Sum(r => r.Q);
            return new StockAvailabilityDto
            {
                ProductId = p.Id, Sku = p.Sku, ItemName = p.NameAr, Unit = p.Unit,
                OnHand = R(stock), Reserved = R(reserved), Issued = R(issued.GetValueOrDefault(p.Id)), Available = R(available),
                Incoming = R(incoming.GetValueOrDefault(p.Id)),
                ReservedForProject = own is decimal o ? R(o) : null,
                AvailableForProject = own is decimal o2 ? R(Math.Max(0, Math.Min(stock, available + o2))) : null,
            };
        }).ToList();
    }

    /// <summary>
    /// قيد التوريد: المتبقي من طلبات الشراء المعتمدة غير المحوّلة (ناقص المستلم بسندات استلام المشروع)، وأسطر فواتير الشراء غير المرحّلة
    /// المحوّلة من الطلبات. الفاتورة المرحّلة صارت رصيداً فلا تتكرر الكمية.
    /// </summary>
    private async Task<Dictionary<Guid, decimal>> IncomingAsync(List<Guid> ids, Guid? warehouseId, CancellationToken ct)
    {
        var open = await _db.Set<MaterialRequisitionItem>().AsNoTracking()
            .Where(i => ids.Contains(i.ItemId) && i.Requisition.Status == "approved" && (warehouseId == null || i.Requisition.WarehouseId == warehouseId)
                // الطلب الذي عليه طلب عروض قائم يُحتسب من أوامر شرائه أدناه
                && !_db.Set<PurchaseRfq>().Any(f => f.RequisitionId == i.RequisitionId && f.Status != PurchaseRfqStatuses.Cancelled))
            .Select(i => new { i.RequisitionId, i.ItemId, Q = i.ApprovedQuantity ?? i.RequestedQuantity }).ToListAsync(ct);
        var reqIds = open.Select(x => (Guid?)x.RequisitionId).Distinct().ToList();
        var received = reqIds.Count == 0 ? new() : (await _db.Set<DeliveryNoteItem>().AsNoTracking()
                .Where(i => reqIds.Contains(i.DeliveryNote.RequisitionId) && i.DeliveryNote.Type == ProjectReceipts.NoteType && ids.Contains(i.ItemId))
                .Select(i => new { R = i.DeliveryNote.RequisitionId!.Value, i.ItemId, Q = i.DeliveredQty - i.ReturnedQty }).ToListAsync(ct))
            .GroupBy(x => (x.R, x.ItemId)).ToDictionary(g => g.Key, g => g.Sum(x => x.Q));
        var result = open.GroupBy(x => (x.RequisitionId, x.ItemId))
            .Select(g => new { g.Key.ItemId, Q = Math.Max(0, g.Sum(x => x.Q) - received.GetValueOrDefault((g.Key.RequisitionId, g.Key.ItemId))) })
            .GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Q));

        var ordered = await _db.Set<PurchaseInvoice>().AsNoTracking()
            .Where(inv => inv.ReferenceType == "material_requisition" && inv.Status != "posted" && inv.Status != "cancelled"
                && (warehouseId == null || inv.WarehouseId == warehouseId))
            .SelectMany(inv => inv.Items).Where(l => ids.Contains(l.ItemId)).Select(l => new { l.ItemId, l.Quantity }).ToListAsync(ct);
        foreach (var o in ordered) result[o.ItemId] = result.GetValueOrDefault(o.ItemId) + o.Quantity;

        // أوامر الشراء المؤكدة: المطلوب − ما وصل (سند فوتر فدخل الرصيد، أو استلام عهدة لمشروع)، صافي المرتجع
        var poLines = await _db.Set<CommercialOrderItem>().AsNoTracking()
            .Where(l => ids.Contains(l.ItemId) && l.Order.Type == "purchase_order" && l.Order.ConvertedInvoiceId == null
                && (l.Order.Status == "confirmed" || l.Order.Status == "partially_fulfilled")
                && (warehouseId == null || l.Order.WarehouseId == warehouseId))
            .Select(l => new { l.OrderId, l.ItemId, l.Quantity }).ToListAsync(ct);
        var poIds = poLines.Select(x => (Guid?)x.OrderId).Distinct().ToList();
        var arrived = poIds.Count == 0 ? new() : (await _db.Set<DeliveryNoteItem>().AsNoTracking()
                .Where(i => poIds.Contains(i.DeliveryNote.PurchaseOrderId) && ids.Contains(i.ItemId)
                    && (i.DeliveryNote.InvoiceId != null || i.DeliveryNote.RequisitionId != null))
                .Select(i => new { O = i.DeliveryNote.PurchaseOrderId!.Value, i.ItemId, Q = i.DeliveredQty - i.ReturnedQty }).ToListAsync(ct))
            .GroupBy(x => (x.O, x.ItemId)).ToDictionary(g => g.Key, g => g.Sum(x => x.Q));
        foreach (var g in poLines.GroupBy(x => (x.OrderId, x.ItemId)))
            result[g.Key.ItemId] = result.GetValueOrDefault(g.Key.ItemId) + Math.Max(0, g.Sum(x => x.Quantity) - arrived.GetValueOrDefault((g.Key.OrderId, g.Key.ItemId)));
        return result;
    }

    // ---------------- مساعدات ----------------

    private static decimal R(decimal v) => Math.Round(v, 4);
    private static string? Trim(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim()[..Math.Min(500, v.Trim().Length)];

    private async Task<CommercialContract> ContractAsync(Guid contractId, CancellationToken ct)
        => await _db.Set<CommercialContract>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == contractId, ct)
           ?? throw new NotFoundException(Messages.ContractNotFound);

    /// <summary>الحجز لمشروع قيد التنفيذ (ساري أو استلام ابتدائي)؛ الدراسة والمسودة والمغلق والملغى لا تحجز رصيداً.</summary>
    private static void EnsureExecuting(string stage)
    {
        if (stage is not (ContractStageDefinition.Active or ContractStageDefinition.InitialAcceptance))
            throw new ConflictException(Messages.StockReservationProjectNotExecuting);
    }

    private async Task<StockReservation> LoadAsync(Guid contractId, Guid reservationId, CancellationToken ct)
        => await _db.Set<StockReservation>().FirstOrDefaultAsync(r => r.Id == reservationId && r.ContractId == contractId, ct)
           ?? throw new NotFoundException(Messages.StockReservationNotFound);

    private async Task<StockReservationTransfer> PendingTransferAsync(Guid transferId, CancellationToken ct)
    {
        var transfer = await _db.Set<StockReservationTransfer>().FirstOrDefaultAsync(t => t.Id == transferId, ct)
            ?? throw new NotFoundException(Messages.StockReservationTransferNotFound);
        // المصدر في نطاق وصول المعتمِد (مرشّح العقود)
        if (!await _db.Set<CommercialContract>().AnyAsync(c => c.Id == transfer.SourceContractId, ct))
            throw new NotFoundException(Messages.StockReservationTransferNotFound);
        if (transfer.Status != StockReservationTransferStatuses.Pending) throw new ConflictException(Messages.StockReservationTransferNotPending);
        // فصل المهام: مقدّم الطلب لا يعتمده ولا يرفضه
        if (transfer.RequestedByUserId != null && transfer.RequestedByUserId == _user.UserId)
            throw new ForbiddenException(Messages.StockReservationTransferSelfDecision);
        return transfer;
    }

    private void Decide(StockReservationTransfer t, string status, string? note)
    {
        t.Status = status;
        t.DecidedByUserId = _user.UserId;
        t.DecidedBy = _user.Name;
        t.DecidedAt = SaudiTime.Now;
        t.DecisionNote = Trim(note);
    }

    private Task<decimal> PendingTransfersAsync(Guid reservationId, CancellationToken ct, Guid? exceptTransferId = null)
        => _db.Set<StockReservationTransfer>().AsNoTracking()
            .Where(t => t.SourceReservationId == reservationId && t.Status == StockReservationTransferStatuses.Pending && t.Id != exceptTransferId)
            .SumAsync(t => t.Quantity, ct);

    private async Task<string> ProductNameAsync(Guid productId, CancellationToken ct)
        => await _db.Set<Product>().AsNoTracking().Where(p => p.Id == productId).Select(p => p.NameAr).FirstOrDefaultAsync(ct) ?? "?";

    /// <summary>المتاح الحر في الموقع (الرصيد − كل الحجوزات النشطة) يغطي الحجز الجديد. يُستدعى تحت القفل.</summary>
    private async Task EnsureFreeAsync(Guid productId, string name, Guid warehouseId, decimal qty, CancellationToken ct)
    {
        var onHand = await WarehouseStocks.QuantityAsync(_db, productId, warehouseId, ct);
        var reserved = await StockReservations.ReservedAsync(_db, productId, warehouseId, null, ct);
        if (qty > onHand - reserved + Tolerance)
            throw new ValidationFailedException(string.Format(Messages.StockReservationExceedsAvailable, name, qty, Math.Max(0, onHand - reserved), onHand, reserved));
    }

    /// <summary>
    /// منع الحجز المكرر: المتبقي من حجوزات المشروع للصنف (كل المواقع) + المسلَّم الصافي له + الجديد ≤ المطلوب في الاحتياجات المعتمدة.
    /// </summary>
    private async Task EnsureWithinRequirementAsync(Guid contractId, Guid productId, string name, decimal qty, CancellationToken ct)
    {
        var plan = await _materials.GetAsync(contractId, ct);
        if (plan.Id == null || plan.Status != ProjectMaterialPlanStatuses.Approved)
            throw new ValidationFailedException(Messages.StockReservationPlanNotApproved);
        var required = plan.Lines.Where(l => l.ProductId == productId && l.ProductQuantity.HasValue).Sum(l => l.ProductQuantity!.Value);
        if (required <= 0) throw new ValidationFailedException(string.Format(Messages.StockReservationNotRequired, name));
        var reserved = await _db.Set<StockReservation>().AsNoTracking()
            .Where(r => r.ContractId == contractId && r.ProductId == productId && r.Status == StockReservationStatuses.Active)
            .SumAsync(r => (decimal?)(r.Quantity - r.IssuedQuantity - r.ReleasedQuantity), ct) ?? 0m;
        var delivered = (await _db.Set<ProjectMaterialIssueLine>().AsNoTracking()
                .Where(l => l.ProductId == productId && l.Issue.ContractId == contractId && l.Issue.Status == ProjectMaterialIssueStatuses.Posted)
                .Select(l => new { l.Issue.Kind, l.Quantity }).ToListAsync(ct))
            .Sum(x => K.IsDelivery(x.Kind) ? x.Quantity : x.Kind is K.Return or K.ReceiptReturn ? -x.Quantity : 0);
        var room = required - reserved - delivered;
        if (qty > room + Tolerance)
            throw new ValidationFailedException(string.Format(Messages.StockReservationExceedsRequirement, name, qty, Math.Max(0, room), required, reserved, delivered));
    }

    private async Task<List<StockReservationDto>> ToDtosAsync(List<StockReservation> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return new();
        var productIds = rows.Select(r => r.ProductId).Distinct().ToList();
        var warehouseIds = rows.Select(r => r.WarehouseId).Distinct().ToList();
        var contractIds = rows.Select(r => r.ContractId).Distinct().ToList();
        var reservationIds = rows.Select(r => r.Id).ToList();
        var products = await _db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => new { p.Sku, p.NameAr, p.Unit }, ct);
        var warehouses = await _db.Set<Warehouse>().AsNoTracking().Where(w => warehouseIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.NameAr, ct);
        var contracts = await _db.Set<CommercialContract>().AsNoTracking()
            .Where(c => contractIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new { c.ContractNumber, c.ProjectName }, ct);
        var pending = (await _db.Set<StockReservationTransfer>().AsNoTracking()
                .Where(t => reservationIds.Contains(t.SourceReservationId) && t.Status == StockReservationTransferStatuses.Pending)
                .Select(t => new { t.SourceReservationId, t.Quantity }).ToListAsync(ct))
            .GroupBy(t => t.SourceReservationId).ToDictionary(g => g.Key, g => g.Sum(t => t.Quantity));
        return rows.Select(r =>
        {
            var p = products.GetValueOrDefault(r.ProductId);
            var c = contracts.GetValueOrDefault(r.ContractId);
            return new StockReservationDto
            {
                Id = r.Id, ContractId = r.ContractId, ContractNumber = c?.ContractNumber, ProjectName = c?.ProjectName,
                ProductId = r.ProductId, Sku = p?.Sku ?? "", ItemName = p?.NameAr ?? "", Unit = p?.Unit,
                WarehouseId = r.WarehouseId, WarehouseName = warehouses.GetValueOrDefault(r.WarehouseId),
                Quantity = r.Quantity, IssuedQuantity = r.IssuedQuantity, ReleasedQuantity = r.ReleasedQuantity,
                RemainingQuantity = R(StockReservations.Remaining(r)), Status = r.Status, Source = r.Source,
                CreatedBy = r.CreatedBy, CreatedAt = r.CreatedAt, Notes = r.Notes, PendingTransferQuantity = pending.GetValueOrDefault(r.Id),
            };
        }).ToList();
    }

    private async Task<List<StockReservationTransferDto>> TransferDtosAsync(List<StockReservationTransfer> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return new();
        var productIds = rows.Select(r => r.ProductId).Distinct().ToList();
        var warehouseIds = rows.Select(r => r.WarehouseId).Distinct().ToList();
        var contractIds = rows.SelectMany(r => new[] { r.SourceContractId, r.TargetContractId }).Distinct().ToList();
        var products = await _db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => new { p.NameAr, p.Unit }, ct);
        var warehouses = await _db.Set<Warehouse>().AsNoTracking().Where(w => warehouseIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.NameAr, ct);
        // الأرقام للعرض في نطاق المستخدم: عقد خارج نطاقه يظهر بلا رقم
        var numbers = await _db.Set<CommercialContract>().AsNoTracking()
            .Where(c => contractIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.ContractNumber, ct);
        return rows.Select(t => new StockReservationTransferDto
        {
            Id = t.Id, SourceReservationId = t.SourceReservationId, SourceContractId = t.SourceContractId,
            SourceContractNumber = numbers.GetValueOrDefault(t.SourceContractId), TargetContractId = t.TargetContractId,
            TargetContractNumber = numbers.GetValueOrDefault(t.TargetContractId), ProductId = t.ProductId,
            ItemName = products.GetValueOrDefault(t.ProductId)?.NameAr ?? "", Unit = products.GetValueOrDefault(t.ProductId)?.Unit,
            WarehouseId = t.WarehouseId, WarehouseName = warehouses.GetValueOrDefault(t.WarehouseId), Quantity = t.Quantity, Reason = t.Reason,
            Status = t.Status, RequestedBy = t.RequestedBy, CreatedAt = t.CreatedAt, DecidedBy = t.DecidedBy, DecidedAt = t.DecidedAt,
            DecisionNote = t.DecisionNote,
        }).ToList();
    }
}
