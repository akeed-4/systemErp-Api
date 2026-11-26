using ERP.Core.Contracts.Shared;
using ERP.Service.Data;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// ربط سندات الاستلام بمتابعة توريد مصدرها: إنشاء المتابعة (فاتورة المشتريات تلقائياً مع أول سند، والاتفاقية حين تُفتح متابعتها)،
/// وتحديث حالتها من الكميات بعد كل عملية على سنداتها، وسجل الأحداث. لا يغيّر قواعد الاستلام ولا الفوترة.
/// </summary>
internal static class PurchaseSupplyTracker
{
    public static IQueryable<PurchaseSupplyTracking> ActiveQuery(ErpDbContext db, string sourceType, Guid sourceId)
        => db.Set<PurchaseSupplyTracking>().Where(t => t.SourceType == sourceType && t.SourceId == sourceId
            && t.Status != PurchaseSupplyStatuses.Cancelled && t.Status != PurchaseSupplyStatuses.Rejected);

    /// <summary>مصدر المتابعة لسند استلام: فاتورة المشتريات، أو الاتفاقية لسند اتفاقية (لا أمر شراء ولا طلب مشروع)؛ وإلا لا متابعة.</summary>
    public static (string Type, Guid Id)? SourceOf(DeliveryNote note)
    {
        if (note.SourceType == DeliveryNoteSources.PurchaseInvoice && note.SourceId is Guid invoiceId) return (PurchaseSupplySourceTypes.PurchaseInvoice, invoiceId);
        if (note.Type == "purchase_delivery" && note.ContractId is Guid contractId && note.RequisitionId == null && note.PurchaseOrderId == null)
            return (PurchaseSupplySourceTypes.Contract, contractId);
        return null;
    }

    /// <summary>قبل سند جديد من المصدر: المتابعة المرفوضة أو الملغاة (بلا متابعة نشطة) تمنع الاستلام حتى يُعاد فتحها.</summary>
    public static async Task EnsureOpenForReceiptAsync(ErpDbContext db, string sourceType, Guid sourceId, string sourceNumber, CancellationToken ct)
    {
        if (await ActiveQuery(db, sourceType, sourceId).AnyAsync(ct)) return;
        if (await db.Set<PurchaseSupplyTracking>().AnyAsync(t => t.SourceType == sourceType && t.SourceId == sourceId, ct))
            throw new ConflictException(string.Format(Messages.PurchaseSupplyClosed, sourceNumber));
    }

    /// <summary>بيانات رأس المصدر بعد التحقق منه: فاتورة مشتريات صالحة للاستلام، أو اتفاقية شراء سارية من مورد (لا جدول كميات).</summary>
    public static async Task<(string Number, DateTime Date, Guid? SupplierId, string SupplierName, Guid? WarehouseId)> SourceHeaderAsync(
        ErpDbContext db, string sourceType, Guid sourceId, CancellationToken ct)
    {
        if (sourceType == PurchaseSupplySourceTypes.PurchaseInvoice)
        {
            var invoice = await DeliverySources.PurchaseInvoiceAsync(db, sourceId, ct);
            return (invoice.InvoiceNumber, invoice.IssueDate, invoice.PartyId, invoice.PartyName, invoice.WarehouseId);
        }
        if (sourceType != PurchaseSupplySourceTypes.Contract) throw new ValidationFailedException(Messages.PurchaseSupplySourceInvalid);
        var contract = await PurchaseAgreementAsync(db, sourceId, ct);
        return (contract.ContractNumber, contract.StartDate ?? contract.CreatedAt, contract.PartyId, contract.PartyName, contract.WarehouseId);
    }

