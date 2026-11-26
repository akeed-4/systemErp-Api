using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// متابعة توريد المشتريات وتقاريرها. الصلاحيات مستقلة (OptIn): <c>purchase-receipt-tracking</c> (عرض، فتح متابعة = إضافة، الموعد
/// والملاحظات = تعديل، رفض، إلغاء) و<c>purchase-receipt-reports</c> (عرض). الكميات من <see cref="PurchaseReceiptQuantities"/>، وحالة
/// «متأخر» محسوبة من موعد التوريد. لا حركة مخزون ولا قيد ولا فاتورة.
/// </summary>
public class PurchaseReceiptTrackingService : IPurchaseReceiptTrackingService
{
    public const string ScreenId = PermissionCatalog.PurchaseReceiptTrackingScreenId;
    public const string ReportsScreenId = PermissionCatalog.PurchaseReceiptReportsScreenId;
    private const int ReportLimit = 5000;

    private readonly ErpDbContext _db;
    private readonly ITransactionRunner _tx;
    private readonly IPermissionService _permissions;
    private readonly ICurrentUser _user;

    public PurchaseReceiptTrackingService(ErpDbContext db, ITransactionRunner tx, IPermissionService permissions, ICurrentUser user)
    {
        _db = db; _tx = tx; _permissions = permissions; _user = user;
    }

    private async Task RequireAsync(string screen, ScreenAction action, CancellationToken ct)
    {
        if (!await _permissions.HasPermissionAsync(screen, action, ct)) throw new ForbiddenException();
    }

    // ---------------- القائمة ----------------

