using ERP.Core.Contracts.Accounting;
using ERP.Core.Models.Shared;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class DeliveryNoteService : IDeliveryNoteService
{
    private readonly ErpDbContext _db;
    private readonly INumberSequenceService _numbers;
    private readonly ICommercialContractService _contracts;
    private readonly ISalesInvoiceService _sales;
    private readonly IPurchaseInvoiceService _purchases;
    private readonly ITransactionRunner _tx;
    private readonly IProjectSupplyService _supply;
    private readonly ERP.Core.Contracts.Shared.IPermissionService _permissions;
    private readonly ERP.Core.Contracts.Shared.ICurrentUser _user;

    public DeliveryNoteService(ErpDbContext db, INumberSequenceService numbers, ICommercialContractService contracts, ISalesInvoiceService sales, IPurchaseInvoiceService purchases,
        ITransactionRunner tx, IProjectSupplyService supply, ERP.Core.Contracts.Shared.IPermissionService permissions, ERP.Core.Contracts.Shared.ICurrentUser user)
    {
        _db = db; _numbers = numbers; _contracts = contracts; _sales = sales; _purchases = purchases; _tx = tx; _supply = supply; _permissions = permissions; _user = user;
    }

    // ---------- مصدر البيان (الاتفاقية أو فاتورة البيع) ----------

    public async Task<DeliverySourceDto> GetSalesInvoiceSourceAsync(Guid invoiceId, CancellationToken ct = default)
    {
        await AuthorizeAsync("sales_delivery", ScreenAction.Create, null, ct);
        var invoice = await DeliverySources.SalesInvoiceAsync(_db, invoiceId, ct);
        return await DeliverySources.SalesInvoiceSourceAsync(_db, invoice, null, ct);
    }

    public async Task<DeliverySourceDto> GetContractSourceAsync(Guid contractId, CancellationToken ct = default)
    {
        await AuthorizeAsync("sales_delivery", ScreenAction.Create, null, ct);
        var contract = await _db.Set<CommercialContract>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == contractId, ct)
            ?? throw new NotFoundException(Messages.ContractNotFoundDot);
        if (await CommercialContractService.IsBoqContractAsync(_db, contract.ContractType, ct)) throw new ConflictException(Messages.BoqContractBilledByCertificates);
        return await DeliverySources.ContractSourceAsync(_db, contract, ct);
    }

    public async Task<List<DeliveryNoteEventDto>> GetEventsAsync(Guid id, CancellationToken ct = default)
    {
        var note = await _db.Set<DeliveryNote>().AsNoTracking().Where(n => n.Id == id).Select(n => new { n.Type, n.ContractId }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.DeliveryNoteNotFound);
        await AuthorizeAsync(note.Type, ScreenAction.View, note.ContractId, ct);
        return await DeliverySources.EventsAsync(_db, id, ct);
    }

    /// <summary>أسطر الطلب تُعاد قيمها المحسوبة: لا يُقبل من العميل مؤكد ولا مفوتر، ولا مصدر لسطر بيان يدوي.</summary>
    private static void ResetTrackingFields(DeliveryNote note)
    {
        foreach (var i in note.Items)
        {
            i.ConfirmedQty = null; i.InvoicedQty = 0; i.ReturnedQty = 0;
            if (note.SourceType == DeliveryNoteSources.Manual) i.SourceLineId = null;
        }
    }

    // ---------- الصلاحية بنوع المستند ----------

    /// <summary>سندات المشتريات ومرتجعاتها (purchase_delivery…) تتبع شاشة المشتريات، وسندات المبيعات شاشة المبيعات.</summary>
    internal static string ScreenOf(string? type) => type?.StartsWith("purchase", StringComparison.Ordinal) == true ? "purchases" : "sales";

    /// <summary>
    /// صلاحية الإجراء على شاشة نوع السند، ولسند مشتريات مرتبط بمشروع: نطاق العقد نفسه (من لا يرى المشروع لا يُرحِّل عهدته ولا يحذفها).
    /// المتحكم يسمح بالدخول لمن له المبيعات أو المشتريات، والفصل الدقيق هنا.
    /// </summary>
    private async Task AuthorizeAsync(string? type, ScreenAction action, Guid? contractId, CancellationToken ct)
    {
        if (ApprovalExecutionScope.Active) return; // تنفيذ عملية معتمدة: فُحص مقدّم الطلب عند طلبه
        var screen = ScreenOf(type);
        if (!await _permissions.HasPermissionAsync(screen, action, ct)) throw new ForbiddenException();
        if (screen == "purchases" && contractId is Guid id)
            await _permissions.AuthorizeRecordAsync(PermissionCatalog.CommercialContractsScreenId, ScreenAction.View, ScopedRecordTypes.CommercialContractKey, id, ct: ct);
    }

    /// <summary>أنواع السندات التي يراها المستخدم (المبيعات و/أو المشتريات).</summary>
    private async Task<(bool Sales, bool Purchases)> VisibleAsync(CancellationToken ct)
        => (await _permissions.HasPermissionAsync("sales", ScreenAction.View, ct), await _permissions.HasPermissionAsync("purchases", ScreenAction.View, ct));

    public async Task<PagedResult<DeliveryNoteDto>> ListAsync(PaginationParams p, CancellationToken ct = default)
    {
        var q = _db.Set<DeliveryNote>().AsNoTracking().AsQueryable();
        var (sales, purchases) = await VisibleAsync(ct);
        if (!sales) q = q.Where(n => n.Type.StartsWith("purchase"));
        if (!purchases) q = q.Where(n => !n.Type.StartsWith("purchase"));
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(n => n.DeliveryNumber.Contains(t) || n.PartyName.Contains(t) || (n.ContractNumber != null && n.ContractNumber.Contains(t)));
        }
        var total = await q.CountAsync(ct);
        var items = await q.Include(n => n.Items).OrderByDescending(n => n.Date)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);
        var dtos = items.Select(Mapper.Map<DeliveryNoteDto>).ToList();
        await ApplyProjectNamesAsync(dtos, ct);
        return new PagedResult<DeliveryNoteDto>
        {
            Items = dtos,
            TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    public async Task<DeliveryNoteDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var note = await _db.Set<DeliveryNote>().AsNoTracking().Include(n => n.Items).FirstOrDefaultAsync(n => n.Id == id, ct)
            ?? throw new NotFoundException(Messages.DeliveryNoteNotFound);
        await AuthorizeAsync(note.Type, ScreenAction.View, note.ContractId, ct);
        var dto = Mapper.Map<DeliveryNoteDto>(note);
        await ApplyProjectNamesAsync(new[] { dto }, ct);
        return dto;
    }

    private async Task ApplyProjectNamesAsync(IReadOnlyCollection<DeliveryNoteDto> notes, CancellationToken ct)
    {
        // الاسم الإنجليزي الحالي لأصناف الأسطر من البطاقة (لا يُحفظ على السطر)
        var productIds = notes.SelectMany(n => n.Items).Select(i => i.ItemId).Where(id => id != Guid.Empty).Distinct().ToList();
        if (productIds.Count > 0)
        {
            var names = await _db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.NameEn, ct);
            foreach (var item in notes.SelectMany(n => n.Items))
                if (names.TryGetValue(item.ItemId, out var nameEn) && !string.IsNullOrWhiteSpace(nameEn)) item.ItemNameEn = nameEn;
        }
        var contractIds = notes.Where(note => note.ContractId.HasValue).Select(note => note.ContractId!.Value).Distinct().ToList();
        if (contractIds.Count == 0) return;

        var projects = await _db.Set<CommercialContract>().AsNoTracking()
            .Where(contract => contractIds.Contains(contract.Id))
            .Select(contract => new { contract.Id, contract.ProjectName, contract.ProjectNameEn })
            .ToDictionaryAsync(contract => contract.Id, ct);

        foreach (var note in notes)
        {
            if (note.ContractId is Guid contractId && projects.TryGetValue(contractId, out var project))
            {
                note.ProjectName = project.ProjectName;
                note.ProjectNameEn = project.ProjectNameEn;
            }
        }
    }

    public Task<DeliveryNoteDto> CreateAsync(CreateDeliveryNoteDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var errors = new List<string>();
            var fromInvoice = r.SourceType == DeliveryNoteSources.SalesInvoice;
            if (fromInvoice) return await CreateFromSalesInvoiceAsync(r, token);
            if (r.SourceType == DeliveryNoteSources.PurchaseInvoice) return await CreateFromPurchaseInvoiceAsync(r, token);
            // سند استلام المشروع: المورد من الطلب (يُفحص مع قواعد المشروع)
            if (r.RequisitionId == null) TradeHelper.RequireParty(r.PartyName, errors);
            if (r.Type is not ("sales_delivery" or "purchase_delivery")) errors.Add(Messages.DeliveryNoteTypeInvalid);
            if (r.Items.Count == 0) errors.Add(Messages.AtLeastOneLineRequired);
            if (r.Items.Any(i => i.DeliveredQty < 0 || i.ContractQty < 0)) errors.Add(Messages.QuantitiesCannotBeNegative);
            ValidatePricing(r.Items, errors);
            if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

            if (r.RequisitionId.HasValue && r.Type != ProjectReceipts.NoteType) throw new ValidationFailedException(Messages.ProjectReceiptOnlyPurchase);
            // استلام مشروع المقاولات مرتبط بطلب شرائه دائماً: بلا الطلب لا يُفحص الاستلام المكرر ولا يُحتسب في متابعة المشروع
            if (r.ConstructionWarehouseReceipt && r.RequisitionId == null) throw new ValidationFailedException(Messages.ProjectReceiptRequiresRequisition);
            var note = Mapper.Map<DeliveryNote>(r);
            note.CustodyIssueId = null;
            note.SourceType = DeliveryNoteSources.Manual; note.SourceId = null; note.SourceNumber = null;
            note.DeliveryStatus = DeliveryTrackingStatuses.Legacy;
            ResetTrackingFields(note);
            await LineDimensions.EnsureAsync(_db, note.Items.Select(i => (i.WarehouseId, i.CostCenterId)), token);
            // استلام على أمر شراء: المورد والسعر من الأمر، والكمية ≤ المتبقي منه (وطلب المشروع يجعله استلام مشروع)
            await PurchaseOrderReceipts.PrepareAsync(_db, note, token);
            var projectReceipt = ProjectReceipts.IsProjectReceipt(note);
            await AuthorizeAsync(r.Type, ScreenAction.Create, r.ContractId, token);
            if (projectReceipt)
            {
                // سند استلام مشتريات مشروع: عهدة للمشروع بسعر شرائها، لا موقع عام (العقد والمورد من الطلب)
                await ProjectReceipts.PrepareAsync(_db, note, token, r.ConstructionWarehouseReceipt);
                // العقد يُعرف من الطلب: نطاق المشروع بعد معرفته
                if (note.ContractId != r.ContractId) await AuthorizeAsync(note.Type, ScreenAction.Create, note.ContractId, token);
            }
            else
            {
                CommercialContract? contract = null;
                if (r.ContractId.HasValue)
                    contract = await _db.Set<CommercialContract>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.ContractId, token)
                        ?? throw new ValidationFailedException(Messages.ContractNotFoundDot);
                if (contract != null && await CommercialContractService.IsBoqContractAsync(_db, contract.ContractType, token))
                    throw new ConflictException(Messages.BoqContractBilledByCertificates);
                // التسليم على عقد أكمل مراحل اعتماده فقط (ساري): المسودة والمراجعة والاعتماد لم تُلزم الطرفين بعد،
                // والموقوف والمغلق والملغي لا تسليم عليها
                if (contract != null && contract.Stage != ContractStageDefinition.Active)
                    throw new ConflictException(Messages.DeliveryContractNotActive);
                note.ContractNumber = contract?.ContractNumber ?? r.ContractNumber;
                if (contract != null && note.Type == "sales_delivery")
                {
                    // مسار الاتفاقية: بيان متتبَّع يُفوتر مرحلياً بالمؤكد تسليمه
                    note.SourceType = DeliveryNoteSources.Contract; note.SourceId = contract.Id; note.SourceNumber = contract.ContractNumber;
                    note.DeliveryStatus = DeliveryTrackingStatuses.Pending;
                }
                // التسليم على دفعات: كل سطر من بنود الاتفاقية وضمن المتبقي منه
                if (contract != null) await ContractDeliveries.EnsureWithinContractAsync(_db, note, contract, token);
            }
            // المستلم والمرفوض: سند استلام المشتريات (لا أمر شراء ولا مشروع) يسجّل المرفوض بسببه خارج كمية السند، فلا يُفوتر ولا يُخزَّن
            DeliverySources.ApplyReceiptQuantities(note, allowRejection: note.Type == "purchase_delivery" && !projectReceipt
                && note.PurchaseOrderId == null && !r.ConstructionWarehouseReceipt);
            // متابعة توريد الاتفاقية المرفوضة أو الملغاة توقف الاستلام عليها حتى يُعاد فتحها
            if (PurchaseSupplyTracker.SourceOf(note) is { } supply)
                await PurchaseSupplyTracker.EnsureOpenForReceiptAsync(_db, supply.Type, supply.Id, note.ContractNumber ?? string.Empty, token);
            note.Date = r.Date == default ? SaudiTime.Today : r.Date;
            // بيان التسليم (بيع) وسند الاستلام (شراء) نوعان بعدّادين وبادئتين مستقلتين
            var salesNote = r.Type == "sales_delivery";
            note.DeliveryNumber = await _numbers.AssignAsync(new DocumentNumberRequest
            {
                Key = salesNote ? "delivery_note" : "goods_receipt", DefaultPrefix = salesNote ? "DN-" : "GRN-", WarehouseId = note.WarehouseId, Date = note.Date,
                ManualNumber = r.DeliveryNumber, UserEntered = true,
                Exists = (n, c) => _db.Set<DeliveryNote>().AnyAsync(x => x.DeliveryNumber == n, c),
            }, token);
            note.Status = DeliveryNoteStatus.Delivered;
            note.InvoiceId = null; note.InvoiceNumber = null;
            foreach (var i in note.Items) i.ReturnedQty = 0;
            // وحدة السطر من وحدات صنفه (تنتقل إلى الفاتورة ويحدد محركها معاملها)
            await ProductLineUnits.NormalizeAsync(_db, note.Items.ToList(), i => i.ItemId, i => i.Unit, (i, u) => i.Unit = u, token);
            await DeliveryLocationSnapshot.ApplyAsync(_db, note, token);
            Price(note);
            _db.Add(note);
            await _db.SaveChangesAsync(token);
            if (DeliveryTrackingStatuses.IsTracked(note.DeliveryStatus))
            {
                DeliverySources.AddEvent(_db, note, _user, "created", null, note.DeliveryStatus);
                await _db.SaveChangesAsync(token);
            }
            if (projectReceipt && note.WarehouseId is null)
            {
                note.CustodyIssueId = await _supply.PostPurchaseReceiptAsync(note, token);
                await _db.SaveChangesAsync(token);
            }
            await RefreshOrderAsync(note.PurchaseOrderId, token);
            await PurchaseSupplyTracker.OnReceiptChangedAsync(_db, _user, note, "receipt_created", null, null, token);
            return await GetAsync(note.Id, token);
        }, ct);

    public Task<DeliveryNoteDto> UpdateAsync(Guid id, UpdateDeliveryNoteDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var note = await _db.Set<DeliveryNote>().Include(n => n.Items).FirstOrDefaultAsync(n => n.Id == id, token)
                ?? throw new NotFoundException(Messages.DeliveryNoteNotFound);
            await AuthorizeAsync(note.Type, ScreenAction.Edit, note.ContractId, token);
            if (note.InvoiceId != null || note.Status is not (DeliveryNoteStatus.Delivered or DeliveryNoteStatus.Draft))
                throw new ConflictException(Messages.CannotEditBilledOrReturnedNote);
            // سند استلام المشروع رحّل عهدته وقيدها: يُحذف (فيُعكسان) ويُنشأ غيره، أو يُرجع منه
            if (ProjectReceipts.IsProjectReceipt(note)) throw new ConflictException(Messages.ProjectReceiptNotEditable);
            if (note.PurchaseOrderId != null) throw new ConflictException(Messages.ReceiptNotEditable);
            if (await _db.Set<DeliveryReturnNote>().AnyAsync(x => x.DeliveryNoteId == id, token)) throw new ConflictException(Messages.HasReturns);
            // البيان المتتبَّع يُعدَّل قبل خروجه للتوصيل فقط
            var tracked = DeliveryTrackingStatuses.IsTracked(note.DeliveryStatus);
            if (tracked && note.DeliveryStatus != DeliveryTrackingStatuses.Pending) throw new ConflictException(Messages.DeliveryNoteNotEditableAfterDispatch);
            if (note.SourceType == DeliveryNoteSources.SalesInvoice) return await UpdateFromSalesInvoiceAsync(note, r, token);
            if (note.SourceType == DeliveryNoteSources.PurchaseInvoice) return await UpdateFromPurchaseInvoiceAsync(note, r, token);

            var errors = new List<string>();
            TradeHelper.RequireParty(r.PartyName, errors);
            if (r.Items.Count == 0) errors.Add(Messages.AtLeastOneLineRequired);
            if (r.Items.Any(i => i.DeliveredQty < 0 || i.ContractQty < 0)) errors.Add(Messages.QuantitiesCannotBeNegative);
            ValidatePricing(r.Items, errors);
            if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

            var keep = (note.DeliveryNumber, note.Type, note.Status, note.InvoiceId, note.InvoiceNumber, note.RequisitionId, note.CustodyIssueId, note.PurchaseOrderId);
            var source = (note.SourceType, note.SourceId, note.SourceNumber, note.DeliveryStatus, note.ContractId, note.ContractNumber);
            Mapper.Apply(r, note);
            (note.DeliveryNumber, note.Type, note.Status, note.InvoiceId, note.InvoiceNumber, note.RequisitionId, note.CustodyIssueId, note.PurchaseOrderId) = keep;
            (note.SourceType, note.SourceId, note.SourceNumber, note.DeliveryStatus) = (source.SourceType, source.SourceId, source.SourceNumber, source.DeliveryStatus);
            // بيان الاتفاقية المتتبَّع لا ينتقل لاتفاقية أخرى
            if (tracked) (note.ContractId, note.ContractNumber) = (source.ContractId, source.ContractNumber);
            if (note.ContractId is Guid linkedContractId
                && await _db.Set<CommercialContract>().AsNoTracking().Where(c => c.Id == linkedContractId).Select(c => new { c.ContractType, c.WarehouseId }).FirstOrDefaultAsync(token) is { } linked
                && await CommercialContractService.IsBoqContractAsync(_db, linked.ContractType, token)
                && !(note.Type == ProjectReceipts.NoteType && note.WarehouseId == linked.WarehouseId))
                throw new ConflictException(Messages.BoqContractBilledByCertificates);
            foreach (var removed in Mapper.SyncCollections(r, note, added => _db.Add(added))) _db.Remove(removed);
            ResetTrackingFields(note);
            // بيان اتفاقية (لا جدول كميات): الأسطر من بنودها والكميات ضمن المتبقي منها عدا هذا البيان
            if (note.ContractId is Guid agreementId
                && await _db.Set<CommercialContract>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == agreementId, token) is { } agreement
                && !await CommercialContractService.IsBoqContractAsync(_db, agreement.ContractType, token))
                await ContractDeliveries.EnsureWithinContractAsync(_db, note, agreement, token);
            await LineDimensions.EnsureAsync(_db, note.Items.Select(i => (i.WarehouseId, i.CostCenterId)), token);
            foreach (var i in note.Items) i.ReturnedQty = 0;
            // المرفوض لسند استلام المشتريات (لا استلام مقاولات على عقد جدول كميات؛ سندات أوامر الشراء والمشاريع لا تُعدَّل)
            var boqReceipt = note.ContractId is Guid boqCheckId
                && await _db.Set<CommercialContract>().AsNoTracking().Where(c => c.Id == boqCheckId).Select(c => c.ContractType).FirstOrDefaultAsync(token) is { } boqType
                && await CommercialContractService.IsBoqContractAsync(_db, boqType, token);
            DeliverySources.ApplyReceiptQuantities(note, allowRejection: note.Type == "purchase_delivery" && !boqReceipt);
            await ProductLineUnits.NormalizeAsync(_db, note.Items.ToList(), i => i.ItemId, i => i.Unit, (i, u) => i.Unit = u, token);
            await DeliveryLocationSnapshot.ApplyAsync(_db, note, token);
            Price(note);
            if (tracked) DeliverySources.AddEvent(_db, note, _user, "updated", note.DeliveryStatus, note.DeliveryStatus);
            await _db.SaveChangesAsync(token);
            await PurchaseSupplyTracker.OnReceiptChangedAsync(_db, _user, note, "receipt_updated", null, null, token);
            return await GetAsync(id, token);
        }, ct);

    /// <summary>
    /// سند استلام من فاتورة مشتريات مرحّلة (المسار الثاني): مستند تشغيلي فقط — لا فاتورة ولا حركة مخزون ولا قيد ولا ضريبة ولا تكلفة؛
    /// الفاتورة أنشأتها. المورد والأسطر والمستودع ومركز التكلفة من الفاتورة، والمستلم (المقبول + المرفوض) ضمن المتبقي بقفل الفاتورة.
    /// يتطلب صلاحية إنشاء المشتريات وإضافة متابعة التوريد.
    /// </summary>
    private async Task<DeliveryNoteDto> CreateFromPurchaseInvoiceAsync(CreateDeliveryNoteDto r, CancellationToken token)
    {
        if (r.SourceId is not Guid invoiceId) throw new ValidationFailedException(Messages.DeliverySourceRequired);
        if (r.Type != "purchase_delivery") throw new ValidationFailedException(Messages.DeliveryNoteTypeInvalid);
        if (r.Items.Count == 0) throw new ValidationFailedException(Messages.AtLeastOneLineRequired);
        await AuthorizeAsync(r.Type, ScreenAction.Create, null, token);
        await RequireReceiptTrackingAsync(ScreenAction.Create, token);
        var note = Mapper.Map<DeliveryNote>(r);
        note.SourceType = DeliveryNoteSources.PurchaseInvoice;
        ResetTrackingFields(note);
        await DeliverySources.PrepareFromPurchaseInvoiceAsync(_db, note, invoiceId, token);
        await PurchaseSupplyTracker.EnsureOpenForReceiptAsync(_db, PurchaseSupplySourceTypes.PurchaseInvoice, invoiceId, note.SourceNumber ?? string.Empty, token);
        note.DeliveryStatus = DeliveryTrackingStatuses.Legacy; // متابعته في متابعة التوريد لا في متابعة توصيل المبيعات
        note.Date = r.Date == default ? SaudiTime.Today : r.Date;
        note.DeliveryNumber = await _numbers.AssignAsync(new DocumentNumberRequest
        {
            Key = "goods_receipt", DefaultPrefix = "GRN-", WarehouseId = note.WarehouseId, Date = note.Date,
            ManualNumber = r.DeliveryNumber, UserEntered = true,
            Exists = (n, c) => _db.Set<DeliveryNote>().AnyAsync(x => x.DeliveryNumber == n, c),
        }, token);
        note.Status = DeliveryNoteStatus.Delivered;
        note.InvoiceId = null; note.InvoiceNumber = null;
        Price(note);
        _db.Add(note);
        await _db.SaveChangesAsync(token);
        await PurchaseSupplyTracker.OnReceiptChangedAsync(_db, _user, note, "receipt_created", null, $"فاتورة {note.SourceNumber}", token);
        return await GetAsync(note.Id, token);
    }

    private async Task<DeliveryNoteDto> UpdateFromPurchaseInvoiceAsync(DeliveryNote note, UpdateDeliveryNoteDto r, CancellationToken token)
    {
        await RequireReceiptTrackingAsync(ScreenAction.Edit, token);
        if (r.Items.Count == 0) throw new ValidationFailedException(Messages.AtLeastOneLineRequired);
        var keep = (note.DeliveryNumber, note.Type, note.Status, note.InvoiceId, note.InvoiceNumber, note.SourceType, note.SourceId, note.SourceNumber, note.DeliveryStatus);
        Mapper.Apply(r, note);
        (note.DeliveryNumber, note.Type, note.Status, note.InvoiceId, note.InvoiceNumber, note.SourceType, note.SourceId, note.SourceNumber, note.DeliveryStatus) = keep;
        foreach (var removed in Mapper.SyncCollections(r, note, added => _db.Add(added))) _db.Remove(removed);
        ResetTrackingFields(note);
        await DeliverySources.PrepareFromPurchaseInvoiceAsync(_db, note, note.SourceId!.Value, token);
        Price(note);
        await _db.SaveChangesAsync(token);
        await PurchaseSupplyTracker.OnReceiptChangedAsync(_db, _user, note, "receipt_updated", null, null, token);
        return await GetAsync(note.Id, token);
    }

    /// <summary>صلاحية متابعة التوريد (OptIn) لسند فاتورة المشتريات، فوق صلاحية المشتريات.</summary>
    private async Task RequireReceiptTrackingAsync(ScreenAction action, CancellationToken ct)
    {
        if (ApprovalExecutionScope.Active) return;
        if (!await _permissions.HasPermissionAsync(PermissionCatalog.PurchaseReceiptTrackingScreenId, action, ct)) throw new ForbiddenException();
    }

    /// <summary>
    /// بيان من فاتورة بيع مرحّلة: مستند توصيل فقط — لا فاتورة ولا حركة مخزون ولا قيد. الأسطر والموقع والطرف من الفاتورة،
    /// والكميات ضمن المتبقي بقفل الفاتورة.
    /// </summary>
    private async Task<DeliveryNoteDto> CreateFromSalesInvoiceAsync(CreateDeliveryNoteDto r, CancellationToken token)
    {
        if (r.SourceId is not Guid invoiceId) throw new ValidationFailedException(Messages.DeliverySourceRequired);
        if (r.Type != "sales_delivery") throw new ValidationFailedException(Messages.DeliveryNoteTypeInvalid);
        if (r.Items.Count == 0) throw new ValidationFailedException(Messages.AtLeastOneLineRequired);
        await AuthorizeAsync(r.Type, ScreenAction.Create, null, token);
        var note = Mapper.Map<DeliveryNote>(r);
        note.CustodyIssueId = null;
        note.SourceType = DeliveryNoteSources.SalesInvoice;
        ResetTrackingFields(note);
        await DeliverySources.PrepareFromSalesInvoiceAsync(_db, note, invoiceId, token);
        note.DeliveryStatus = DeliveryTrackingStatuses.Pending;
        note.Date = r.Date == default ? SaudiTime.Today : r.Date;
        note.DeliveryNumber = await _numbers.AssignAsync(new DocumentNumberRequest
        {
            Key = "delivery_note", DefaultPrefix = "DN-", WarehouseId = note.WarehouseId, Date = note.Date,
            ManualNumber = r.DeliveryNumber, UserEntered = true,
            Exists = (n, c) => _db.Set<DeliveryNote>().AnyAsync(x => x.DeliveryNumber == n, c),
        }, token);
        note.Status = DeliveryNoteStatus.Delivered;
        note.InvoiceId = null; note.InvoiceNumber = null;
        await DeliveryLocationSnapshot.ApplyAsync(_db, note, token);
        Price(note);
        _db.Add(note);
        await _db.SaveChangesAsync(token);
        DeliverySources.AddEvent(_db, note, _user, "created", null, note.DeliveryStatus, $"فاتورة {note.SourceNumber}");
        await _db.SaveChangesAsync(token);
        return await GetAsync(note.Id, token);
    }

    private async Task<DeliveryNoteDto> UpdateFromSalesInvoiceAsync(DeliveryNote note, UpdateDeliveryNoteDto r, CancellationToken token)
    {
        if (r.Items.Count == 0) throw new ValidationFailedException(Messages.AtLeastOneLineRequired);
        var keep = (note.DeliveryNumber, note.Type, note.Status, note.InvoiceId, note.InvoiceNumber, note.SourceType, note.SourceId, note.SourceNumber, note.DeliveryStatus);
        Mapper.Apply(r, note);
        (note.DeliveryNumber, note.Type, note.Status, note.InvoiceId, note.InvoiceNumber, note.SourceType, note.SourceId, note.SourceNumber, note.DeliveryStatus) = keep;
        foreach (var removed in Mapper.SyncCollections(r, note, added => _db.Add(added))) _db.Remove(removed);
        ResetTrackingFields(note);
        await DeliverySources.PrepareFromSalesInvoiceAsync(_db, note, note.SourceId!.Value, token);
        await DeliveryLocationSnapshot.ApplyAsync(_db, note, token);
        Price(note);
        DeliverySources.AddEvent(_db, note, _user, "updated", note.DeliveryStatus, note.DeliveryStatus);
        await _db.SaveChangesAsync(token);
        return await GetAsync(note.Id, token);
    }

    /// <summary>حالة أمر الشراء من سنداته بعد كل تغيير على سند مرتبط به.</summary>
    private async Task RefreshOrderAsync(Guid? orderId, CancellationToken ct)
    {
        if (orderId is not Guid id) return;
        await PurchaseOrderReceipts.RefreshStatusAsync(_db, id, ct);
        await _db.SaveChangesAsync(ct);
    }

    private static void ValidatePricing(IEnumerable<DeliveryNoteItemDto> items, List<string> errors)
    {
        if (items.Any(i => i.UnitPrice < 0 || i.VatRate is < 0 or > 100)) errors.Add(Messages.PriceAndVatInvalid);
        if (items.Any(i => i.Discount < 0 || i.Discount > DocumentPricing.Round(i.DeliveredQty * i.UnitPrice))) errors.Add(Messages.SomeDocumentLinesInvalid);
    }

    /// <summary>قيمة البيان تُحتسب في السرفر: الكمية المسلّمة × السعر ناقص خصم السطر، والضريبة لكل سطر.</summary>
    private static void Price(DeliveryNote note)
    {
        foreach (var i in note.Items)
        {
            i.TotalBeforeVat = DocumentPricing.Round(i.DeliveredQty * i.UnitPrice - i.Discount);
            i.VatAmount = DocumentPricing.Round(i.TotalBeforeVat * i.VatRate / 100m);
            i.TotalAfterVat = i.TotalBeforeVat + i.VatAmount;
        }
        note.Subtotal = note.Items.Sum(i => i.TotalBeforeVat);
        note.VatTotal = note.Items.Sum(i => i.VatAmount);
        note.GrandTotal = note.Subtotal + note.VatTotal;
    }

    public Task DeleteAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var note = await _db.Set<DeliveryNote>().Include(n => n.Items).FirstOrDefaultAsync(n => n.Id == id, token)
                ?? throw new NotFoundException(Messages.DeliveryNoteNotFound);
            await AuthorizeAsync(note.Type, ScreenAction.Delete, note.ContractId, token);
            if (note.InvoiceId != null) throw new ConflictException(Messages.CannotDeleteBilledNote);
            // البيان المتتبَّع (له سجل أحداث) لا يُحذف نهائياً: يُلغى بسبب
            if (DeliveryTrackingStatuses.IsTracked(note.DeliveryStatus) || await _db.Set<DeliveryNoteEvent>().AnyAsync(e => e.DeliveryNoteId == id, token)
                || await _db.Set<DeliveryNoteInvoiceLine>().AnyAsync(l => l.DeliveryNoteId == id, token))
                throw new ConflictException(Messages.DeliveryNoteHasHistory);
            if (await _db.Set<DeliveryReturnNote>().AnyAsync(x => x.DeliveryNoteId == id, token)) throw new ConflictException(Messages.DeleteReturnsFirst);
            // عهدة سند المشروع وقيدها تُعكس معه (يمنعها اختيار كمياتها في مستخلص أو هدر منها)
            if (note.CustodyIssueId is { } custody)
                await _supply.CancelReceiptCustodyAsync(note.ContractId!.Value, custody, string.Format(Messages.ProjectReceiptDeletedReason, note.DeliveryNumber), token);
            // مرفقات سند استلام المشتريات تُحذف معه في معاملته
            if (note.Type == "purchase_delivery")
                await DocumentAttachmentService.RemoveForOwnersAsync(_db, PurchaseReceiptAttachmentOwnerTypes.Receipt, new[] { id }, token);
            _db.RemoveRange(note.Items);
            _db.Remove(note);
            await _db.SaveChangesAsync(token);
            await RefreshOrderAsync(note.PurchaseOrderId, token);
            await PurchaseSupplyTracker.OnReceiptChangedAsync(_db, _user, note, "receipt_deleted", null, null, token);
        }, ct);

    public async Task<DeliveryReturnNoteDto> GetReturnAsync(Guid id, CancellationToken ct = default)
    {
        var ret = await _db.Set<DeliveryReturnNote>().AsNoTracking().Include(n => n.Items).FirstOrDefaultAsync(n => n.Id == id, ct)
            ?? throw new NotFoundException(Messages.DeliveryReturnNotFound);
        await AuthorizeAsync(ret.Type, ScreenAction.View, await ContractOfNoteAsync(ret.DeliveryNoteId, ct), ct);
        return Mapper.Map<DeliveryReturnNoteDto>(ret);
    }

    /// <summary>عقد السند الأصلي للمرتجع (نطاق المشروع).</summary>
    private Task<Guid?> ContractOfNoteAsync(Guid deliveryNoteId, CancellationToken ct)
        => _db.Set<DeliveryNote>().AsNoTracking().Where(n => n.Id == deliveryNoteId).Select(n => n.ContractId).FirstOrDefaultAsync(ct);

    public Task DeleteReturnAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var ret = await _db.Set<DeliveryReturnNote>().Include(n => n.Items).FirstOrDefaultAsync(n => n.Id == id, token)
                ?? throw new NotFoundException(Messages.DeliveryReturnNotFound);
            var note = await _db.Set<DeliveryNote>().Include(n => n.Items).FirstOrDefaultAsync(n => n.Id == ret.DeliveryNoteId, token);
            await AuthorizeAsync(ret.Type, ScreenAction.Delete, note?.ContractId, token);
            if (note != null)
            {
                if (note.InvoiceId != null) throw new ConflictException(Messages.OriginalNoteBilled);
                // إرجاع المورد من عهدة المشروع: يُعكس سحبه وقيده فتعود الكمية للعهدة
                if (ret.CustodyIssueId is { } custody)
                    await _supply.CancelReceiptCustodyAsync(note.ContractId!.Value, custody, string.Format(Messages.ProjectReceiptDeletedReason, ret.ReturnNumber), token);
                foreach (var line in ret.Items)
                {
                    var src = note.Items.FirstOrDefault(i => i.ItemId == line.ItemId);
                    if (src != null) src.ReturnedQty = Math.Max(0, src.ReturnedQty - line.Quantity);
                }
                if (DeliveryTrackingStatuses.IsTracked(note.DeliveryStatus))
                {
                    // حذف المرتجع يعيد الكمية مسلَّمة: يُفحص المتبقي من المصدر (لا يتجاوز مع البيانات اللاحقة)
                    await EnsureSourceRoomAsync(note, token);
                    DeliverySources.RefreshTrackedStatus(note);
                    DeliverySources.AddEvent(_db, note, _user, "return_deleted", note.DeliveryStatus, note.DeliveryStatus, ret.ReturnNumber);
                }
                else
                    note.Status = note.Items.Any(i => i.ReturnedQty > 0)
                        ? (note.Items.All(i => i.ReturnedQty >= i.DeliveredQty) ? DeliveryNoteStatus.Returned : DeliveryNoteStatus.PartiallyReturned)
                        : DeliveryNoteStatus.Delivered;
            }
            if (note?.PurchaseOrderId is Guid orderId) await PurchaseOrderReceipts.LockAsync(_db, orderId, token);
            _db.RemoveRange(ret.Items);
            _db.Remove(ret);
            await _db.SaveChangesAsync(token);
            // إلغاء المرتجع يعيد الكمية مستلمة: لا تتجاوز مع الاستلامات اللاحقة كمية أمر الشراء
            if (note?.PurchaseOrderId is Guid po)
            {
                await PurchaseOrderReceipts.EnsureWithinOrderAsync(_db, po, token);
                await RefreshOrderAsync(po, token);
            }
            // سند استلام المشتريات: حذف المرتجع يعيد الكمية مقبولة، فلا تتجاوز مع السندات اللاحقة الفاتورة أو الاتفاقية (بقفل المصدر)
            if (note != null && PurchaseSupplyTracker.SourceOf(note) is { } supply)
            {
                if (supply.Type == PurchaseSupplySourceTypes.PurchaseInvoice)
                    await DeliverySources.EnsurePurchaseInvoiceRoomAsync(_db, supply.Id, token);
                else if (await _db.Set<CommercialContract>().AsNoTracking().Where(c => c.Id == supply.Id).Select(c => c.ContractType).FirstOrDefaultAsync(token) is { } agreementType
                         && !await CommercialContractService.IsBoqContractAsync(_db, agreementType, token))
                    await ContractDeliveries.EnsureRoomAfterChangeAsync(_db, supply.Id, token);
                await PurchaseSupplyTracker.OnReceiptChangedAsync(_db, _user, note, "receipt_return_deleted", ret.ReturnNumber, null, token);
            }
        }, ct);

    public async Task<PagedResult<DeliveryReturnNoteDto>> ListReturnsAsync(PaginationParams p, CancellationToken ct = default)
    {
        var q = _db.Set<DeliveryReturnNote>().AsNoTracking().AsQueryable();
        var (sales, purchases) = await VisibleAsync(ct);
        if (!sales) q = q.Where(n => n.Type.StartsWith("purchase"));
        if (!purchases) q = q.Where(n => !n.Type.StartsWith("purchase"));
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(n => n.ReturnNumber.Contains(t) || n.DeliveryNumber.Contains(t));
        }
        var total = await q.CountAsync(ct);
        var items = await q.Include(n => n.Items).OrderByDescending(n => n.Date)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);
        return new PagedResult<DeliveryReturnNoteDto>
        {
            Items = items.Select(Mapper.Map<DeliveryReturnNoteDto>).ToList(),
            TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    public Task<DeliveryReturnNoteDto> CreateReturnAsync(CreateDeliveryReturnNoteDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            // قفل البيان: مرتجع متزامن مع تأكيد أو فوترة أو إلغاء لا يقرأ الكميات نفسها
            await DeliverySources.LockNoteAsync(_db, r.DeliveryNoteId, token);
            var note = await _db.Set<DeliveryNote>().Include(n => n.Items).FirstOrDefaultAsync(n => n.Id == r.DeliveryNoteId, token)
                ?? throw new NotFoundException(Messages.DeliveryNoteNotFound);
            await AuthorizeAsync(note.Type, ScreenAction.Create, note.ContractId, token);
            if (note.Status == DeliveryNoteStatus.Invoiced) throw new ConflictException(Messages.CannotReturnBilledNote);
            if (r.Items.Count == 0 || r.Items.Any(i => i.Quantity <= 0)) throw new ValidationFailedException(Messages.ReturnedQuantitiesMustBePositive);
            if (r.Type is not ("sales_delivery_return" or "purchase_delivery_return")) throw new ValidationFailedException(Messages.InvalidType);
            if (ProjectReceipts.IsProjectReceipt(note) && r.Type != ProjectReceipts.ReturnType) throw new ValidationFailedException(Messages.InvalidType);
            if (note.Status == DeliveryNoteStatus.Cancelled) throw new ConflictException(Messages.DeliveryNoteCancelled);
            // مرتجع التسليم في البيان المتتبَّع: بعد تأكيد الاستلام، ومن المسلَّم غير المفوتر (المفوتر يُرجع ماليا بإشعار دائن)
            var tracked = DeliveryTrackingStatuses.IsTracked(note.DeliveryStatus);
            if (tracked)
            {
                if (!DeliveryTrackingStatuses.IsConfirmed(note.DeliveryStatus)) throw new ConflictException(Messages.DeliveryReturnNotConfirmed);
            }

            foreach (var line in r.Items)
            {
                var src = note.Items.FirstOrDefault(i => i.ItemId == line.ItemId)
                    ?? throw new ValidationFailedException(Messages.ItemNotInDeliveryNote);
                if (tracked)
                {
                    var open = DeliverySources.Billable(src);
                    if (line.Quantity > open + DeliverySources.Tolerance)
                        throw new ValidationFailedException(string.Format(Messages.DeliveryReturnExceedsUnbilled, src.ItemName, open.ToString("0.####")));
                    src.ReturnedQty += line.Quantity;
                    continue;
                }
                var remaining = src.DeliveredQty - src.ReturnedQty;
                if (line.Quantity > remaining)
                    throw new ValidationFailedException(string.Format(Messages.ReturnQtyExceedsAvailable, src.ItemName, remaining));
                src.ReturnedQty += line.Quantity;
            }
            if (tracked)
            {
                DeliverySources.RefreshTrackedStatus(note);
                DeliverySources.AddEvent(_db, note, _user, "returned", note.DeliveryStatus, note.DeliveryStatus, r.ReturnReason);
            }
            else
                note.Status = note.Items.All(i => i.ReturnedQty >= i.DeliveredQty) ? DeliveryNoteStatus.Returned : DeliveryNoteStatus.PartiallyReturned;

            var ret = Mapper.Map<DeliveryReturnNote>(r);
            ret.DeliveryNumber = note.DeliveryNumber;
            ret.ContractId = note.ContractId; ret.ContractNumber = note.ContractNumber; ret.PartyName = note.PartyName;
            ret.Date = r.Date == default ? SaudiTime.Today : r.Date;
            ret.ReturnNumber = await _numbers.AssignAsync(new DocumentNumberRequest
            {
                Key = "delivery_return", DefaultPrefix = "DR-", WarehouseId = note.WarehouseId, Date = ret.Date, // موقع بيان التسليم
                ManualNumber = r.ReturnNumber, UserEntered = true,
                Exists = (n, c) => _db.Set<DeliveryReturnNote>().AnyAsync(x => x.ReturnNumber == n, c),
            }, token);
            ret.Status = "completed";
            foreach (var i in ret.Items)
            {
                // سعر وضريبة المرتجع من سطر التسليم الأصلي، لا مما يرسله العميل.
                var src = note.Items.First(x => x.ItemId == i.ItemId);
                i.ItemName = src.ItemName; i.Unit = src.Unit; i.UnitPrice = src.UnitPrice; i.VatRate = src.VatRate;
                // خصم سطر التسليم بنسبة الكمية المرتجعة
                var net = DocumentPricing.Round(i.Quantity * i.UnitPrice
                    - (src.DeliveredQty == 0 ? 0 : src.Discount * i.Quantity / src.DeliveredQty));
                i.VatAmount = DocumentPricing.Round(net * i.VatRate / 100m);
                i.TotalAfterVat = net + i.VatAmount;
            }
            ret.Subtotal = ret.Items.Sum(i => i.TotalAfterVat - i.VatAmount);
            ret.VatTotal = ret.Items.Sum(i => i.VatAmount);
            ret.GrandTotal = ret.Subtotal + ret.VatTotal;
            _db.Add(ret);
            await _db.SaveChangesAsync(token);
            if (ProjectReceipts.IsProjectReceipt(note) && note.WarehouseId is null)
            {
                // إرجاع للمورد من عهدة المشروع (لا للموقع العام): يسحب من أسطر العهدة ويعكس استحقاقها
                ret.CustodyIssueId = await _supply.PostReceiptReturnAsync(note, ret, token);
                await _db.SaveChangesAsync(token);
            }
            await RefreshOrderAsync(note.PurchaseOrderId, token);
            // مرتجع سند الاستلام (تشغيلي): يعيد الكمية للمتبقي للاستلام بلا حركة ولا قيد في مسار فاتورة المشتريات
            await PurchaseSupplyTracker.OnReceiptChangedAsync(_db, _user, note, "receipt_returned", ret.ReturnNumber, r.ReturnReason, token);
            return Mapper.Map<DeliveryReturnNoteDto>(await _db.Set<DeliveryReturnNote>().AsNoTracking().Include(n => n.Items).FirstAsync(n => n.Id == ret.Id, token));
        }, ct);

    public Task<InvoiceDto> CreateInvoiceAsync(Guid deliveryNoteId, DateTime? date = null, Guid? supplierId = null, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            // سند على أمر شراء: يقفل الأمر قبل قراءة السند فلا يُفوتر مرتين بالتزامن (منفرداً ومع غيره في فاتورة واحدة)
            if (await _db.Set<DeliveryNote>().AsNoTracking().Where(n => n.Id == deliveryNoteId).Select(n => n.PurchaseOrderId).FirstOrDefaultAsync(token) is Guid orderId)
                await PurchaseOrderReceipts.LockAsync(_db, orderId, token);
            await DeliverySources.LockNoteAsync(_db, deliveryNoteId, token);
            var note = await _db.Set<DeliveryNote>().Include(n => n.Items).FirstOrDefaultAsync(n => n.Id == deliveryNoteId, token)
                ?? throw new NotFoundException(Messages.DeliveryNoteNotFound);
            await EnsureBillableAsync(note, token);
            if (!DeliveryTrackingStatuses.IsTracked(note.DeliveryStatus) && note.InvoiceId != null) throw new ConflictException(Messages.DeliveryNoteAlreadyBilled);
            // الفوترة إنشاء فاتورة من نوع السند: مشتريات لسند المورد، ومبيعات لسند العميل
            await AuthorizeAsync(note.Type, ScreenAction.Create, note.ContractId, token);
            return await BillAsync(new List<DeliveryNote> { note }, date, supplierId,
                ("delivery_note", note.Id, note.DeliveryNumber), token);
        }, ct);

    /// <summary>
    /// قواعد الفوترة من البيان: بيان الفاتورة لا يُفوتر، والملغى لا يُفوتر، والمتتبَّع يُفوتر بعد تأكيد الاستلام وبما لم يُفوتر من المؤكد.
    /// </summary>
    private Task EnsureBillableAsync(DeliveryNote note, CancellationToken ct)
    {
        if (note.SourceType == DeliveryNoteSources.SalesInvoice) throw new ConflictException(Messages.DeliveryNoteFromInvoiceNotBillable);
        // سند فاتورة المشتريات: الفاتورة قائمة (المخزون والقيد والضريبة منها)، فلا تُنشأ فاتورة ثانية من السند
        if (note.SourceType == DeliveryNoteSources.PurchaseInvoice) throw new ConflictException(Messages.DeliveryNoteFromPurchaseInvoiceNotBillable);
        if (note.Status == DeliveryNoteStatus.Cancelled) throw new ConflictException(Messages.DeliveryNoteCancelled);
        if (DeliveryTrackingStatuses.IsTracked(note.DeliveryStatus))
        {
            if (!DeliveryTrackingStatuses.IsConfirmed(note.DeliveryStatus)) throw new ConflictException(Messages.DeliveryNoteNotConfirmed);
            if (note.Items.All(i => DeliverySources.Billable(i) <= DeliverySources.Tolerance)) throw new ConflictException(Messages.DeliveryNoteNothingToBill);
        }
        return Task.CompletedTask;
    }

    /// <summary>يسجّل ما فُوتر من كل سطر في فاتورة مرحلية (يمنع تكرار الفوترة، وحذف الفاتورة يحرره) ويحدّث حالة البيان.</summary>
    private void RecordTrackedBilling(DeliveryNote note, IEnumerable<(DeliveryNoteItem Item, decimal Qty)> lines, InvoiceDto invoice)
    {
        foreach (var (item, qty) in lines.Where(x => x.Qty > 0))
        {
            item.InvoicedQty += qty;
            _db.Add(new DeliveryNoteInvoiceLine
            {
                DeliveryNoteId = note.Id, DeliveryNoteItemId = item.Id, InvoiceId = invoice.Id, InvoiceNumber = invoice.InvoiceNumber, Quantity = qty,
            });
        }
        note.InvoiceId = invoice.Id; note.InvoiceNumber = invoice.InvoiceNumber;
        DeliverySources.RefreshTrackedStatus(note);
        DeliverySources.AddEvent(_db, note, _user, "invoiced", note.DeliveryStatus, note.DeliveryStatus, invoice.InvoiceNumber);
    }

    /// <summary>
    /// بعد إعادة كمية للتسليم (حذف مرتجع تسليم): صافي البيانات الفعّالة على المصدر لا يتجاوز كميته. يُحفظ أولاً ثم يُقرأ بقفل المصدر.
    /// </summary>
    private async Task EnsureSourceRoomAsync(DeliveryNote note, CancellationToken ct)
    {
        if (note.SourceId is not Guid sourceId) return;
        if (note.SourceType == DeliveryNoteSources.SalesInvoice)
        {
            await DeliverySources.LockAsync(_db, "sales-invoice-delivery", sourceId, ct);
            await _db.SaveChangesAsync(ct);
            var invoice = await _db.Set<SalesInvoice>().AsNoTracking().Include(i => i.Items).FirstAsync(i => i.Id == sourceId, ct);
            var src = await DeliverySources.SalesInvoiceSourceAsync(_db, invoice, null, ct);
            if (src.Lines.FirstOrDefault(l => l.SourceQty - l.FinancialReturnedQty - l.DeliveredQty < -DeliverySources.Tolerance) is { } over)
                throw new ConflictException(string.Format(Messages.DeliveryExceedsInvoice, over.ItemName, over.DeliveredQty.ToString("0.####"),
                    Math.Max(0, over.SourceQty - over.FinancialReturnedQty).ToString("0.####"), over.SourceQty.ToString("0.####")));
            return;
        }
        if (note.SourceType != DeliveryNoteSources.Contract) return;
        await DeliverySources.LockAsync(_db, "contract-delivery", sourceId, ct);
        await _db.SaveChangesAsync(ct);
        var agreed = await _db.Set<CommercialContractItem>().AsNoTracking().Where(i => i.ContractId == sourceId && !i.IsGroup)
            .Select(i => new { Key = i.ItemId ?? i.Id, i.Description, i.Quantity }).ToListAsync(ct);
        var delivered = await DeliverySources.Active(_db.Set<DeliveryNoteItem>().AsNoTracking()).Where(i => i.DeliveryNote.ContractId == sourceId)
            .GroupBy(i => i.ItemId).Select(g => new { g.Key, Qty = g.Sum(i => (i.ConfirmedQty ?? i.DeliveredQty) - i.ReturnedQty) }).ToListAsync(ct);
        foreach (var d in delivered)
        {
            var lines = agreed.Where(a => a.Key == d.Key).ToList();
            var qty = lines.Sum(a => a.Quantity);
            if (lines.Count > 0 && d.Qty > qty + DeliverySources.Tolerance)
                throw new ConflictException(string.Format(Messages.DeliveryExceedsContract, lines[0].Description, d.Qty.ToString("0.####"),
                    qty.ToString("0.####"), qty.ToString("0.####")));
        }
    }

    /// <summary>
    /// فاتورة مورد واحدة لعدة سندات استلام من أمر شراء واحد (بقفل الأمر): كل سند غير مفوتر ومن الأمر نفسه وبالموقع والمشروع نفسيهما،
    /// والفاتورة بصافي كمياتها (المستلم − المرتجع)، ثم تُربط كل السندات بها فلا تُفوتر كمية مرتين. حذف الفاتورة يعيدها كلها قابلة للفوترة.
    /// </summary>
    public Task<InvoiceDto> CreatePurchaseOrderInvoiceAsync(Guid purchaseOrderId, IReadOnlyCollection<Guid> deliveryNoteIds, DateTime? date = null,
        CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var ids = deliveryNoteIds.Where(id => id != Guid.Empty).Distinct().ToList();
            if (ids.Count == 0) throw new ValidationFailedException(Messages.PurchaseReceiptsToInvoiceRequired);
            await PurchaseOrderReceipts.LockAsync(_db, purchaseOrderId, token);
            var order = await _db.Set<CommercialOrder>().AsNoTracking()
                    .FirstOrDefaultAsync(o => o.Id == purchaseOrderId && o.Type == PurchaseOrderReceipts.OrderType, token)
                ?? throw new NotFoundException(Messages.PurchaseCycleOrderNotFound);
            var notes = await _db.Set<DeliveryNote>().Include(n => n.Items).Where(n => ids.Contains(n.Id)).ToListAsync(token);
            if (notes.Count != ids.Count || notes.Any(n => n.PurchaseOrderId != order.Id))
                throw new ValidationFailedException(Messages.ReceiptNotOfOrder);
            if (notes.FirstOrDefault(n => n.InvoiceId != null) is { } billed)
                throw new ConflictException(string.Format(Messages.ReceiptAlreadyInvoiced, billed.DeliveryNumber, billed.InvoiceNumber));
            if (notes.Select(n => (n.WarehouseId, n.RequisitionId, n.ContractId, n.PartyId)).Distinct().Count() > 1)
                throw new ValidationFailedException(Messages.ReceiptsNotCombinable);
            await AuthorizeAsync(notes[0].Type, ScreenAction.Create, notes[0].ContractId, token);
            return await BillAsync(notes.OrderBy(n => n.Date).ThenBy(n => n.CreatedAt).ToList(), date, null,
                (PurchaseOrderReceipts.InvoiceReferenceType, order.Id, order.OrderNumber), token);
        }, ct);

    /// <summary>
    /// فاتورة من سند أو أكثر (الأول يحدد الطرف والموقع والعقد): أسطرها صافي كميات السندات، وتُعلَّم السندات مفوترة بها.
    /// </summary>
    private async Task<InvoiceDto> BillAsync(List<DeliveryNote> notes, DateTime? date, Guid? supplierId,
        (string Type, Guid Id, string Number) reference, CancellationToken token)
    {
        var note = notes[0];
        // البيان المتتبَّع (اتفاقية): المؤكد تسليمه غير المفوتر فقط؛ وغيره صافي الكمية كما كان
        var tracked = notes.Count == 1 && DeliveryTrackingStatuses.IsTracked(note.DeliveryStatus);
        var lines = notes.SelectMany(n => n.Items)
            .Select(i => (Item: i, Qty: tracked ? DeliverySources.Billable(i) : i.DeliveredQty - i.ReturnedQty)).Where(x => x.Qty > 0).ToList();
        if (lines.Count == 0) throw new ConflictException(Messages.NoNetQuantitiesToBill);

        // الأصناف المخزنية تُفوتر كأصناف، وغيرها (بنود العقد الوصفية) كبنود خدمة.
        var ids = lines.Select(x => x.Item.ItemId).Distinct().ToList();
        var products = await _db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToListAsync(token);
        var sales = note.Type == "sales_delivery";
        if (sales && supplierId.HasValue) throw new ValidationFailedException(Messages.DeliveryInvoiceSupplierOnSales);
        // المورد الذي سلّم هو من يُفوتر: يُختار للسند بلا مورد فقط، ولا يُستبدل مورد السند
        if (supplierId.HasValue && note.PartyId.HasValue && supplierId != note.PartyId)
            throw new ValidationFailedException(Messages.DeliveryInvoiceSupplierMismatch);
        var selectedSupplier = supplierId.HasValue && note.PartyId == null
            ? await _db.Set<Supplier>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == supplierId.Value, token)
                ?? throw new NotFoundException(Messages.SupplierNotFound)
            : null;
        var partyId = selectedSupplier?.Id ?? note.PartyId;
        // بيان تسليم عقد: فاتورته على مركز تكلفة العقد
        var contractCenter = note.ContractId is Guid contractId
            ? await _db.Set<CommercialContract>().AsNoTracking().Where(c => c.Id == contractId).Select(c => c.CostCenterId).FirstOrDefaultAsync(token)
            : null;
        // سند استلام مشروع: المواد عهدة للمشروع منذ استلامها، ففاتورة المورد تسوّي استحقاق الاستلام فقط — أسطرها بلا حركة مخزون
        // (بنود وصفية بأسماء الأصناف) على حساب «بضاعة مستلمة لم تُفوتر»، فلا يتغير الموقع العام ولا متوسط تكلفة الصنف
        var projectReceipt = ProjectReceipts.IsProjectReceipt(note) && note.WarehouseId is null;
        var constructionWarehouseReceipt = !sales && !projectReceipt && note.ContractId is Guid constructionContractId
            && await _db.Set<CommercialContract>().AsNoTracking().Where(c => c.Id == constructionContractId)
                .Select(c => c.ContractType).FirstOrDefaultAsync(token) is { } constructionType
            && await CommercialContractService.IsBoqContractAsync(_db, constructionType, token);
        string? receivedNotInvoiced = null;
        if (projectReceipt)
        {
            await ContractAccountsSetup.EnsureMissingAsync(_db, token);
            receivedNotInvoiced = (await PostingAccounts.SettingsAsync(_db, token))
                .ContractAccount(nameof(AccountingSettings.ProjectGoodsReceivedNotInvoicedAccountCode));
        }
        var invoice = await (sales ? (ITradeDocumentService)_sales : _purchases).CreateAsync(new CreateInvoiceDto
        {
            Kind = sales ? InvoiceKind.Sales : InvoiceKind.Purchase, SystemGenerated = true, // فاتورة البيان: الرقم تلقائي دائماً
            IssueDate = date?.Date ?? default,
            // مركز العقد من المصدر؛ ومركز اختاره المستخدم على سطر البيان يُفحص بصلاحية التعيين في الفاتورة
            CostCenterId = contractCenter, CostCenterFromSource = lines.All(x => x.Item.CostCenterId == null),
            InvoiceType = sales ? TradeHelper.InvoiceTypeFor(note.PartyTaxNumber) : InvoiceType.TaxInvoice,
            PartyId = partyId,
            PartyName = selectedSupplier?.NameAr ?? note.PartyName,
            PartyVatNumber = selectedSupplier?.VatNumber ?? note.PartyTaxNumber,
            PartyPhone = selectedSupplier?.Phone ?? note.PartyPhone,
            WarehouseId = projectReceipt ? null : note.WarehouseId, InventoryAccountCode = receivedNotInvoiced,
            PaymentMethod = partyId.HasValue ? PaymentMethod.Credit : PaymentMethod.Cash, Status = "posted",
            Notes = (notes.Count == 1 ? $"فاتورة من بيان التسليم {note.DeliveryNumber}"
                    : $"فاتورة من سندات الاستلام {string.Join("، ", notes.Select(n => n.DeliveryNumber))} - أمر الشراء {reference.Number}")
                + (note.ContractNumber != null ? $" - عقد {note.ContractNumber}" : ""),
            ReferenceType = reference.Type, ReferenceId = reference.Id, ReferenceNumber = reference.Number,
            Items = lines.Select(x => new InvoiceItemDto
            {
                ItemId = !projectReceipt && products.Contains(x.Item.ItemId) ? x.Item.ItemId : Guid.Empty,
                ItemName = x.Item.ItemName, Sku = x.Item.Sku ?? string.Empty, Unit = x.Item.Unit ?? string.Empty,
                Quantity = x.Qty, UnitPrice = x.Item.UnitPrice, VatRate = x.Item.VatRate,
                // الخصم بنسبة الكمية المفوترة (الصافي بعد المرتجع)، والموقع والمركز من سطر البيان (سند المشروع بلا موقع عام)
                Discount = x.Item.DeliveredQty == 0 ? 0 : DocumentPricing.Round(x.Item.Discount * x.Qty / x.Item.DeliveredQty),
                WarehouseId = projectReceipt ? null : x.Item.WarehouseId, CostCenterId = x.Item.CostCenterId,
            }).ToList(),
        }, token);
        if (tracked) RecordTrackedBilling(note, lines, invoice);
        else foreach (var n in notes) { n.InvoiceId = invoice.Id; n.InvoiceNumber = invoice.InvoiceNumber; n.Status = DeliveryNoteStatus.Invoiced; }
        // بيان عقد: الفاتورة تُسجَّل في فواتير العقد وتُضاف لمفوتره كما في فوترة الدفعات (فاتورة مورد المشروع ليست فوترة للعميل)
        if (!projectReceipt && !constructionWarehouseReceipt && note.ContractId is Guid billedContractId
            && await _db.Set<CommercialContract>().FirstOrDefaultAsync(c => c.Id == billedContractId, token) is { } contract)
        {
            _db.Add(CommercialContractService.ContractInvoiceFor(contract.Id, "progress", invoice, note.Id));
            contract.TotalInvoiced += invoice.GrandTotal;
            contract.RemainingBalance = contract.TotalValueWithVat - contract.TotalInvoiced;
        }
        await _db.SaveChangesAsync(token);
        await RefreshOrderAsync(note.PurchaseOrderId, token);
        return invoice;
    }

    public Task<InvoiceDto> CreateMilestoneInvoiceAsync(Guid deliveryNoteId, Guid milestoneId, DateTime? date = null, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            await DeliverySources.LockNoteAsync(_db, deliveryNoteId, token);
            var note = await _db.Set<DeliveryNote>().Include(n => n.Items).FirstOrDefaultAsync(n => n.Id == deliveryNoteId, token)
                ?? throw new NotFoundException(Messages.DeliveryNoteNotFound);
            await EnsureBillableAsync(note, token);
            var tracked = DeliveryTrackingStatuses.IsTracked(note.DeliveryStatus);
            if (!tracked && note.InvoiceId != null) throw new ConflictException(Messages.DeliveryNoteAlreadyBilled);
            if (note.Status == DeliveryNoteStatus.Returned) throw new ConflictException(Messages.DeliveryNoteFullyReturned);
            await AuthorizeAsync(note.Type, ScreenAction.Create, note.ContractId, token);
            if (note.ContractId == null) throw new ValidationFailedException(Messages.DeliveryNoteNotLinkedToContract);
            if (ProjectReceipts.IsProjectReceipt(note)) throw new ConflictException(Messages.ProjectReceiptOnlyPurchase);
            if (note.Type != "sales_delivery") throw new ConflictException(Messages.MilestoneBillingSalesOnly);

            var invoice = await _contracts.BillMilestoneAsync(note.ContractId.Value, milestoneId, date, token);
            // الدفعة على بيان متتبَّع تستهلك كل المؤكد غير المفوتر فيه (لا يُفوتر مرة أخرى)
            if (tracked) RecordTrackedBilling(note, note.Items.Select(i => (i, DeliverySources.Billable(i))).ToList(), invoice);
            else { note.InvoiceId = invoice.Id; note.InvoiceNumber = invoice.InvoiceNumber; note.Status = DeliveryNoteStatus.Invoiced; }
            // سطر فواتير العقد الذي أنشأته فوترة الدفعة يُربط بالبيان الذي فُوتر معه
            var logged = await _db.Set<ContractInvoice>().FirstOrDefaultAsync(i => i.InvoiceId == invoice.Id, token);
            if (logged != null) logged.DeliveryNoteId = note.Id;
            await _db.SaveChangesAsync(token);
            return invoice;
        }, ct);
}