    /// <summary>اتفاقية شراء من مورد، سارية، وليست عقد جدول كميات (له مستخلصاته).</summary>
    public static async Task<CommercialContract> PurchaseAgreementAsync(ErpDbContext db, Guid contractId, CancellationToken ct)
    {
        var contract = await db.Set<CommercialContract>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == contractId, ct)
            ?? throw new NotFoundException(Messages.ContractNotFoundDot);
        if (!contract.IsSupplierParty || contract.Stage != ContractStageDefinition.Active
            || await CommercialContractService.IsBoqContractAsync(db, contract.ContractType, ct))
            throw new ConflictException(Messages.PurchaseSupplySourceInvalid);
        return contract;
    }

    /// <summary>المتابعة النشطة للمصدر، وتُنشأ إن طُلب (بحدث opened).</summary>
    public static async Task<PurchaseSupplyTracking?> ForSourceAsync(ErpDbContext db, ICurrentUser user, string sourceType, Guid sourceId, bool create,
        DateTime? expectedDate, string? notes, CancellationToken ct)
    {
        var tracking = await ActiveQuery(db, sourceType, sourceId).FirstOrDefaultAsync(ct);
        if (tracking != null || !create) return tracking;
        var head = await SourceHeaderAsync(db, sourceType, sourceId, ct);
        tracking = new PurchaseSupplyTracking
        {
            SourceType = sourceType, SourceId = sourceId, SourceNumber = head.Number, SourceDate = head.Date.Date,
            SupplierId = head.SupplierId, SupplierName = head.SupplierName, WarehouseId = head.WarehouseId,
            ExpectedDate = expectedDate?.Date, Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            Status = PurchaseSupplyStatuses.Pending, CreatedByUserId = user.UserId,
        };
        db.Add(tracking);
        await db.SaveChangesAsync(ct);
        tracking.Status = PurchaseReceiptQuantities.StatusOf(await LinesAsync(db, tracking, ct));
        AddEvent(db, tracking, user, "opened", null, tracking.Status);
        return tracking;
    }

    /// <summary>
    /// بعد عملية على سند استلام (محفوظة): المتابعة النشطة لمصدره (تُنشأ لفاتورة المشتريات) تُحدَّث حالتها من الكميات ويُسجَّل الحدث.
    /// الرفض والإلغاء اليدويان لا يتغيران.
    /// </summary>
    public static async Task OnReceiptChangedAsync(ErpDbContext db, ICurrentUser user, DeliveryNote note, string eventType, string? documentNumber,
        string? notes, CancellationToken ct)
    {
        if (SourceOf(note) is not { } source) return;
        var tracking = await ForSourceAsync(db, user, source.Type, source.Id, source.Type == PurchaseSupplySourceTypes.PurchaseInvoice, null, null, ct);
        if (tracking == null) return;
        await db.SaveChangesAsync(ct);
        var from = tracking.Status;
        tracking.Status = PurchaseReceiptQuantities.StatusOf(await LinesAsync(db, tracking, ct));
        if (eventType == "receipt_created" && (tracking.LastReceiptDate == null || note.Date > tracking.LastReceiptDate)) tracking.LastReceiptDate = note.Date;
        AddEvent(db, tracking, user, eventType, from, tracking.Status, notes, note.Id, documentNumber ?? note.DeliveryNumber);
        await db.SaveChangesAsync(ct);
    }

    public static async Task<List<PurchaseReceiptSourceLineDto>> LinesAsync(ErpDbContext db, PurchaseSupplyTracking t, CancellationToken ct)
        => (await LinesForAsync(db, new[] { t }, ct)).GetValueOrDefault(t.Id) ?? new();

    /// <summary>كميات عدة متابعات دفعة واحدة (القوائم والتقارير).</summary>
    public static async Task<Dictionary<Guid, List<PurchaseReceiptSourceLineDto>>> LinesForAsync(ErpDbContext db, IReadOnlyCollection<PurchaseSupplyTracking> trackings,
        CancellationToken ct)
    {
        var invoiceIds = trackings.Where(t => t.SourceType == PurchaseSupplySourceTypes.PurchaseInvoice).Select(t => t.SourceId).Distinct().ToList();
        var contractIds = trackings.Where(t => t.SourceType == PurchaseSupplySourceTypes.Contract).Select(t => t.SourceId).Distinct().ToList();
        var invoices = invoiceIds.Count == 0 ? new List<PurchaseInvoice>()
            : await db.Set<PurchaseInvoice>().AsNoTracking().Include(i => i.Items).Where(i => invoiceIds.Contains(i.Id)).ToListAsync(ct);
        var contracts = contractIds.Count == 0 ? new List<CommercialContract>()
            : await db.Set<CommercialContract>().AsNoTracking().Where(c => contractIds.Contains(c.Id)).ToListAsync(ct);
        var byInvoice = await PurchaseReceiptQuantities.ForInvoicesAsync(db, invoices, null, ct);
        var byContract = await PurchaseReceiptQuantities.ForContractsAsync(db, contracts, ct);
        return trackings.ToDictionary(t => t.Id, t =>
            (t.SourceType == PurchaseSupplySourceTypes.PurchaseInvoice ? byInvoice.GetValueOrDefault(t.SourceId) : byContract.GetValueOrDefault(t.SourceId)) ?? new());
    }

    public static void AddEvent(ErpDbContext db, PurchaseSupplyTracking t, ICurrentUser user, string type, string? from, string? to, string? notes = null,
        Guid? deliveryNoteId = null, string? documentNumber = null)
        => db.Add(new PurchaseSupplyEvent
        {
            TrackingId = t.Id, DeliveryNoteId = deliveryNoteId, DocumentNumber = documentNumber, EventType = type, FromStatus = from, ToStatus = to,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(), UserId = user.UserId, UserName = user.Name ?? user.Email, OccurredAt = DateTime.UtcNow,
        });
}