    /// <summary>فلاتر المتابعة (الحالة المعروضة: «متأخر» = مفتوحة تجاوزت الموعد، و«معلّق/جزئي» دون المتأخر منها).</summary>
    private IQueryable<PurchaseSupplyTracking> Filter(PurchaseSupplyTrackingQueryDto q)
    {
        var today = SaudiTime.Today;
        var query = _db.Set<PurchaseSupplyTracking>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q.SourceType)) query = query.Where(t => t.SourceType == q.SourceType);
        if (q.SupplierId is Guid supplier) query = query.Where(t => t.SupplierId == supplier);
        if (q.WarehouseId is Guid warehouse) query = query.Where(t => t.WarehouseId == warehouse);
        if (q.From is DateTime from) query = query.Where(t => t.SourceDate >= from.Date);
        if (q.To is DateTime to) query = query.Where(t => t.SourceDate < to.Date.AddDays(1));
        var open = new[] { PurchaseSupplyStatuses.Pending, PurchaseSupplyStatuses.PartiallyReceived };
        switch (q.Status)
        {
            case null or "": break;
            case PurchaseSupplyStatuses.Delayed:
                query = query.Where(t => open.Contains(t.Status) && t.ExpectedDate != null && t.ExpectedDate < today);
                break;
            case PurchaseSupplyStatuses.Pending or PurchaseSupplyStatuses.PartiallyReceived:
                query = query.Where(t => t.Status == q.Status && (t.ExpectedDate == null || t.ExpectedDate >= today));
                break;
            default:
                query = query.Where(t => t.Status == q.Status);
                break;
        }
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim();
            query = query.Where(t => t.SourceNumber.Contains(s) || t.SupplierName.Contains(s)
                || _db.Set<DeliveryNote>().Any(n => n.DeliveryNumber.Contains(s)
                    && ((t.SourceType == PurchaseSupplySourceTypes.PurchaseInvoice && n.SourceType == DeliveryNoteSources.PurchaseInvoice && n.SourceId == t.SourceId)
                        || (t.SourceType == PurchaseSupplySourceTypes.Contract && n.Type == "purchase_delivery" && n.ContractId == t.SourceId))));
        }
        return query;
    }

    public async Task<PagedResult<PurchaseSupplyTrackingDto>> ListAsync(PurchaseSupplyTrackingQueryDto q, CancellationToken ct = default)
    {
        await RequireAsync(ScreenId, ScreenAction.View, ct);
        var query = Filter(q);
        var size = Math.Clamp(q.PageSize, 1, 200);
        var page = Math.Max(1, q.Page);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(t => t.SourceDate).ThenByDescending(t => t.CreatedAt).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<PurchaseSupplyTrackingDto> { Items = await MapAsync(rows, ct), TotalCount = total, PageNumber = page, PageSize = size };
    }

    private async Task<List<PurchaseSupplyTrackingDto>> MapAsync(IReadOnlyCollection<PurchaseSupplyTracking> rows, CancellationToken ct)
    {
        var lines = await PurchaseSupplyTracker.LinesForAsync(_db, rows, ct);
        var receipts = await ReceiptHeadersAsync(rows, ct);
        return rows.Select(t => ToDto<PurchaseSupplyTrackingDto>(t, lines.GetValueOrDefault(t.Id) ?? new(),
            receipts.Where(r => r.Key == (t.SourceType, t.SourceId)).Select(r => r.Number).ToList())).ToList();
    }

    /// <summary>أرقام سندات الاستلام النشطة لكل مصدر.</summary>
    private async Task<List<((string, Guid) Key, string Number)>> ReceiptHeadersAsync(IReadOnlyCollection<PurchaseSupplyTracking> rows, CancellationToken ct)
    {
        var invoiceIds = rows.Where(t => t.SourceType == PurchaseSupplySourceTypes.PurchaseInvoice).Select(t => t.SourceId).Distinct().ToList();
        var contractIds = rows.Where(t => t.SourceType == PurchaseSupplySourceTypes.Contract).Select(t => t.SourceId).Distinct().ToList();
        var notes = await ReceiptsQuery(invoiceIds, contractIds)
            .Select(n => new { n.SourceType, n.SourceId, n.ContractId, n.DeliveryNumber, n.Date }).OrderBy(n => n.Date).ToListAsync(ct);
        return notes.Select(n => (n.SourceType == DeliveryNoteSources.PurchaseInvoice
            ? (PurchaseSupplySourceTypes.PurchaseInvoice, n.SourceId!.Value) : (PurchaseSupplySourceTypes.Contract, n.ContractId!.Value), n.DeliveryNumber)).ToList();
    }

    /// <summary>سندات الاستلام النشطة لمصادر المتابعة: سندات الفاتورة بمصدرها، وسندات الاتفاقية بعقدها (لا أمر شراء ولا طلب مشروع).</summary>
    private IQueryable<DeliveryNote> ReceiptsQuery(List<Guid> invoiceIds, List<Guid> contractIds)
        => _db.Set<DeliveryNote>().AsNoTracking()
            .Where(n => n.Status != DeliveryNoteStatus.Draft && n.Status != DeliveryNoteStatus.Cancelled)
            .Where(n => (n.SourceType == DeliveryNoteSources.PurchaseInvoice && n.SourceId != null && invoiceIds.Contains(n.SourceId.Value))
                || (n.Type == "purchase_delivery" && n.ContractId != null && contractIds.Contains(n.ContractId.Value)
                    && n.RequisitionId == null && n.PurchaseOrderId == null));

    private static T ToDto<T>(PurchaseSupplyTracking t, List<PurchaseReceiptSourceLineDto> lines, List<string> receiptNumbers) where T : PurchaseSupplyTrackingDto, new()
    {
        var today = SaudiTime.Today;
        var open = t.Status is PurchaseSupplyStatuses.Pending or PurchaseSupplyStatuses.PartiallyReceived;
        var delayed = open && t.ExpectedDate is DateTime due && due.Date < today;
        return new T
        {
            Id = t.Id, SourceType = t.SourceType, SourceId = t.SourceId, SourceNumber = t.SourceNumber, SourceDate = t.SourceDate,
            SupplierId = t.SupplierId, SupplierName = t.SupplierName, WarehouseId = t.WarehouseId, ExpectedDate = t.ExpectedDate,
            StoredStatus = t.Status, Status = delayed ? PurchaseSupplyStatuses.Delayed : t.Status, IsDelayed = delayed,
            DelayDays = delayed ? (int)(today - t.ExpectedDate!.Value.Date).TotalDays : 0,
            LastReceiptDate = t.LastReceiptDate, Notes = t.Notes, CreatedAt = t.CreatedAt,
            RejectedAt = t.RejectedAt, RejectedByName = t.RejectedByName, RejectionReason = t.RejectionReason,
            CancelledAt = t.CancelledAt, CancelledByName = t.CancelledByName, CancellationReason = t.CancellationReason,
            OrderedQty = lines.Where(l => l.Receivable).Sum(l => l.OrderedQty), ReceivedQty = lines.Sum(l => l.ReceivedQty),
            AcceptedQty = lines.Sum(l => l.AcceptedQty), RejectedQty = lines.Sum(l => l.RejectedQty),
            OperationalReturnedQty = lines.Sum(l => l.OperationalReturnedQty), FinancialReturnedQty = lines.Sum(l => l.FinancialReturnedQty),
            AwaitingFinancialReturnQty = lines.Sum(l => l.AwaitingFinancialReturnQty), RemainingQty = lines.Sum(l => l.RemainingQty),
            InvoicedQty = lines.Where(l => l.Receivable).Sum(l => l.InvoicedQty),
            ReceiptsCount = receiptNumbers.Count, ReceiptNumbers = receiptNumbers,
        };
    }

    // ---------------- التفاصيل ----------------

    public async Task<PurchaseSupplyTrackingDetailsDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        await RequireAsync(ScreenId, ScreenAction.View, ct);
        return await DetailsAsync(id, ct);
    }

    private async Task<PurchaseSupplyTrackingDetailsDto> DetailsAsync(Guid id, CancellationToken ct)
    {
        var t = await _db.Set<PurchaseSupplyTracking>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(Messages.PurchaseSupplyTrackingNotFound);
        var lines = await PurchaseSupplyTracker.LinesAsync(_db, t, ct);
        var invoiceIds = t.SourceType == PurchaseSupplySourceTypes.PurchaseInvoice ? new List<Guid> { t.SourceId } : new();
        var contractIds = t.SourceType == PurchaseSupplySourceTypes.Contract ? new List<Guid> { t.SourceId } : new();
        var notes = await ReceiptsQuery(invoiceIds, contractIds).Include(n => n.Items).OrderBy(n => n.Date).ThenBy(n => n.CreatedAt).ToListAsync(ct);
        var noteIds = notes.Select(n => n.Id).ToList();
        var returns = await _db.Set<DeliveryReturnNote>().AsNoTracking().Where(r => noteIds.Contains(r.DeliveryNoteId))
            .Select(r => new { r.DeliveryNoteId, r.ReturnNumber }).ToListAsync(ct);
        var dto = ToDto<PurchaseSupplyTrackingDetailsDto>(t, lines, notes.Select(n => n.DeliveryNumber).ToList());
        dto.Lines = lines;
        dto.Receipts = notes.Select(n => new PurchaseReceiptSummaryDto
        {
            Id = n.Id, DeliveryNumber = n.DeliveryNumber, Date = n.Date, Status = n.Status.ToString(), WarehouseId = n.WarehouseId,
            InvoiceNumber = n.InvoiceNumber, Notes = n.Notes,
            ReceivedQty = n.Items.Sum(i => i.ReceivedQty ?? i.DeliveredQty), AcceptedQty = n.Items.Sum(i => i.DeliveredQty),
            RejectedQty = n.Items.Sum(i => i.RejectedQty), ReturnedQty = n.Items.Sum(i => i.ReturnedQty),
            Items = n.Items.Select(Mapper.Map<DeliveryNoteItemDto>).ToList(),
            ReturnNumbers = returns.Where(r => r.DeliveryNoteId == n.Id).Select(r => r.ReturnNumber).ToList(),
        }).ToList();
        dto.Events = await EventsQueryAsync(id, ct);
        return dto;
    }

    public async Task<List<PurchaseSupplyEventDto>> GetEventsAsync(Guid id, CancellationToken ct = default)
    {
        await RequireAsync(ScreenId, ScreenAction.View, ct);
        if (!await _db.Set<PurchaseSupplyTracking>().AnyAsync(t => t.Id == id, ct)) throw new NotFoundException(Messages.PurchaseSupplyTrackingNotFound);
        return await EventsQueryAsync(id, ct);
    }

    private Task<List<PurchaseSupplyEventDto>> EventsQueryAsync(Guid id, CancellationToken ct)
        => _db.Set<PurchaseSupplyEvent>().AsNoTracking().Where(e => e.TrackingId == id).OrderBy(e => e.OccurredAt).ThenBy(e => e.CreatedAt)
            .Select(e => new PurchaseSupplyEventDto
            {
                Id = e.Id, DeliveryNoteId = e.DeliveryNoteId, DocumentNumber = e.DocumentNumber, EventType = e.EventType,
                FromStatus = e.FromStatus, ToStatus = e.ToStatus, Notes = e.Notes, UserName = e.UserName, OccurredAt = e.OccurredAt,
            }).ToListAsync(ct);

    // ---------------- الإجراءات ----------------

    private static string LockScopeOf(string sourceType) => sourceType == PurchaseSupplySourceTypes.PurchaseInvoice ? DeliverySources.PurchaseInvoiceLockScope : "contract-delivery";

    public Task<PurchaseSupplyTrackingDetailsDto> OpenAsync(OpenPurchaseSupplyTrackingDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            await RequireAsync(ScreenId, ScreenAction.Create, token);
            if (!PurchaseSupplySourceTypes.IsValid(r.SourceType) || r.SourceId == Guid.Empty) throw new ValidationFailedException(Messages.PurchaseSupplySourceInvalid);
            // قفل المصدر نفسه الذي يأخذه سند الاستلام: متابعة نشطة واحدة لكل مصدر
            await DeliverySources.LockAsync(_db, LockScopeOf(r.SourceType), r.SourceId, token);
            var reopened = !await PurchaseSupplyTracker.ActiveQuery(_db, r.SourceType, r.SourceId).AnyAsync(token)
                && await _db.Set<PurchaseSupplyTracking>().AnyAsync(t => t.SourceType == r.SourceType && t.SourceId == r.SourceId, token);
            var tracking = (await PurchaseSupplyTracker.ForSourceAsync(_db, _user, r.SourceType, r.SourceId, true, r.ExpectedDate, r.Notes, token))!;
            if (reopened) PurchaseSupplyTracker.AddEvent(_db, tracking, _user, "reopened", null, tracking.Status);
            await _db.SaveChangesAsync(token);
            return await DetailsAsync(tracking.Id, token);
        }, ct);

    public Task<PurchaseSupplyTrackingDetailsDto> UpdateAsync(Guid id, UpdatePurchaseSupplyTrackingDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            await RequireAsync(ScreenId, ScreenAction.Edit, token);
            var t = await LoadLockedAsync(id, token);
            if (PurchaseSupplyStatuses.IsClosedManually(t.Status)) throw new ConflictException(Messages.PurchaseSupplyAlreadyClosed);
            var notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();
            var changes = new List<string>();
            if (t.ExpectedDate?.Date != r.ExpectedDate?.Date)
                changes.Add($"موعد التوريد: {t.ExpectedDate:yyyy-MM-dd} ← {r.ExpectedDate:yyyy-MM-dd}");
            if (t.Notes != notes) changes.Add("الملاحظات");
            t.ExpectedDate = r.ExpectedDate?.Date; t.Notes = notes;
            if (changes.Count > 0) PurchaseSupplyTracker.AddEvent(_db, t, _user, "updated", t.Status, t.Status, string.Join("، ", changes));
            await _db.SaveChangesAsync(token);
            return await DetailsAsync(id, token);
        }, ct);

    public Task<PurchaseSupplyTrackingDetailsDto> RejectAsync(Guid id, ClosePurchaseSupplyTrackingDto r, CancellationToken ct = default)
        => CloseAsync(id, r, ScreenAction.Reject, PurchaseSupplyStatuses.Rejected, ct);

    public Task<PurchaseSupplyTrackingDetailsDto> CancelAsync(Guid id, ClosePurchaseSupplyTrackingDto r, CancellationToken ct = default)
        => CloseAsync(id, r, ScreenAction.Cancel, PurchaseSupplyStatuses.Cancelled, ct);

    /// <summary>الرفض والإلغاء: بسبب إلزامي ويُسجَّل المستخدم والتاريخ؛ يغلقان المتابعة فلا سند جديد من المصدر حتى يُعاد فتحها. السندات القائمة كما هي.</summary>
    private Task<PurchaseSupplyTrackingDetailsDto> CloseAsync(Guid id, ClosePurchaseSupplyTrackingDto r, ScreenAction action, string status, CancellationToken ct)
        => _tx.RunAsync(async token =>
        {
            await RequireAsync(ScreenId, action, token);
            var reason = r.Reason?.Trim();
            if (string.IsNullOrEmpty(reason)) throw new ValidationFailedException(Messages.PurchaseSupplyReasonRequired);
            var t = await LoadLockedAsync(id, token);
            if (PurchaseSupplyStatuses.IsClosedManually(t.Status)) throw new ConflictException(Messages.PurchaseSupplyAlreadyClosed);
            if (t.Status == PurchaseSupplyStatuses.Received) throw new ConflictException(Messages.PurchaseSupplyAlreadyReceived);
            var from = t.Status;
            var by = _user.Name ?? _user.Email;
            if (status == PurchaseSupplyStatuses.Rejected)
                (t.RejectedAt, t.RejectedByUserId, t.RejectedByName, t.RejectionReason) = (DateTime.UtcNow, _user.UserId, by, reason);
            else
                (t.CancelledAt, t.CancelledByUserId, t.CancelledByName, t.CancellationReason) = (DateTime.UtcNow, _user.UserId, by, reason);
            t.Status = status;
            PurchaseSupplyTracker.AddEvent(_db, t, _user, status, from, status, reason);
            await _db.SaveChangesAsync(token);
            return await DetailsAsync(id, token);
        }, ct);

    /// <summary>المتابعة بقفل مصدرها (إجراء متزامن مع سند استلام على المصدر نفسه يُنفَّذ بالتتابع).</summary>
    private async Task<PurchaseSupplyTracking> LoadLockedAsync(Guid id, CancellationToken ct)
    {
        var head = await _db.Set<PurchaseSupplyTracking>().AsNoTracking().Where(t => t.Id == id).Select(t => new { t.SourceType, t.SourceId }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.PurchaseSupplyTrackingNotFound);
        await DeliverySources.LockAsync(_db, LockScopeOf(head.SourceType), head.SourceId, ct);
        return await _db.Set<PurchaseSupplyTracking>().FirstAsync(t => t.Id == id, ct);
    }

    // ---------------- المصادر ----------------

    public async Task<PurchaseReceiptSourceDto> GetPurchaseInvoiceSourceAsync(Guid invoiceId, CancellationToken ct = default)
    {
        await RequireAsync(ScreenId, ScreenAction.View, ct);
        var invoice = await DeliverySources.PurchaseInvoiceAsync(_db, invoiceId, ct);
        var dto = await DeliverySources.PurchaseInvoiceSourceAsync(_db, invoice, null, ct);
        await AttachTrackingAsync(dto, PurchaseSupplySourceTypes.PurchaseInvoice, ct);
        return dto;
    }

    public async Task<PurchaseReceiptSourceDto> GetContractSourceAsync(Guid contractId, CancellationToken ct = default)
    {
        await RequireAsync(ScreenId, ScreenAction.View, ct);
        var contract = await PurchaseSupplyTracker.PurchaseAgreementAsync(_db, contractId, ct);
        var lines = (await PurchaseReceiptQuantities.ForContractsAsync(_db, new[] { contract }, ct))[contract.Id];
        var dto = new PurchaseReceiptSourceDto
        {
            SourceType = PurchaseSupplySourceTypes.Contract, SourceId = contract.Id, SourceNumber = contract.ContractNumber,
            SourceDate = contract.StartDate ?? contract.CreatedAt, PartyId = contract.PartyId, PartyName = contract.PartyName, PartyPhone = contract.PartyPhone,
            WarehouseId = contract.WarehouseId, CostCenterId = contract.CostCenterId, Lines = lines,
        };
        await AttachTrackingAsync(dto, PurchaseSupplySourceTypes.Contract, ct);
        return dto;
    }

    private async Task AttachTrackingAsync(PurchaseReceiptSourceDto dto, string type, CancellationToken ct)
    {
        var t = await PurchaseSupplyTracker.ActiveQuery(_db, type, dto.SourceId).AsNoTracking().Select(x => new { x.Id, x.Status }).FirstOrDefaultAsync(ct);
        dto.TrackingId = t?.Id; dto.TrackingStatus = t?.Status;
    }

    // ---------------- التقارير ----------------

    public async Task<PurchaseReceiptReportDto> ReportAsync(PurchaseSupplyTrackingQueryDto q, CancellationToken ct = default)
    {
        await RequireAsync(ReportsScreenId, ScreenAction.View, ct);
        var trackings = await Filter(q).OrderByDescending(t => t.SourceDate).Take(ReportLimit).ToListAsync(ct);
        var lines = await PurchaseSupplyTracker.LinesForAsync(_db, trackings, ct);
        var receiptNumbers = await ReceiptHeadersAsync(trackings, ct);
        var rows = trackings.Select(t => ToDto<PurchaseSupplyTrackingDto>(t, lines.GetValueOrDefault(t.Id) ?? new(),
            receiptNumbers.Where(r => r.Key == (t.SourceType, t.SourceId)).Select(r => r.Number).ToList())).ToList();
        var report = new PurchaseReceiptReportDto
        {
            From = q.From, To = q.To,
            Totals = new PurchaseReceiptReportTotalsDto
            {
                Trackings = rows.Count, Delayed = rows.Count(r => r.IsDelayed),
                OrderedQty = rows.Sum(r => r.OrderedQty), ReceivedQty = rows.Sum(r => r.ReceivedQty), AcceptedQty = rows.Sum(r => r.AcceptedQty),
                RejectedQty = rows.Sum(r => r.RejectedQty), OperationalReturnedQty = rows.Sum(r => r.OperationalReturnedQty),
                FinancialReturnedQty = rows.Sum(r => r.FinancialReturnedQty), AwaitingFinancialReturnQty = rows.Sum(r => r.AwaitingFinancialReturnQty),
                RemainingQty = rows.Sum(r => r.RemainingQty),
            },
            Suppliers = rows.GroupBy(r => (r.SupplierId, r.SupplierName)).Select(g =>
            {
                var ordered = g.Sum(r => r.OrderedQty);
                var net = g.Sum(r => r.AcceptedQty - r.OperationalReturnedQty);
                return new PurchaseReceiptSupplierRowDto
                {
                    SupplierId = g.Key.SupplierId, SupplierName = g.Key.SupplierName, Trackings = g.Count(),
                    Received = g.Count(r => r.StoredStatus == PurchaseSupplyStatuses.Received),
                    Partial = g.Count(r => r.StoredStatus == PurchaseSupplyStatuses.PartiallyReceived), Delayed = g.Count(r => r.IsDelayed),
                    OrderedQty = ordered, ReceivedQty = g.Sum(r => r.ReceivedQty), RejectedQty = g.Sum(r => r.RejectedQty), RemainingQty = g.Sum(r => r.RemainingQty),
                    FulfillmentPercent = ordered > 0 ? Math.Round(net / ordered * 100m, 1) : 0,
                };
            }).OrderByDescending(s => s.Trackings).ToList(),
            Partial = rows.Where(r => r.StoredStatus == PurchaseSupplyStatuses.PartiallyReceived).ToList(),
            Delayed = rows.Where(r => r.IsDelayed).OrderByDescending(r => r.DelayDays).ToList(),
            Items = lines.Values.SelectMany(l => l).Where(l => l.Receivable).GroupBy(l => (l.ItemId, l.Unit)).Select(g => new PurchaseReceiptItemRowDto
            {
                ItemId = g.Key.ItemId, ItemName = g.First().ItemName, Unit = g.Key.Unit,
                OrderedQty = g.Sum(l => l.OrderedQty), ReceivedQty = g.Sum(l => l.ReceivedQty), AcceptedQty = g.Sum(l => l.AcceptedQty),
                RejectedQty = g.Sum(l => l.RejectedQty), OperationalReturnedQty = g.Sum(l => l.OperationalReturnedQty),
                FinancialReturnedQty = g.Sum(l => l.FinancialReturnedQty), RemainingQty = g.Sum(l => l.RemainingQty),
            }).OrderBy(i => i.ItemName).ToList(),
        };

        // المرفوض ومرتجعات الاستلام من سندات مصادر التقرير
        var invoiceIds = trackings.Where(t => t.SourceType == PurchaseSupplySourceTypes.PurchaseInvoice).Select(t => t.SourceId).Distinct().ToList();
        var contractIds = trackings.Where(t => t.SourceType == PurchaseSupplySourceTypes.Contract).Select(t => t.SourceId).Distinct().ToList();
        var notes = await ReceiptsQuery(invoiceIds, contractIds).Include(n => n.Items).ToListAsync(ct);
        var trackingOf = trackings.GroupBy(t => (t.SourceType, t.SourceId)).ToDictionary(g => g.Key, g => g.First());
        PurchaseSupplyTracking? Of(DeliveryNote n) => trackingOf.GetValueOrDefault(n.SourceType == DeliveryNoteSources.PurchaseInvoice
            ? (PurchaseSupplySourceTypes.PurchaseInvoice, n.SourceId!.Value) : (PurchaseSupplySourceTypes.Contract, n.ContractId!.Value));
        var lineState = lines.Values.SelectMany(l => l).GroupBy(l => l.SourceLineId).ToDictionary(g => g.Key, g => g.First());
        report.Rejected = notes.SelectMany(n => n.Items.Where(i => i.RejectedQty > 0).Select(i =>
        {
            var t = Of(n);
            var invoicePath = n.SourceType == DeliveryNoteSources.PurchaseInvoice;
            var awaiting = invoicePath && i.SourceLineId is Guid lid && lineState.GetValueOrDefault(lid)?.AwaitingFinancialReturnQty > DeliverySources.Tolerance;
            return new PurchaseReceiptRejectedRowDto
            {
                DeliveryNoteId = n.Id, DeliveryNumber = n.DeliveryNumber, Date = n.Date, SourceType = t?.SourceType ?? "", SourceNumber = t?.SourceNumber ?? "",
                SupplierName = n.PartyName, ItemId = i.ItemId, ItemName = i.ItemName, Unit = i.Unit, RejectedQty = i.RejectedQty, RejectionReason = i.RejectionReason,
                State = !invoicePath ? "returned_to_remaining" : awaiting ? "awaiting_financial_return" : "financially_returned",
            };
        })).OrderByDescending(r => r.Date).ToList();
        var noteIds = notes.Select(n => n.Id).ToList();
        var byId = notes.ToDictionary(n => n.Id);
        var returns = await _db.Set<DeliveryReturnNote>().AsNoTracking().Include(r => r.Items).Where(r => noteIds.Contains(r.DeliveryNoteId))
            .OrderByDescending(r => r.Date).ToListAsync(ct);
        report.Returns = returns.Select(r =>
        {
            var t = Of(byId[r.DeliveryNoteId]);
            return new PurchaseReceiptReturnRowDto
            {
                Id = r.Id, ReturnNumber = r.ReturnNumber, Date = r.Date, DeliveryNumber = r.DeliveryNumber, SourceType = t?.SourceType ?? "",
                SourceNumber = t?.SourceNumber ?? "", SupplierName = r.PartyName, Reason = r.ReturnReason,
                Quantity = r.Items.Sum(i => i.Quantity), Total = r.GrandTotal,
            };
        }).ToList();
        return report;
    }
}
