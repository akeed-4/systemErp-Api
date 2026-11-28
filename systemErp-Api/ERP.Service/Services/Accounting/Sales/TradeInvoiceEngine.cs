using System.Linq.Expressions;
using DevExtreme.AspNet.Data.ResponseModel;
using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// محرك الفواتير المشترك: البناء والتحقق والتسعير والترحيل (مخزون + قيد + QR) والمرتجعات والقيد، لكل جداول الفواتير المستقلة.
/// <typeparamref name="T"/> في كل عملية هو جدول المستند: SalesInvoice / PurchaseInvoice / SalesReturn / PurchaseReturn
/// للتجارة العامة، أو Invoice لفواتير المعارض (بامتداداتها IInvoiceExtension). لا يُكتب مستند في غير جدوله:
/// فاتورة المعارض (أسطر سيارات، مستند معرض مرجعي، أو مرتجع لأصل معرض) لا تُقبل في جداول التجارة، والعكس.
/// </summary>
public class TradeInvoiceEngine
{
    private readonly ErpDbContext _db;
    private readonly IAccountingPostingService _posting;
    private readonly IInventoryService _inventory;
    private readonly INumberSequenceService _numbers;
    private readonly IZatcaService _zatca;
    private readonly ITransactionRunner _tx;
    private readonly ERP.Core.Contracts.Shared.IAuditService _audit;
    /// <summary>امتدادات الوحدات (المعارض) على دورة فواتيرها: تعمل على جدول Invoice وحده، ومنطقها خارج المحرك.</summary>
    private readonly IReadOnlyList<IInvoiceExtension> _extensions;
    private readonly IApprovalService _approvals;
    private readonly ERP.Core.Contracts.Shared.IPermissionService _permissions;
    private readonly ERP.Core.Contracts.Shared.ISubscriptionEntitlements _entitlements;
    private readonly IPeriodLock _periods;
    /// <summary>تسوية استحقاق إيجار المعدات المستأجرة بفواتير المؤجر ومرتجعاته (بعد ترحيلها أو عكس قيدها عند حذفها).</summary>
    private readonly IRentalAccrualService _rentals;
    /// <summary>تجاوزات التصنيف الضريبي التي أقرّها آخر بناء؛ تُسجَّل في سجل التدقيق بعد حفظ المستند.</summary>
    private List<ItemTaxTreatment.Override> _taxOverrides = new();

    public TradeInvoiceEngine(ErpDbContext db, IAccountingPostingService posting, IInventoryService inventory,
        INumberSequenceService numbers, IZatcaService zatca, ITransactionRunner tx, ERP.Core.Contracts.Shared.IAuditService audit,
        IEnumerable<IInvoiceExtension> extensions, IApprovalService approvals, ERP.Core.Contracts.Shared.IPermissionService permissions,
        ERP.Core.Contracts.Shared.ISubscriptionEntitlements entitlements, IPeriodLock periods, IRentalAccrualService rentals)
    {
        _entitlements = entitlements; _periods = periods; _rentals = rentals;
        _audit = audit; _extensions = extensions.ToList(); _approvals = approvals; _permissions = permissions;
        _db = db; _posting = posting; _inventory = inventory; _numbers = numbers; _zatca = zatca; _tx = tx;
    }

    /// <summary>نوع المستند الثابت لجدول تجارة عامة؛ null لجدول المعارض (كل الأنواع).</summary>
    public static InvoiceKind? KindOf<T>() where T : InvoiceBase
        => typeof(T) == typeof(SalesInvoice) ? InvoiceKind.Sales
            : typeof(T) == typeof(PurchaseInvoice) ? InvoiceKind.Purchase
            : typeof(T) == typeof(SalesReturn) ? InvoiceKind.SalesReturn
            : typeof(T) == typeof(PurchaseReturn) ? InvoiceKind.PurchaseReturn
            : null;

    private static bool IsShowroom<T>() => typeof(T) == typeof(Invoice);

    /// <summary>الرأس ببنوده وتقسيمات دفعه (وأسطر السيارات لفواتير المعارض).</summary>
    private static IQueryable<T> Detailed<T>(IQueryable<T> q) where T : InvoiceBase
    {
        q = q.Include(nameof(InvoiceBase<InvoiceItem>.Items)).Include(i => i.PaymentSplits);
        if (IsShowroom<T>()) q = q.Include(nameof(Invoice.VehicleLines)).Include(nameof(Invoice.VehicleBatches));
        return q.AsSplitQuery();
    }

    // ---------------- القراءة ----------------
    /// <summary>
    /// القوائم لا تعرض مسودة لم تُعتمد: آخر طلب اعتماد عليها معلّق أو مرفوض. المُعادة للتعديل والمسحوبة تبقى ظاهرة
    /// لمقدّمها ليعدّلها ويعيد إرسالها، والمستند المرحَّل ظاهر دائماً.
    /// </summary>
    private IQueryable<T> WithoutUnapproved<T>(IQueryable<T> q) where T : InvoiceBase
        => q.Where(i => i.Status != "draft" || !_db.Set<ApprovalRequest>().Where(r => r.DocumentId == i.Id)
            .OrderByDescending(r => r.CreatedAt).Take(1).Any(r => r.Status == "pending" || r.Status == "rejected"));

    public async Task<PagedResult<InvoiceDto>> ListAsync<T>(InvoiceKind? kind, PaginationParams p, Expression<Func<T, bool>>? filter, CancellationToken ct)
        where T : InvoiceBase, new()
    {
        var q = _db.Set<T>().AsNoTracking().AsQueryable();
        if (kind.HasValue) q = q.Where(i => i.Kind == kind);
        if (filter != null) q = q.Where(filter);
        q = WithoutUnapproved(q);
        if (!string.IsNullOrWhiteSpace(p.Status)) q = q.Where(i => i.Status == p.Status);
        if (p.StartDate.HasValue) q = q.Where(i => i.IssueDate >= p.StartDate);
        if (p.EndDate.HasValue) q = q.Where(i => i.IssueDate <= p.EndDate);
        if (!string.IsNullOrWhiteSpace(p.SearchTerm))
        {
            var t = p.SearchTerm.Trim();
            q = q.Where(i => i.InvoiceNumber.Contains(t) || i.PartyName.Contains(t) || (i.PartyVatNumber != null && i.PartyVatNumber.Contains(t)));
        }
        var total = await q.CountAsync(ct);
        var items = await Detailed(q.OrderByDescending(i => i.IssueDate).ThenByDescending(i => i.InvoiceNumber)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize)).ToListAsync(ct);
        var due = await InvoiceBalances.ForAsync(_db, items.Where(i => i.Status == "posted").Select(i => i.Id).ToList(), null, ct);
        var rows = items.Select(i => (Invoice: i, Dto: ToDto(i, due.GetValueOrDefault(i.Id)?.AmountDue ?? 0))).ToList();
        await ApplyProjectNamesAsync(rows, ct);
        await ApplyDeliveryAsync(rows, ct);
        return new PagedResult<InvoiceDto>
        {
            Items = await MaskAsync(rows, ct),
            TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize,
        };
    }

    /// <summary>القائمة بخيارات DevExtreme: الفلترة والفرز والترقيم في SQL، والمتبقي يُحسب لفواتير الصفحة فقط.</summary>
    public Task<LoadResult> LoadAsync<T>(InvoiceKind? kind, DataSourceLoadOptions options, Expression<Func<T, bool>>? filter, CancellationToken ct)
        where T : InvoiceBase, new()
    {
        var q = _db.Set<T>().AsNoTracking().AsQueryable();
        if (kind.HasValue) q = q.Where(i => i.Kind == kind);
        if (filter != null) q = q.Where(filter);
        q = WithoutUnapproved(q);
        return EntityLoader.LoadAsync(Detailed(q), options, async (page, token) =>
        {
            var due = await InvoiceBalances.ForAsync(_db, page.Where(i => i.Status == "posted").Select(i => i.Id).ToList(), null, token);
            var rows = page.Select(i => (Invoice: i, Dto: ToDto(i, due.GetValueOrDefault(i.Id)?.AmountDue ?? 0))).ToList();
            await ApplyProjectNamesAsync(rows, token);
            await ApplyDeliveryAsync(rows, token);
            return await MaskAsync(rows, token);
        }, ct, EntityLoader.Desc(nameof(InvoiceBase.IssueDate)), EntityLoader.Desc(nameof(InvoiceBase.InvoiceNumber)));
    }

    public async Task<InvoiceDto> GetAsync<T>(Guid id, CancellationToken ct) where T : InvoiceBase, new()
    {
        var invoice = await Detailed(_db.Set<T>().AsNoTracking()).FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new NotFoundException(Messages.InvoiceNotFound);
        var dto = ToDto(invoice, invoice.Status == "posted" ? await InvoiceBalances.DueAsync(_db, id, ct) : 0);
        var single = new List<(T Invoice, InvoiceDto Dto)> { (invoice, dto) };
        await ApplyProjectNamesAsync(single, ct);
        await ApplyDeliveryAsync(single, ct);
        if (invoice is Invoice car) foreach (var ext in _extensions) await ext.FillAsync(car, dto, ct);
        return (await MaskAsync(new List<(T, InvoiceDto)> { (invoice, dto) }, ct))[0];
    }

    private static InvoiceDto ToDto(InvoiceBase invoice, decimal amountDue)
    {
        var dto = Mapper.Map<InvoiceDto>(invoice);
        dto.JournalCostCenters = JournalCostCenterMap.Unpack(invoice.JournalCostCentersJson);
        dto.AmountDue = amountDue;
        var sourceLines = invoice.Lines.ToDictionary(line => line.Id);
        foreach (var item in dto.Items)
            if (sourceLines.TryGetValue(item.Id, out var line))
            {
                item.ProjectResourceId = line.ProjectResourceId;
                item.OriginalInvoiceItemId = line.OriginalInvoiceItemId;
                item.WarehouseId = SiteOf(invoice, line); // السطر القديم بلا موقع = موقع المستند
                if (line.ComponentsJson != null) item.Components = InvoiceLineStock.Components(line);
            }
        return dto;
    }

    public async Task<List<RentalInvoiceResourceDto>> RentalResourcesAsync(Guid supplierId, CancellationToken ct = default)
        => await (from resource in _db.Set<ProjectResource>().AsNoTracking()
                  join contract in _db.Set<CommercialContract>().AsNoTracking() on resource.ContractId equals contract.Id
                  where resource.SupplierId == supplierId && resource.Kind == ProjectResourceKinds.RentedEquipment
                      && contract.CostCenterId.HasValue
                  orderby contract.ProjectName, resource.Name
                  select new RentalInvoiceResourceDto
                  {
                      Id = resource.Id,
                      ResourceName = resource.Name,
                      ProjectName = string.IsNullOrWhiteSpace(contract.ProjectName) ? contract.ContractNumber : contract.ProjectName,
                      ProjectNameEn = string.IsNullOrWhiteSpace(contract.ProjectNameEn) ? contract.ContractNumber : contract.ProjectNameEn,
                      ContractNumber = contract.ContractNumber,
                      CostCenterId = contract.CostCenterId!.Value,
                  }).ToListAsync(ct);

    /// <summary>
    /// حالة التسليم لفواتير البيع وحالة الاستلام لفواتير المشتريات المرحّلة (عرض فقط، من بيانات التسليم وسندات الاستلام على الفاتورة).
    /// </summary>
    private async Task ApplyDeliveryAsync<T>(IReadOnlyCollection<(T Invoice, InvoiceDto Dto)> rows, CancellationToken ct) where T : InvoiceBase
    {
        var sales = rows.Select(r => r.Invoice).OfType<SalesInvoice>().ToList();
        var purchases = rows.Select(r => r.Invoice).OfType<PurchaseInvoice>().ToList();
        var states = sales.Count > 0 ? await DeliverySources.SalesInvoiceDeliveryAsync(_db, sales, ct)
            : purchases.Count > 0 ? await PurchaseReceiptQuantities.InvoiceReceiptStatusAsync(_db, purchases, ct)
            : null;
        if (states == null) return;
        foreach (var (invoice, dto) in rows)
            if (states.TryGetValue(invoice.Id, out var d)) { dto.DeliveryStatus = d.Status; dto.DeliveryRemaining = d.Remaining; }
    }

    private async Task ApplyProjectNamesAsync<T>(IReadOnlyCollection<(T Invoice, InvoiceDto Dto)> rows, CancellationToken ct)
        where T : InvoiceBase
    {
        await ApplyLineNamesAsync(rows.SelectMany(row => row.Dto.Items).ToList(), ct);
        var requisitionIds = rows
            .Where(row => row.Invoice.Kind == InvoiceKind.Purchase
                && row.Invoice.ReferenceType == "material_requisition"
                && row.Invoice.ReferenceId.HasValue)
            .Select(row => row.Invoice.ReferenceId!.Value)
            .Distinct()
            .ToList();

        // فواتير العقود (المستخلصات والدفعات والمحتجزات وفواتير الباطن): المشروع من العقد المرتبط بالفاتورة،
        // وعقد الباطن بلا اسم مشروع يأخذ اسم مشروع عقده الرئيسي
        var invoiceIds = rows.Select(row => row.Invoice.Id).ToList();
        var contractProjects = (await (
            from link in _db.Set<ContractInvoice>().AsNoTracking()
            join contract in _db.Set<CommercialContract>().AsNoTracking() on link.ContractId equals contract.Id
            join parent in _db.Set<CommercialContract>().AsNoTracking() on contract.ParentContractId equals (Guid?)parent.Id into parents
            from parent in parents.DefaultIfEmpty()
            where link.InvoiceId != null && invoiceIds.Contains(link.InvoiceId.Value)
            select new
            {
                InvoiceId = link.InvoiceId!.Value,
                ProjectName = string.IsNullOrWhiteSpace(contract.ProjectName) && parent != null ? parent.ProjectName : contract.ProjectName,
                ProjectNameEn = string.IsNullOrWhiteSpace(contract.ProjectNameEn) && parent != null ? parent.ProjectNameEn : contract.ProjectNameEn,
            }).ToListAsync(ct))
            .GroupBy(x => x.InvoiceId).ToDictionary(g => g.Key, g => g.First());
        foreach (var row in rows)
            if (contractProjects.TryGetValue(row.Invoice.Id, out var cp) && !string.IsNullOrWhiteSpace(cp.ProjectName ?? cp.ProjectNameEn))
            {
                row.Dto.ProjectName = cp.ProjectName;
                row.Dto.ProjectNameEn = cp.ProjectNameEn;
            }

        if (requisitionIds.Count == 0) return;

        var projects = await (
            from requisition in _db.Set<MaterialRequisition>().AsNoTracking()
            join contract in _db.Set<CommercialContract>().AsNoTracking()
                on requisition.ContractId equals (Guid?)contract.Id
            where requisitionIds.Contains(requisition.Id)
            select new { RequisitionId = requisition.Id, contract.ProjectName, contract.ProjectNameEn }
        ).ToDictionaryAsync(project => project.RequisitionId, ct);

        foreach (var row in rows)
        {
            if (row.Invoice.Kind == InvoiceKind.Purchase
                && row.Invoice.ReferenceType == "material_requisition"
                && row.Invoice.ReferenceId is Guid requisitionId
                && projects.TryGetValue(requisitionId, out var project))
            {
                row.Dto.ProjectName = project.ProjectName;
                row.Dto.ProjectNameEn = project.ProjectNameEn;
            }
        }
    }

    /// <summary>حجب ما تحجبه الامتدادات (تكلفة السيارات) عن نتائج فواتير المعارض.</summary>
    private async Task<List<InvoiceDto>> MaskAsync<T>(List<(T Invoice, InvoiceDto Dto)> rows, CancellationToken ct) where T : InvoiceBase
    {
        if (IsShowroom<T>())
        {
            var cars = rows.Select(x => ((Invoice)(object)x.Invoice, x.Dto)).ToList();
            foreach (var ext in _extensions) await ext.MaskAsync(cars, ct);
        }
        return rows.Select(x => x.Dto).ToList();
    }

    private async Task EnsureOpenPeriodAsync(DateTime issueDate, CancellationToken ct)
        => await _periods.EnsureOpenAsync(issueDate == default ? SaudiTime.Today : issueDate, ct);

    // ---------------- الإنشاء ----------------
    public async Task<InvoiceDto> CreateAsync<T>(CreateInvoiceDto r, CancellationToken ct) where T : InvoiceBase, new()
        => await SendToZatcaIfRequestedAsync<T>(await _tx.RunAsync(async token =>
        {
            await EnsureOpenPeriodAsync(r.IssueDate, token);
            r.ExistingInvoiceId = null;
            var invoice = await BuildAsync<T>(r, token);
            var draft = string.Equals(r.Status, "draft", StringComparison.OrdinalIgnoreCase);
            invoice.Status = "draft";
            // المسودة برقم مؤقت؛ الرقم المتسلسل يُصرف عند الترحيل فلا يترك حذف مسودة فجوة في تسلسل الفواتير
            invoice.InvoiceNumber = DraftPrefix + invoice.Id.ToString("N")[..10].ToUpperInvariant();
            // ترقيم يدوي لهذا النوع: الرقم المُدخل (بعد التحقق من صيغته وتكراره) من الإنشاء ولو مسودة
            if (await ManualNumberAsync(invoice, r.InvoiceNumber, r.SystemGenerated, token) is { } manual) invoice.InvoiceNumber = manual;
            _db.Add(invoice);
            await OnSavingAsync(invoice, deleting: false, token);
            await ClaimSourceAsync(invoice, token);
            await _db.SaveChangesAsync(token);
            // سياسة اعتماد منطبقة: تبقى مسودة بطلبها، والترحيل عند الموافقة
            if (!draft && !(r.ApplyApprovalPolicy && await RequiresApprovalAsync(invoice, token))) await FinalizeAsync(invoice, token);
            await LogTaxOverridesAsync(invoice, token); // بعد الترحيل: برقم الفاتورة النهائي
            return await GetAsync<T>(invoice.Id, token);
        }, ct), ct);

    /// <summary>يقيّم سياسات «إنشاء» نوع المستند وينشئ طلب الاعتماد داخل معاملة الإنشاء (يُلغى معها إن فشلت).</summary>
    private async Task<bool> RequiresApprovalAsync(InvoiceBase invoice, CancellationToken ct)
    {
        var documentType = invoice.Kind switch
        {
            InvoiceKind.Sales => "sales",
            InvoiceKind.Purchase => "purchase",
            InvoiceKind.SalesReturn => "sales_return",
            _ => "purchase_return",
        };
        var check = await _approvals.CheckAndCreateAsync(new ApprovalCheckRequestDto
        {
            DocumentType = documentType, ActionType = "create", DocumentId = invoice.Id,
            DocumentNumber = invoice.InvoiceNumber, DocumentAmount = invoice.GrandTotal,
            DocumentDataSnapshot = ApprovalSnapshot(ToDto(invoice, invoice.GrandTotal)),
        }, ct);
        return check.ApprovalRequired;
    }

    /// <summary>حالة آخر طلب اعتماد على المستند؛ null إن لم يُطلب له اعتماد.</summary>
    private Task<string?> LatestApprovalAsync(Guid id, CancellationToken ct)
        => _db.Set<ApprovalRequest>().Where(r => r.DocumentId == id).OrderByDescending(r => r.CreatedAt)
            .Select(r => (string?)r.Status).FirstOrDefaultAsync(ct);

    private static readonly System.Text.Json.JsonSerializerOptions SnapshotJson = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.SnakeCaseLower) },
    };

    /// <summary>ما يراه المعتمِد من المستند: الرأس والبنود والإجماليات فقط (بلا بيانات الهيئة والحسابات).</summary>
    private static string ApprovalSnapshot(InvoiceDto d) => System.Text.Json.JsonSerializer.Serialize(new
    {
        d.Kind, d.InvoiceNumber, d.IssueDate, d.PartyName, d.PartyVatNumber, d.WarehouseName, d.PaymentMethod,
        d.CurrencyCode, d.ReferenceNumber, d.OriginalInvoiceNumber, d.ReturnReason, d.Notes,
        Items = d.Items.Select(l => new { l.ItemName, l.Unit, l.Quantity, l.UnitPrice, l.Discount, l.VatAmount, l.TotalAfterVat }),
        d.Subtotal, d.DiscountTotal, d.VatTotal, d.GrandTotal,
    }, SnapshotJson);

    public Task<InvoiceDto> UpdateAsync<T>(Guid id, UpdateInvoiceDto r, CancellationToken ct) where T : InvoiceBase, new()
        => UpdateCoreAsync<T>(id, r, null, ct);

    /// <summary>
    /// إعادة بناء المرتجع بعد إلغاء ترحيله: تُرجع طلب المستند الجديد، وخطوة اختيارية بعد إعادة ترحيله (إلحاق سيارات المرتجع).
    /// </summary>
    private delegate Task<(UpdateInvoiceDto Request, Func<InvoiceBase, CancellationToken, Task>? AfterPost)> ReturnRebuilder(InvoiceBase current, CancellationToken ct);

    /// <summary>
    /// تعديل الفاتورة: بطلب جاهز (<paramref name="given"/>)، أو بطلب يُبنى بعد إلغاء ترحيل المستند (<paramref name="rebuild"/>،
    /// للمرتجع: يُعاد بناؤه من أصله بعد أن تعود كمياته وسياراته).
    /// </summary>
    private async Task<InvoiceDto> UpdateCoreAsync<T>(Guid id, UpdateInvoiceDto? given, ReturnRebuilder? rebuild, CancellationToken ct) where T : InvoiceBase, new()
        => await SendToZatcaIfRequestedAsync<T>(await _tx.RunAsync(async token =>
        {
            var invoice = await Detailed(_db.Set<T>()).FirstOrDefaultAsync(i => i.Id == id, token) ?? throw new NotFoundException(Messages.InvoiceNotFound);
            var returnEdit = rebuild != null;
            if (returnEdit)
            {
                if (!invoice.IsReturn || !invoice.OriginalInvoiceId.HasValue) throw new ConflictException(Messages.CannotChangeDocumentType);
                await EnsureOpenPeriodAsync(invoice.IssueDate, token);
                // المرسل للهيئة لا يُعدَّل إلا المرفوض: يُصحَّح ويُعاد بمعرّف جديد
                if (invoice.ZatcaStatus is not (ZatcaSubmissionStatus.NotSubmitted or ZatcaSubmissionStatus.Rejected))
                    throw new ConflictException(Messages.PostedInvoiceEditZatcaSubmitted);
            }
            else
            {
                await EnsureOpenPeriodAsync(given!.IssueDate == default ? invoice.IssueDate : given.IssueDate, token);
                if (given.Kind != invoice.Kind) throw new ConflictException(Messages.CannotChangeDocumentType);
            }
            // المرحّلة (قبل إرسالها للهيئة): يُعكس قيدها وحركاتها المخزنية ثم يُعاد ترحيلها بنفس رقمها بعد التعديل
            var wasPosted = invoice.Status == "posted";
            if (wasPosted) await UnpostForEditAsync(invoice, token, allowReturn: returnEdit);
            else
            {
                EnsureNotLinkedToAnotherDocument(invoice);
                if (invoice.Status != "draft") throw new ConflictException(Messages.CannotEditCancelledInvoice);
            }

            // المرتجع يُبنى من أصله الآن: كمياته وسياراته عادت فلا يُحسب «مرتجعاً من قبل» (يُستثنى بمعرّفه أيضاً)
            Func<InvoiceBase, CancellationToken, Task>? afterPost = null;
            UpdateInvoiceDto r;
            if (returnEdit)
            {
                (r, afterPost) = await rebuild!(invoice, token);
                if (r.Kind != invoice.Kind) throw new ConflictException(Messages.CannotChangeDocumentType);
                await EnsureOpenPeriodAsync(r.IssueDate == default ? invoice.IssueDate : r.IssueDate, token);
            }
            else r = given!;

            r.ExistingInvoiceId = id; // شواصي هذه المسودة المحفوظة ليست تكراراً
            var fresh = await BuildAsync<T>(r, token); // نفس تحقق الإنشاء وحساب الأرقام في السرفر
            // المرفوضة من الهيئة: تُعاد بعد التصحيح كمستند جديد لديها (معرّف UUID جديد، فالهيئة لا تقبل تكراره) وبرقمها نفسه؛
            // ومحاولتها المرفوضة تبقى حلقة في السلسلة ومسجّلة في سجل التدقيق. حالة الهيئة وبصمتها تُصفَّر مع البناء الجديد
            var wasRejected = invoice.ZatcaStatus == ZatcaSubmissionStatus.Rejected;
            var (keepId, keepTenant, keepCreated, keepNumber, keepUuid) = (invoice.Id, invoice.TenantId, invoice.CreatedAt, invoice.InvoiceNumber, wasRejected ? fresh.Uuid : invoice.Uuid);
            var (keepRefType, keepRefId) = (invoice.ReferenceType, invoice.ReferenceId); // ارتباط المرحّلة بمستندها التجاري يبقى
            var keepEntry = invoice.JournalEntryId; // قيد المرحّلة يُستبدل في مكانه عند إعادة الترحيل

            _db.RemoveRange(invoice.Lines.ToList());
            _db.RemoveRange(invoice.PaymentSplits);
            invoice.ClearLines();
            invoice.PaymentSplits.Clear();
            if (invoice is Invoice car)
            {
                _db.RemoveRange(car.VehicleLines);
                _db.RemoveRange(car.VehicleBatches);
                car.VehicleLines.Clear();
                car.VehicleBatches.Clear();
            }
            Mapper.Apply(fresh, invoice);
            (invoice.Id, invoice.TenantId, invoice.CreatedAt, invoice.InvoiceNumber, invoice.Uuid) = (keepId, keepTenant, keepCreated, keepNumber, keepUuid);
            // الترقيم اليدوي: المسودة يُعتمد/يُغيَّر رقمها المُدخل، والمرحّلة لا يتغير رقمها
            var requestedNumber = r.InvoiceNumber?.Trim();
            if (!string.IsNullOrEmpty(requestedNumber) && requestedNumber != keepNumber && await IsManualAsync(invoice, r.SystemGenerated, token))
            {
                if (wasPosted) throw new ConflictException(Messages.DocumentNumberLocked);
                invoice.InvoiceNumber = (await ManualNumberAsync(invoice, requestedNumber, r.SystemGenerated, token))!;
            }
            // ارتباط الفاتورة بمستندها المصدر يبقى (المرحّلة والمسودة المحوَّلة): الشاشة لا تعيد إرساله عند التعديل،
            // وبه تُحسب كميات طلب الشراء «مشتراة» في المشروع ويُفَك عند حذفها
            (invoice.ReferenceType, invoice.ReferenceId) = (keepRefType, keepRefId);
            if (wasPosted) invoice.JournalEntryId = keepEntry;
            invoice.Status = "draft";
            foreach (var item in fresh.Lines.ToList()) invoice.AddLine(item);
            foreach (var split in fresh.PaymentSplits) invoice.PaymentSplits.Add(split);
            if (invoice is Invoice target && fresh is Invoice source)
            {
                foreach (var vl in source.VehicleLines) target.VehicleLines.Add(vl);
                foreach (var batch in source.VehicleBatches)
                {
                    target.VehicleBatches.Add(batch);
                    _db.Add(batch); // معرّفها محدَّد مسبقاً (تشير إليه شواصيها)، فتُضاف صراحةً
                }
            }
            await OnSavingAsync(invoice, deleting: false, token);
            await _db.SaveChangesAsync(token);

            // المرحّلة تعود مرحّلة دائماً (بنفس رقمها) بقيد وحركات مخزنية جديدة
            var posting = wasPosted || !string.Equals(r.Status, "draft", StringComparison.OrdinalIgnoreCase);
            // مسودة لم تُعتمد (ومنها المرفوضة بعد تصحيحها): حفظها للترحيل يرسلها للاعتماد إن انطبقت سياسة
            if (posting && !wasPosted && !IsShowroom<T>() && await LatestApprovalAsync(id, token) != "approved" && await RequiresApprovalAsync(invoice, token))
                posting = false;
            if (posting) await FinalizeAsync(invoice, token);
            if (posting && afterPost != null) await afterPost(invoice, token);
            await LogTaxOverridesAsync(invoice, token);
            if (wasPosted)
                await _audit.LogAsync(wasRejected ? "INVOICE_ZATCA_REJECTED_EDITED" : returnEdit ? "RETURN_POSTED_EDITED" : "INVOICE_POSTED_EDITED", typeof(T).Name, invoice.Id.ToString(),
                    $"تعديل {DescribeKind(invoice.Kind)} مرحّلة {invoice.InvoiceNumber}{(wasRejected ? $" بعد رفض الهيئة (معرّف جديد {invoice.Uuid})" : string.Empty)}: عُكس قيدها وحركاتها وأُعيد ترحيلها بإجمالي {invoice.GrandTotal:0.00} - {invoice.PartyName}", token);
            return await GetAsync<T>(id, token);
        }, ct), ct);

    // امتدادات الوحدات تعمل على فواتيرها (جدول المعارض) وحده
    private async Task OnSavingAsync(InvoiceBase invoice, bool deleting, CancellationToken ct)
    {
        if (invoice is Invoice car) foreach (var ext in _extensions) await ext.OnSavingAsync(car, deleting, ct);
    }

    /// <summary>
    /// يلغي أثر ترحيل فاتورة مبيعات/مشتريات تمهيداً لتعديلها وإعادة ترحيلها: يعكس قيدها (بتاريخه، في فترة مفتوحة)
    /// ويعكس حركاتها المخزنية (المبيعات تعود بتكلفة خروجها، والمشتريات تخرج بتكلفتها). يُمنع ما لا يُصحَّح إلا بمرتجع:
    /// المرسلة للهيئة، المرتجعات وما عليه مرتجعات أو سندات موزّعة، فواتير السيارات ومستندات النقاط والعقود،
    /// والمشتريات التي تلتها حركات على أصنافها (يتغيّر بها المتوسط) أو بطريقة الوارد أولاً.
    /// المرتجع (<paramref name="allowReturn"/>: حذفه، أو تعديله من أصله عبر UpdateReturnAsync) يُلغى أثره بالقاعدة نفسها
    /// قبل إرساله للهيئة: مرتجع المبيعات تخرج كمياته بتكلفة دخولها، ومرتجع المشتريات تعود كمياته بتكلفة خروجها.
    /// </summary>
    private async Task UnpostForEditAsync(InvoiceBase invoice, CancellationToken ct, bool forDelete = false, bool allowReturn = false)
    {
        var isReturnDoc = invoice.IsReturn || invoice.OriginalInvoiceId.HasValue;
        var editableKind = isReturnDoc
            ? allowReturn && invoice.Kind is InvoiceKind.SalesReturn or InvoiceKind.PurchaseReturn
            : invoice.Kind is InvoiceKind.Sales or InvoiceKind.Purchase;
        if (!editableKind) throw new ConflictException(isReturnDoc ? Messages.PostedReturnEditFromReturnScreen : Messages.PostedInvoiceLocked);
        // فاتورة بيع عليها بيانات تسليم قائمة (أسطرها ومواقعها وكمياتها مرجع التسليم): تُلغى البيانات أولاً
        if (invoice.Kind == InvoiceKind.Sales && !isReturnDoc)
        {
            var notes = await _db.Set<DeliveryNote>().AsNoTracking()
                .Where(n => n.SourceType == DeliveryNoteSources.SalesInvoice && n.SourceId == invoice.Id && n.Status != DeliveryNoteStatus.Cancelled)
                .Select(n => n.DeliveryNumber).ToListAsync(ct);
            if (notes.Count > 0) throw new ConflictException(string.Format(Messages.InvoiceHasDeliveryNotes, string.Join("، ", notes)));
        }
        // فاتورة مشتريات عليها سندات استلام قائمة (أسطرها وكمياتها مرجع الاستلام): تُحذف السندات أولاً (حماية مستقلة عن المبيعات)
        if (invoice.Kind == InvoiceKind.Purchase && !isReturnDoc)
        {
            var receipts = await _db.Set<DeliveryNote>().AsNoTracking()
                .Where(n => n.SourceType == DeliveryNoteSources.PurchaseInvoice && n.SourceId == invoice.Id && n.Status != DeliveryNoteStatus.Cancelled)
                .Select(n => n.DeliveryNumber).ToListAsync(ct);
            if (receipts.Count > 0) throw new ConflictException(string.Format(Messages.PurchaseInvoiceHasReceipts, string.Join("، ", receipts)));
        }
        // المرسلة للهيئة لا تُعدَّل، إلا المرفوضة: تُصحَّح وتُعاد بمعرّف جديد (الحذف ممنوع لها أيضاً: صارت حلقة في السلسلة)
        if (invoice.ZatcaStatus != ZatcaSubmissionStatus.NotSubmitted && (forDelete || invoice.ZatcaStatus != ZatcaSubmissionStatus.Rejected))
            throw new ConflictException(Messages.PostedInvoiceEditZatcaSubmitted);
        // فاتورة نقطة بيع/عقد: مجاميع مستندها المصدر (الوردية، العقد) تتبعها، فتُعدَّل من ذلك المستند
        if (invoice.ReferenceType != null && !IsCommercialSource(invoice.ReferenceType))
            throw new ConflictException(Messages.InvoiceFromSourceDocument);
        // مرتجع فاتورة مستخلص يتبع إلغاء المستخلص نفسه، فلا يُعدَّل ولا يُحذف وحده
        if (isReturnDoc && invoice.OriginalInvoiceId is Guid originalId && await _db.Set<InvoiceBase>().AsNoTracking()
                .AnyAsync(i => i.Id == originalId && (i.ReferenceType == "contract_certificate" || i.ReferenceType == "workshop_work_order" || i.ReferenceType == "rental_billing"
                    || i.ReferenceType == "tailoring_advance" || i.ReferenceType == "tailoring_delivery"), ct))
            throw new ConflictException(Messages.InvoiceFromSourceDocument);
        // تكاليف إضافية معتمدة رفعت تكلفة حركات شرائها: إعادة الترحيل تُسقطها، فيُلغى السند أولاً
        if (invoice.Kind == InvoiceKind.Purchase && await _db.Set<PurchaseLandedCostInvoice>()
                .AnyAsync(l => l.InvoiceId == invoice.Id && l.Voucher.Status == PurchaseLandedCostStatus.Approved, ct))
            throw new ConflictException(Messages.PurchaseInvoiceHasLandedCosts);

        var stockItems = invoice.Lines.Where(InvoiceLineStock.MovesItself).ToList();
        var policy = await _db.Set<CostingPolicy>().AsNoTracking().FirstOrDefaultAsync(ct) ?? new CostingPolicy();
        // ما أدخل المخزون (الشراء ومرتجع المبيعات) يُعكس بإخراج، وما أخرجه (البيع ومرتجع المشتريات) يُعكس بإدخال بتكلفة خروجه
        var stockIn = invoice.Kind is InvoiceKind.Purchase or InvoiceKind.SalesReturn;
        // إدخال تلته حركات على أصنافه: يُعكس بتكلفته الحالية، وأي فرق عن أثره الأصلي يُرحَّل بقيد إعادة تقييم أدناه
        var restoreAverage = false;
        if (stockIn && stockItems.Count > 0 && policy.Method == CostingMethod.MovingAverage)
        {
            var ids = stockItems.Select(i => i.ItemId).Distinct().ToList();
            var postedAt = await _db.Set<StockMovement>().AsNoTracking()
                .Where(m => m.SourceId == invoice.Id && m.SourceType == "invoice").MaxAsync(m => (DateTime?)m.CreatedAt, ct);
            restoreAverage = postedAt.HasValue && !await _db.Set<StockMovement>().AsNoTracking()
                .AnyAsync(m => ids.Contains(m.ItemId) && m.CreatedAt > postedAt && (m.SourceId == null || m.SourceId != invoice.Id), ct);
        }

        var reason = string.Format(forDelete ? Messages.PostedInvoiceDeleteReason : Messages.PostedInvoiceEditReason, invoice.InvoiceNumber);
        if (forDelete)
        {
            // الحذف: لا يُحذف مستند بُني عليه مرتجع أو وُزّع عليه سند (يبقى أثرهما بلا أصل)
            if (await _db.Set<InvoiceBase>().AnyAsync(i => i.OriginalInvoiceId == invoice.Id, ct)) throw new ConflictException(Messages.PostedInvoiceDeleteHasReturns);
            if (await _db.Set<VoucherAllocation>().AnyAsync(a => a.InvoiceId == invoice.Id, ct)) throw new ConflictException(Messages.PostedInvoiceDeleteHasPayments);
        }
        if (invoice is Invoice car) foreach (var ext in _extensions) await ext.OnUnpostingAsync(car, ct);
        // التعديل: يبقى القيد ويُستبدل في مكانه بنفس رقمه عند إعادة الترحيل (FinalizeAsync). الحذف: يُحذف القيد وأسطره نهائياً بلا قيد عكسي.
        if (forDelete && invoice.JournalEntryId is { } entryId)
        {
            await _posting.DeleteEntryAsync(entryId, ct);
            // فاتورة مؤجر سُوّي بها استحقاق إيجار: يُفك ما زاد عن فواتيره الباقية (التعديل يعيد الموازنة عند إعادة الترحيل)
            await _rentals.OnPurchaseDocumentChangedAsync(invoice, ct);
        }

        // أثر الفاتورة الأصلي على قيمة المخزون (ما رُحِّل لحساب المخزون): تكلفتها ناقص تكلفة بنود الخدمات
        var serviceCost = invoice.Lines.Where(i => InvoiceLineStock.IsNonStock(i) && i.UnitCost > 0).Sum(i => Math.Round(i.Quantity * i.UnitCost, 2));
        var originalStockChange = stockIn ? invoice.TotalCost - serviceCost : -(invoice.TotalCost - serviceCost);
        decimal reversalChange = 0;
        // مواقع المستند: موقع واحد = إعادة التقييم كما كانت؛ أكثر = لكل موقع على حساب مخزونه من حركات ترحيله الأخير
        var stockSites = invoice.Lines.Where(l => !InvoiceLineStock.IsNonStock(l)).Select(l => SiteOf(invoice, l)).Distinct().ToList();
        var postedBySite = stockSites.Count > 1 ? await PostedStockValueBySiteAsync(invoice.Id, ct) : null;
        var reversalBySite = new Dictionary<Guid, decimal>();
        foreach (var item in stockItems)
        {
            var recorded = await _inventory.RecordMovementAsync(new RecordStockMovementDto
            {
                // العكس بالوحدة الأساسية كما رُحِّل: الكمية × المعامل، والتكلفة والسعر لكل وحدة أساسية
                ItemId = item.ItemId, WarehouseId = SiteOf(invoice, item), Quantity = InvoiceLineStock.BaseQuantity(item),
                Type = stockIn ? StockMovementType.AdjustmentOut : StockMovementType.AdjustmentIn,
                UnitCost = stockIn ? 0 : InvoiceLineStock.BaseUnitCost(item, item.UnitCost), // المبيعات ومرتجع المشتريات تعود بتكلفة خروجها
                // المتوسط المتحرك: إخراج كمية الشراء بتكلفة شرائها يعيد متوسط ما قبل الشراء (لا شيء بعدها يغيّره)، وتحمل الحركة تلك القيمة
                OutUnitCost = restoreAverage ? InvoiceLineStock.BaseUnitCost(item, item.UnitCost) : null,
                UnitPrice = Math.Round(VatPricing.NetUnitPrice(invoice.PricingMethod, item.UnitPrice, item.VatRate) / InvoiceLineStock.Factor(item), 4), ReferenceNumber = invoice.InvoiceNumber, Date = invoice.IssueDate, Notes = reason,
                SourceType = "invoice_reversal", SourceId = invoice.Id, SourceLineId = item.Id,
                // الصنف المتتبَّع: يعود العكس لتشغيلات ترحيل هذا السطر نفسها
                BatchAllocations = await ItemBatches.OfDocumentAsync(_db, "invoice", invoice.Id, item.ItemId, item.Id, ct),
            }, ct);
            reversalChange += recorded.ValueChange;
            reversalBySite[SiteKey(SiteOf(invoice, item))] = reversalBySite.GetValueOrDefault(SiteKey(SiteOf(invoice, item))) + recorded.ValueChange;
        }
        // المجمّع والمركّب: تعود مكوناته بتكلفة خروجها المحفوظة على السطر (لا من تعريف الصنف الحالي)؛ ومرتجعه تخرج مكوناته بتكلفة دخولها
        foreach (var item in invoice.Lines.Where(InvoiceLineStock.SellsComponents))
            foreach (var c in InvoiceLineStock.Components(item).Where(c => c.Quantity > 0))
            {
                var recorded = await _inventory.RecordMovementAsync(new RecordStockMovementDto
                {
                    ItemId = c.ItemId, WarehouseId = SiteOf(invoice, item), Quantity = c.Quantity,
                    Type = stockIn ? StockMovementType.AdjustmentOut : StockMovementType.AdjustmentIn,
                    UnitCost = stockIn ? 0 : c.UnitCost, OutUnitCost = stockIn && restoreAverage ? c.UnitCost : null,
                    UnitPrice = VatPricing.NetUnitPrice(invoice.PricingMethod, item.UnitPrice, item.VatRate), ReferenceNumber = invoice.InvoiceNumber, Date = invoice.IssueDate, Notes = reason,
                    SourceType = "invoice_reversal", SourceId = invoice.Id, SourceLineId = item.Id,
                    BatchAllocations = await ItemBatches.OfDocumentAsync(_db, "invoice", invoice.Id, c.ItemId, item.Id, ct),
                }, ct);
                reversalChange += recorded.ValueChange;
                reversalBySite[SiteKey(SiteOf(invoice, item))] = reversalBySite.GetValueOrDefault(SiteKey(SiteOf(invoice, item))) + recorded.ValueChange;
            }
        await ItemBatches.MarkReversedAsync(_db, "invoice", invoice.Id, ct);
        await _db.SaveChangesAsync(ct);
        // هللات التقريب (أو إعادة تسعير آخر شراء): قيد إعادة تقييم يبقي حساب المخزون مساوياً لقيمة الأصناف
        // الفرق تسوية لأثر هذا المستند على حساب مخزون موقعه؛ المستند بحساب يدوي (السيارات) يبقى على حساب المخزون العام كما كان
        var postingAccounts = await PostingAccounts.SettingsAsync(_db, ct);
        if (postedBySite == null || !string.IsNullOrWhiteSpace(invoice.InventoryAccountCode))
        {
            var site = stockSites.Count == 1 ? stockSites[0] : invoice.WarehouseId;
            var inventoryAccount = string.IsNullOrWhiteSpace(invoice.InventoryAccountCode)
                ? await postingAccounts.InventoryAsync(_db, site, ct) : postingAccounts.Inventory();
            await InventoryValuation.PostRevaluationAsync(_db, _posting, reversalChange + originalStockChange, inventoryAccount, reason, invoice.Id, ct);
        }
        else
        {
            var byAccount = new Dictionary<string, decimal>();
            foreach (var site in stockSites)
            {
                var account = await postingAccounts.InventoryAsync(_db, site, ct);
                byAccount[account] = byAccount.GetValueOrDefault(account)
                    + reversalBySite.GetValueOrDefault(SiteKey(site)) + postedBySite.GetValueOrDefault(SiteKey(site));
            }
            await InventoryValuation.PostRevaluationAsync(_db, _posting, byAccount, reason, invoice.Id, ct);
        }
        if (forDelete) invoice.JournalEntryId = null;
    }

    public async Task<InvoiceDto> PostDraftAsync<T>(Guid id, CancellationToken ct) where T : InvoiceBase, new()
        => await SendToZatcaIfRequestedAsync<T>(await _tx.RunAsync(async token =>
        {
            // حجز المسودة بتحديث مشروط يقفل صفّها حتى نهاية المعاملة: طلب ترحيل متزامن ينتظره ثم يجدها مرحّلة فيُرفض (409)
            // بدل أن يقرأها الطلبان مسودةً فيُرحِّلانها معاً
            var claimed = await _db.Set<T>().Where(i => i.Id == id && i.Status == "draft")
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.UpdatedAt, DateTime.UtcNow), token);
            var invoice = await Detailed(_db.Set<T>()).FirstOrDefaultAsync(i => i.Id == id, token) ?? throw new NotFoundException(Messages.InvoiceNotFound);
            if (claimed != 1 || invoice.Status != "draft") throw new ConflictException(Messages.InvoiceAlreadyPostedOrCancelled);
            // سير الموافقات: المسودة المعلّقة أو المرفوضة لا تُرحَّل (آخر طلب على المستند هو المرجع)
            var approval = await LatestApprovalAsync(id, token);
            if (approval == "pending") throw new ConflictException(Messages.InvoicePendingApproval);
            if (approval == "rejected") throw new ConflictException(Messages.InvoiceApprovalRejected);
            // لم تُعتمد بعد (محوّلة من عرض سعر/أمر بيع أو محفوظة مسودة): سياسة منطبقة ترسلها للاعتماد بدل ترحيلها
            if (approval != "approved" && !IsShowroom<T>() && await RequiresApprovalAsync(invoice, token)) return await GetAsync<T>(id, token);
            await FinalizeAsync(invoice, token);
            return await GetAsync<T>(id, token);
        }, ct), ct);

    /// <summary>
    /// الإرسال لهيئة الزكاة بعد اكتمال الترحيل (خارج معاملته): لفاتورة مبيعات مرحّلة طُلب إرسالها من الشاشة أو من
    /// إعداد المنشأة. رفض الهيئة أو تعذّر الاتصال لا يُسقط الفاتورة: تبقى مرحّلة والسبب محفوظ عليها ويُعاد الإرسال.
    /// </summary>
    private async Task<InvoiceDto> SendToZatcaIfRequestedAsync<T>(InvoiceDto invoice, CancellationToken ct) where T : InvoiceBase, new()
    {
        if (!invoice.SendToZatca || invoice.Status != "posted" || invoice.ZatcaStatus != ZatcaSubmissionStatus.NotSubmitted) return invoice;
        if (invoice.Kind is not (InvoiceKind.Sales or InvoiceKind.SalesReturn)) return invoice;
        // داخل معاملة مستند آخر (نقطة بيع، عقد، مرتجع): الفاتورة لم تُثبَّت بعد، فلا تُرسل قبل اكتمال معاملته
        if (_db.Database.CurrentTransaction != null) return invoice;
        try
        {
            await _zatca.SubmitInvoiceAsync(invoice.Id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await _audit.LogAsync("ZATCA_SUBMIT_FAILED", typeof(T).Name, invoice.Id.ToString(), $"{invoice.InvoiceNumber}: {ex.Message}", ct);
        }
        return await GetAsync<T>(invoice.Id, ct);
    }

    /// <summary>
    /// إرسال فاتورة أنشأتها وحدة داخل معاملتها (عقد بيع السيارة) بعد اكتمال تلك المعاملة، بقواعد الإرسال التلقائي نفسها:
    /// المطلوب إرسالها ولم تُرسل بعد فقط، والرفض أو تعذّر الاتصال يُحفظ على الفاتورة ولا يُفشل العملية.
    /// </summary>
    public async Task<InvoiceDto> SubmitIfRequestedAsync<T>(Guid id, CancellationToken ct) where T : InvoiceBase, new()
        => await SendToZatcaIfRequestedAsync<T>(await GetAsync<T>(id, ct), ct);

    /// <summary>
    /// المرتجع يتبع فاتورته في الإرسال للهيئة: طُلب إرسالها أو وصلتها فعلاً. الإشعار الدائن لفاتورة في سلسلة الهيئة يلزم أن يصلها،
    /// وفاتورة لم تُطلب لها لا يُرسل مرتجعها.
    /// </summary>
    public static bool ReturnSendsToZatca(InvoiceBase original)
        => original.SendToZatca || original.ZatcaStatus != ZatcaSubmissionStatus.NotSubmitted;

    /// <summary>
    /// مرتجع (إشعار دائن/مدين) لفاتورة مرحّلة من جدول <typeparamref name="TOriginal"/>، يُنشأ في جدول <typeparamref name="TReturn"/>
    /// (مبيعات ← مرتجع مبيعات، مشتريات ← مرتجع مشتريات، والمعارض في جدولها).
    /// </summary>
    public async Task<InvoiceDto> CreateReturnAsync<TOriginal, TReturn>(CreateReturnInvoiceRequestDto r, CancellationToken ct)
        where TOriginal : InvoiceBase, new() where TReturn : InvoiceBase, new()
        => await SendToZatcaIfRequestedAsync<TReturn>(await _tx.RunAsync(async token =>
        {
            var original = await ReturnOriginalAsync<TOriginal>(r, token);
            if (original is Invoice car)
                foreach (var ext in _extensions)
                    if (await ext.CreateReturnAsync(r, car, CreateAsync<TReturn>, token) is { } extensionReturn) return extensionReturn;

            var dto = await BuildTradeReturnRequestAsync<TReturn>(original, r, null, token);
            return await CreateAsync<TReturn>(dto, token);
        }, ct), ct);

    /// <summary>
    /// تعديل مرتجع مرحّل لم يُرسل للهيئة (أو رفضته): يُلغى ترحيله ثم يُعاد بناؤه من أصله بالطلب نفسه الذي ينشئه (الأصناف والكميات
    /// أو السيارات، السبب، التاريخ، طريقة الرد) ويُرحَّل بقيده في مكانه وبرقمه نفسه. الأصل لا يتغير، ورقم المرتجع لا يُعدَّل.
    /// </summary>
    public Task<InvoiceDto> UpdateReturnAsync<TOriginal, TReturn>(Guid id, CreateReturnInvoiceRequestDto r, CancellationToken ct)
        where TOriginal : InvoiceBase, new() where TReturn : InvoiceBase, new()
        => UpdateCoreAsync<TReturn>(id, null, async (current, token) =>
        {
            r.OriginalInvoiceId = current.OriginalInvoiceId!.Value; // المرتجع يبقى على أصله
            r.ManualNumber = null;
            r.SystemGenerated = false;
            var original = await ReturnOriginalAsync<TOriginal>(r, token);
            // مرتجع سيارات: الوحدة تبنيه وتلحق سياراته بعد إعادة ترحيله
            if (original is Invoice car)
                foreach (var ext in _extensions)
                    if (await ext.RebuildReturnAsync(r, car, current.Id, token) is { } unit)
                    {
                        if (!r.ReturnDate.HasValue) unit.Request.IssueDate = current.IssueDate;
                        return (unit.Request, (invoice, t) => unit.AfterPost((Invoice)invoice, t));
                    }
            var dto = await BuildTradeReturnRequestAsync<TReturn>(original, r, current.Id, token);
            if (!r.ReturnDate.HasValue) dto.IssueDate = current.IssueDate; // بلا تاريخ جديد يبقى تاريخ المرتجع
            return (dto, null);
        }, ct);

    /// <summary>مدة الإرجاع بالأيام لأصل من نوع <paramref name="originalKind"/> (مبيعات أو مشتريات)؛ null أو 0 = بلا حد.</summary>
    public async Task<int?> ReturnPeriodDaysAsync(InvoiceKind originalKind, CancellationToken ct)
    {
        var settings = await PostingAccounts.SettingsAsync(_db, ct);
        var days = originalKind == InvoiceKind.Purchase ? settings.PurchaseReturnPeriodDays : settings.SalesReturnPeriodDays;
        return days is > 0 ? days : null;
    }

    /// <summary>يرفض مرتجعاً بتاريخ <paramref name="returnDate"/> بعد انقضاء مدة الإرجاع من تاريخ أصله.</summary>
    private async Task EnsureWithinReturnPeriodAsync(InvoiceBase original, DateTime returnDate, CancellationToken ct)
    {
        var periodDays = await ReturnPeriodDaysAsync(original.Kind, ct);
        if (periodDays.HasValue && (returnDate.Date - original.IssueDate.Date).TotalDays > periodDays)
            throw new ConflictException(string.Format(Messages.ReturnPeriodExpired, periodDays, original.InvoiceNumber));
    }

    /// <summary>أصل المرتجع وتحقق طلبه (إنشاءً وتعديلاً): أصل مرحّل، ليس مستخلصاً ولا دفعة مقدمة، سبب، وطريقة رد صالحة.</summary>
    private async Task<InvoiceBase> ReturnOriginalAsync<TOriginal>(CreateReturnInvoiceRequestDto r, CancellationToken token) where TOriginal : InvoiceBase, new()
    {
        var original = await Detailed(_db.Set<TOriginal>().AsNoTracking()).FirstOrDefaultAsync(i => i.Id == r.OriginalInvoiceId, token)
            ?? throw new NotFoundException(Messages.OriginalInvoiceNotFound);
        if (original.Status != "posted" || original.IsReturn) throw new ConflictException(Messages.OnlyPostedOriginalInvoiceCanBeReturned);
        // فاتورة المستخلص تُلغى من المستخلص نفسه (يعكس معها قيدي الاسترداد والتكلفة ويعيد كمياته)؛ المرتجع الحر يفصلها عن مستخلصها
        if (original.ReferenceType == "contract_certificate" && !r.SystemGenerated)
            throw new ConflictException("فاتورة المستخلص تُلغى من شاشة المستخلصات (إلغاء الفاتورة) لا بمرتجع مستقل.");
        // فاتورة أمر عمل الورشة تُلغى من الأمر نفسه (يعكس معها قيد التكلفة ويعيده «جاهزاً»)
        if (original.ReferenceType == "workshop_work_order" && !r.SystemGenerated)
            throw new ConflictException(Messages.WorkOrderInvoiceReturnBlocked);
        // فاتورة فوترة التأجير تُلغى من الفوترة نفسها (تعيد المفوتر والرسوم)
        if (original.ReferenceType == "rental_billing" && !r.SystemGenerated)
            throw new ConflictException(Messages.RentalInvoiceReturnBlocked);
        // فواتير الخياطة (الدفعة المقدمة والتسليم) تُلغى من أمر التفصيل نفسه (يعيد العربون أو القطع المسلّمة)
        if (original.ReferenceType is "tailoring_advance" or "tailoring_delivery" && !r.SystemGenerated)
            throw new ConflictException(Messages.TailoringInvoiceReturnBlocked);
        // فاتورة الدفعة المقدمة تستردها المستخلصات (وتخفض بها أساسها الضريبي): مرتجعها الحر يتركها تُسترد وهي ملغاة
        if (!r.SystemGenerated && await _db.Set<ContractInvoice>().AsNoTracking()
                .AnyAsync(i => i.InvoiceId == original.Id && i.Kind == "advance", token))
            throw new ConflictException(Messages.AdvanceInvoiceReturnBlocked);
        if (string.IsNullOrWhiteSpace(r.ReturnReason)) throw new ValidationFailedException(Messages.ReturnReasonRequired);
        if (r.RefundPaymentMethod == PaymentMethod.ContractSettlement) throw new ValidationFailedException(Messages.InvalidPaymentMethod);
        if (r.RefundPaymentMethod.HasValue)
            r.RefundPaymentMethod = await PaymentMethodRules.MethodForAsync(_db, r.RefundPaymentMethod.Value, r.RefundPaymentMethodCode, token);
        return original;
    }

    /// <summary>طلب مرتجع أصناف من أصله: الكميات ضمن المتبقي (بلا المرتجع <paramref name="excludeReturnId"/> عند تعديله) وطريقة الرد.</summary>
    private async Task<UpdateInvoiceDto> BuildTradeReturnRequestAsync<TReturn>(InvoiceBase original, CreateReturnInvoiceRequestDto r, Guid? excludeReturnId, CancellationToken token)
        where TReturn : InvoiceBase
    {
        var originalLines = original.Lines.ToList();
        var perSourceLine = ReturnsPerSourceLine(original);
        var previouslyReturned = await PreviouslyReturnedAsync<TReturn>(original, perSourceLine, token, excludeReturnId);
        var lines = NormalizeReturnLines(originalLines, r.Lines, previouslyReturned, perSourceLine, returnAllWhenEmpty: true);
        if (lines.Count == 0) throw new ConflictException(Messages.NoRemainingQtyToReturn);
        // مرتجع مشتريات على فاتورة لها سندات استلام: يقابل المرفوض، ولا يُرجع مالياً كمية مقبولة لم تُرجع بمرتجع السند (لا احتساب مرتين)
        await DeliverySources.EnsurePurchaseReturnFitsReceiptsAsync(_db, original, lines, excludeReturnId, token);

        var dto = BuildTradeReturn(original, r, lines, previouslyReturned, perSourceLine);
        if (r.RefundSplits.Count > 0) await ApplyRefundSplitsAsync(_db, dto, r.RefundSplits, token);
        else if (r.RefundPaymentMethod == null) MirrorOriginalSplits(dto, original);
        return dto;
    }

    /// <summary>
    /// معاينة مرتجع بلا أي أثر (لا رقم ولا قيد ولا مخزون ولا هيئة): الأصل كاملاً، المتبقي من كل صنف/سيارة، وتسعير المختار
    /// (r.Lines، أو r.VehicleIds لفاتورة سيارات؛ الفارغ = لا اختيار) بالبناء نفسه الذي يُنشئ المرتجع.
    /// </summary>
    public async Task<ReturnPreviewDto> PreviewReturnAsync<TOriginal, TReturn>(CreateReturnInvoiceRequestDto r, CancellationToken ct)
        where TOriginal : InvoiceBase, new() where TReturn : InvoiceBase, new()
    {
        var original = await Detailed(_db.Set<TOriginal>().AsNoTracking()).FirstOrDefaultAsync(i => i.Id == r.OriginalInvoiceId, ct)
            ?? throw new NotFoundException(Messages.OriginalInvoiceNotFound);
        if (original.Status != "posted" || original.IsReturn) throw new ConflictException(Messages.OnlyPostedOriginalInvoiceCanBeReturned);
        // فترة الإرجاع تُفحص هنا أيضاً فيعرفها المستخدم قبل الاختيار لا عند الحفظ؛ تعديل مرتجع بتاريخه نفسه لا يُفحص (كالحفظ)
        var returnDate = r.ReturnDate ?? SaudiTime.Now;
        var savedDate = r.ExcludeReturnId is Guid editingId
            ? await _db.Set<InvoiceBase>().AsNoTracking().Where(i => i.Id == editingId).Select(i => (DateTime?)i.IssueDate).FirstOrDefaultAsync(ct)
            : null;
        if (!(savedDate.HasValue && savedDate.Value.Date == returnDate.Date)) await EnsureWithinReturnPeriodAsync(original, returnDate, ct);
        var preview = new ReturnPreviewDto { Original = await GetAsync<TOriginal>(original.Id, ct) };

        if (original is Invoice car)
            foreach (var ext in _extensions)
                if (await ext.PreviewReturnAsync(r, car, ct) is { } units)
                {
                    preview.ReturnedVehicleIds = units.ReturnedVehicleIds.ToList();
                    if (units.Return != null) preview.Totals = PriceReturn(units.Return).Totals;
                    return preview;
                }

        var originalLines = original.Lines.ToList();
        var perSourceLine = ReturnsPerSourceLine(original);
        var previouslyReturned = await PreviouslyReturnedAsync<TReturn>(original, perSourceLine, ct, r.ExcludeReturnId);
        var chosen = NormalizeReturnLines(originalLines, r.Lines.Where(l => l.Quantity > 0).ToList(), previouslyReturned, perSourceLine);
        var priced = chosen.Count > 0 ? PriceReturn(BuildTradeReturn(original, r, chosen, previouslyReturned, perSourceLine)) : (Totals: null, Lines: Array.Empty<PricedLine>());
        var previewLines = perSourceLine
            ? originalLines
            : originalLines.GroupBy(i => i.ItemId).Select(g => g.First()).ToList();
        foreach (var src in previewLines)
        {
            var returned = previouslyReturned.GetValueOrDefault(perSourceLine ? src.Id : src.ItemId);
            var index = chosen.FindIndex(l => perSourceLine
                ? l.OriginalInvoiceItemId == src.Id
                : l.ItemId == src.ItemId);
            var line = index >= 0 ? priced.Lines[index] : null;
            preview.Lines.Add(new ReturnPreviewLineDto
            {
                OriginalInvoiceItemId = perSourceLine ? src.Id : null,
                ItemId = src.ItemId, ItemName = src.ItemName, Sku = src.Sku, Unit = src.Unit,
                OriginalQuantity = src.Quantity, ReturnedQuantity = returned, RemainingQuantity = Math.Max(0, src.Quantity - returned),
                UnitPrice = src.UnitPrice, Discount = src.Discount, VatRate = src.VatRate,
                Quantity = index >= 0 ? chosen[index].Quantity : 0, Net = line?.Net ?? 0, VatAmount = line?.VatAmount ?? 0, Total = line?.Total ?? 0,
            });
        }
        preview.Totals = priced.Totals;
        return preview;
    }

    /// <summary>الكميات المرتجعة لكل سطر أصل؛ تُوزّع أسطر المرتجعات القديمة غير المرتبطة بسطر على سطور الصنف بالترتيب.</summary>
    private static bool IsRentalContractPurchase(InvoiceBase original)
        => original.Kind == InvoiceKind.Purchase && original.Lines.Any(line => line.ProjectResourceId.HasValue);

    /// <summary>
    /// المرتجع يُبنى لكل سطر أصل (بسعره وخصمه وموقعه، والكمية تُوزَّع على أسطر الصنف بالترتيب) لا لكل صنف: فاتورة مؤجر المعدات،
    /// أو صنف وردت أسطره من أكثر من موقع (فيعود كل جزء لموقع سطره). غير ذلك يبقى بالصنف كما كان.
    /// </summary>
    private static bool ReturnsPerSourceLine(InvoiceBase original)
        => IsRentalContractPurchase(original)
           || original.Lines.GroupBy(line => line.ItemId).Any(g => g.Select(line => SiteOf(original, line)).Distinct().Count() > 1
               // أو بأكثر من وحدة: كمية كل سطر بوحدته، فلا تُجمع كميات وحدات مختلفة
               || g.Select(InvoiceLineStock.Factor).Distinct().Count() > 1);

    /// <summary>موقع السطر الفعلي: المحفوظ عليه، والفارغ (أسطر قديمة) = موقع المستند.</summary>
    internal static Guid? SiteOf(InvoiceBase invoice, InvoiceItemBase line) => line.WarehouseId ?? invoice.WarehouseId;

    private static Guid SiteKey(Guid? site) => site ?? Guid.Empty;

    /// <summary>البائع: موظف من المنشأة (نشط لغير المرتجع)، ويُحفظ اسمه لحظة الحفظ. الفارغ = بلا بائع.</summary>
    private async Task ApplySellerAsync(InvoiceBase invoice, Guid? employeeId, bool requireActive, CancellationToken ct)
    {
        invoice.SellerEmployeeId = employeeId; invoice.SellerName = null;
        if (employeeId is not Guid id) return;
        var employee = await _db.Set<Employee>().AsNoTracking().Where(e => e.Id == id).Select(e => new { e.NameAr, e.Status }).FirstOrDefaultAsync(ct)
            ?? throw new ValidationFailedException(Messages.SellerNotFound);
        if (requireActive && employee.Status != EmployeeStatuses.Active) throw new ValidationFailedException(Messages.SellerInactive);
        invoice.SellerName = employee.NameAr;
    }

    /// <summary>البائعون المتاحون لفاتورة البيع: الموظفون النشطون (رمز واسم فقط، بلا بيانات الموظف الأخرى).</summary>
    public Task<List<InvoiceSellerOptionDto>> SellerOptionsAsync(CancellationToken ct)
        => _db.Set<Employee>().AsNoTracking().Where(e => e.Status == EmployeeStatuses.Active).OrderBy(e => e.Code)
            .Select(e => new InvoiceSellerOptionDto { Id = e.Id, Code = e.Code, NameAr = e.NameAr, NameEn = e.NameEn }).ToListAsync(ct);

    /// <summary>
    /// موقع كل سطر: المرسَل أو موقع المستند، ويُحفظ الفعلي على السطر. الموقع المختلف عن موقع المستند يجب أن يكون من المنشأة ومسموحاً
    /// للمستخدم (كموقع المستند)، وفعّالاً إلا في المرتجع (يعود لموقع سطر أصله ولو أُوقف بعده).
    /// </summary>
    private async Task ApplyLineWarehousesAsync(InvoiceBase invoice, CreateInvoiceDto r, bool isReturn, CancellationToken ct)
    {
        var lines = invoice.Lines.ToList();
        for (var i = 0; i < lines.Count && i < r.Items.Count; i++) lines[i].WarehouseId = r.Items[i].WarehouseId ?? invoice.WarehouseId;
        var others = lines.Where(l => l.WarehouseId != invoice.WarehouseId).Select(l => l.WarehouseId!.Value).Distinct().ToList();
        if (others.Count == 0) return;
        var found = await _db.Set<Warehouse>().AsNoTracking().Where(w => others.Contains(w.Id))
            .Select(w => new { w.Id, w.Status }).ToDictionaryAsync(w => w.Id, ct);
        foreach (var id in others)
        {
            if (!found.TryGetValue(id, out var site)) throw new ValidationFailedException(Messages.WarehouseNotFound);
            if (!isReturn && site.Status != "active") throw new ValidationFailedException(Messages.WarehouseInactive);
            if (!_db.IsWarehouseAllowed(id)) throw new ForbiddenException(Messages.WarehouseNotAllowed);
        }
    }

    /// <summary>
    /// وحدة كل سطر ومعاملها إلى وحدة الصنف الأساسية، والخادم مصدرها لا ما يرسله العميل:
    /// - فاتورة البيع والشراء: من وحدات الصنف (<see cref="ProductLineUnits"/>): الأساسية بمعامل 1 أو بديلة بمعاملها، والفارغة =
    ///   الأساسية؛ صنف له وحدات بديلة يُرفض فيه غيرها، وصنف بلا وحدات بديلة يبقى كما كان (النص المرسل بمعامل 1).
    /// - المرتجع على أصل (بيع أو شراء): وحدة سطر أصله ومعامله (لقطة الفاتورة)، فيعود بالكمية نفسها مهما تغيّرت وحدات الصنف بعدها؛
    ///   والمرتجع بلا أصل أو سطر لا أصل له كالفاتورة من وحدات الصنف.
    /// العنصر <c>Unit</c> الفارغ = يُترك منطق الوحدة القائم.
    /// </summary>
    private async Task<List<(string? Unit, decimal Factor)>> ResolveLineUnitsAsync(CreateInvoiceDto r, Dictionary<Guid, Product> products, CancellationToken ct)
    {
        // المرتجع على أصل: وحدة سطر الأصل ومعامله (السطر القديم بنص حر يبقى كما حُفظ)
        var fromOrigin = new (string? Unit, decimal Factor)?[r.Items.Count];
        if (r.Kind is InvoiceKind.SalesReturn or InvoiceKind.PurchaseReturn && r.OriginalInvoiceId is Guid originalId)
        {
            var origin = await _db.Set<InvoiceItemBase>().AsNoTracking().Where(l => l.InvoiceId == originalId)
                .Select(l => new { l.Id, l.ItemId, l.Unit, l.UnitFactor }).ToListAsync(ct);
            for (var i = 0; i < r.Items.Count; i++)
            {
                var src = r.Items[i];
                var match = origin.FirstOrDefault(l => src.OriginalInvoiceItemId.HasValue && l.Id == src.OriginalInvoiceItemId)
                    ?? origin.FirstOrDefault(l => l.ItemId == src.ItemId && string.Equals(l.Unit, src.Unit, StringComparison.OrdinalIgnoreCase))
                    ?? origin.FirstOrDefault(l => l.ItemId == src.ItemId);
                if (match != null) fromOrigin[i] = (match.Unit, match.UnitFactor > 0 ? match.UnitFactor : 1m);
            }
        }
        // الفاتورة المحوَّلة من مستند (عرض، أمر، بيان) تقبل نص وحدة قديماً غير مرتبط بمعنى الأساسية كما كان
        var result = await ProductLineUnits.ResolveAsync(_db, r.Items.Select((item, i) =>
            (fromOrigin[i] == null && products.ContainsKey(item.ItemId) ? item.ItemId : Guid.Empty, (string?)item.Unit)).ToList(), ct,
            unlinkedAsBase: r.SystemGenerated);
        for (var i = 0; i < result.Count; i++)
            if (fromOrigin[i] is { } o) result[i] = o;
        return result;
    }

    /// <summary>للقراءة: اسم موقع السطر والاسم الإنجليزي الحالي لصنفه (من البطاقة، بلا تخزين على السطر).</summary>
    private async Task ApplyLineNamesAsync(IReadOnlyCollection<InvoiceItemDto> items, CancellationToken ct)
    {
        if (items.Count == 0) return;
        var productIds = items.Where(i => i.ItemId != Guid.Empty).Select(i => i.ItemId).Distinct().ToList();
        var siteIds = items.Where(i => i.WarehouseId.HasValue).Select(i => i.WarehouseId!.Value).Distinct().ToList();
        var names = productIds.Count == 0 ? new Dictionary<Guid, string>()
            : await _db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.NameEn, ct);
        var sites = siteIds.Count == 0 ? new Dictionary<Guid, string>()
            : await _db.Set<Warehouse>().AsNoTracking().Where(w => siteIds.Contains(w.Id)).ToDictionaryAsync(w => w.Id, w => w.NameAr, ct);
        foreach (var item in items)
        {
            if (names.TryGetValue(item.ItemId, out var nameEn) && !string.IsNullOrWhiteSpace(nameEn)) item.ItemNameEn = nameEn;
            if (item.WarehouseId is Guid site && sites.TryGetValue(site, out var siteName)) item.WarehouseName = siteName;
        }
    }

    /// <summary>
    /// قيمة ما أدخله/أخرجه آخر ترحيل للمستند في كل موقع (مجموع تغيّر قيمة حركاته منذ آخر عكس): أثره على حساب مخزون كل موقع،
    /// لإعادة تقييم كل موقع على حسابه عند العكس.
    /// </summary>
    private async Task<Dictionary<Guid, decimal>> PostedStockValueBySiteAsync(Guid invoiceId, CancellationToken ct)
    {
        var lastReversal = await _db.Set<StockMovement>().AsNoTracking()
            .Where(m => m.SourceId == invoiceId && m.SourceType == "invoice_reversal").MaxAsync(m => (DateTime?)m.CreatedAt, ct);
        var rows = await _db.Set<StockMovement>().AsNoTracking()
            .Where(m => m.SourceId == invoiceId && m.SourceType == "invoice" && (lastReversal == null || m.CreatedAt > lastReversal))
            .GroupBy(m => m.WarehouseId).Select(g => new { g.Key, Value = g.Sum(m => m.ValueChange) }).ToListAsync(ct);
        return rows.ToDictionary(r => SiteKey(r.Key), r => r.Value);
    }

    private async Task<Dictionary<Guid, decimal>> PreviouslyReturnedAsync<TReturn>(
        InvoiceBase original, bool perSourceLine, CancellationToken ct, Guid? excludeReturnId = null) where TReturn : InvoiceBase
    {
        // المرتجع الجاري تعديله لا يُحسب من «المرتجع من قبل»: كمياته تُعاد اختيارها
        var rows = await _db.Set<InvoiceItemBase>().AsNoTracking()
            .Where(i => _db.Set<TReturn>().Any(inv => inv.Id == i.InvoiceId && inv.OriginalInvoiceId == original.Id && inv.Status == "posted"
                && (excludeReturnId == null || inv.Id != excludeReturnId)))
            .ToListAsync(ct);
        if (!perSourceLine)
            return rows.GroupBy(i => i.ItemId).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        var returned = rows.Where(i => i.OriginalInvoiceItemId.HasValue)
            .GroupBy(i => i.OriginalInvoiceItemId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
        var legacy = rows.Where(i => !i.OriginalInvoiceItemId.HasValue)
            .GroupBy(i => i.ItemId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
        foreach (var group in original.Lines.GroupBy(i => i.ItemId))
        {
            var remainingLegacy = legacy.GetValueOrDefault(group.Key);
            foreach (var line in group.OrderBy(i => i.CreatedAt).ThenBy(i => i.Id))
            {
                var legacyQuantity = Math.Min(Math.Max(0, line.Quantity - returned.GetValueOrDefault(line.Id)), remainingLegacy);
                if (legacyQuantity > 0)
                {
                    returned[line.Id] = returned.GetValueOrDefault(line.Id) + legacyQuantity;
                    remainingLegacy -= legacyQuantity;
                }
            }
        }
        return returned;
    }

    private static List<ReturnLineDto> NormalizeReturnLines(
        IReadOnlyList<InvoiceItemBase> originalLines,
        IReadOnlyList<ReturnLineDto> requested,
        IReadOnlyDictionary<Guid, decimal> previouslyReturned,
        bool perSourceLine,
        bool returnAllWhenEmpty = false)
    {
        if (!perSourceLine)
        {
            if (requested.Count == 0 && !returnAllWhenEmpty) return new();
            if (requested.Count > 0) return requested.ToList();
            return originalLines.Select(line => new ReturnLineDto
            {
                ItemId = line.ItemId,
                Quantity = line.Quantity - previouslyReturned.GetValueOrDefault(line.ItemId),
            }).Where(line => line.Quantity > 0).ToList();
        }

        if (requested.Count == 0 && !returnAllWhenEmpty) return new();
        if (requested.Count == 0)
            return originalLines.Select(line => new ReturnLineDto
            {
                ItemId = line.ItemId, OriginalInvoiceItemId = line.Id,
                Quantity = Math.Max(0, line.Quantity - previouslyReturned.GetValueOrDefault(line.Id)),
            }).Where(line => line.Quantity > 0).ToList();

        var normalized = new List<ReturnLineDto>();
        foreach (var line in requested)
        {
            if (line.OriginalInvoiceItemId is Guid sourceId)
            {
                var source = originalLines.FirstOrDefault(i => i.Id == sourceId && i.ItemId == line.ItemId)
                    ?? throw new ValidationFailedException(Messages.ItemNotInOriginalInvoice);
                var remaining = source.Quantity - previouslyReturned.GetValueOrDefault(source.Id)
                    - normalized.Where(i => i.OriginalInvoiceItemId == source.Id).Sum(i => i.Quantity);
                if (line.Quantity <= 0 || line.Quantity > remaining)
                    throw new ValidationFailedException(string.Format(Messages.ReturnQtyRange, source.ItemName, Math.Max(0, remaining)));
                normalized.Add(new ReturnLineDto { ItemId = source.ItemId, OriginalInvoiceItemId = source.Id, Quantity = line.Quantity });
                continue;
            }

            var quantity = line.Quantity;
            foreach (var source in originalLines.Where(i => i.ItemId == line.ItemId).OrderBy(i => i.CreatedAt).ThenBy(i => i.Id))
            {
                var remaining = Math.Max(0, source.Quantity - previouslyReturned.GetValueOrDefault(source.Id)
                    - normalized.Where(i => i.OriginalInvoiceItemId == source.Id).Sum(i => i.Quantity));
                var take = Math.Min(Math.Max(0, quantity), remaining);
                if (take > 0)
                {
                    normalized.Add(new ReturnLineDto { ItemId = source.ItemId, OriginalInvoiceItemId = source.Id, Quantity = take });
                    quantity -= take;
                }
                if (quantity <= 0) break;
            }
            if (quantity > 0)
            {
                var source = originalLines.FirstOrDefault(i => i.ItemId == line.ItemId);
                if (source == null) throw new ValidationFailedException(Messages.ItemNotInOriginalInvoice);
                var available = originalLines.Where(i => i.ItemId == line.ItemId)
                    .Sum(i => Math.Max(0, i.Quantity - previouslyReturned.GetValueOrDefault(i.Id)));
                throw new ValidationFailedException(string.Format(Messages.ReturnQtyRange, source.ItemName, available));
            }
        }
        return normalized;
    }

    /// <summary>
    /// تسعير طلب الفاتورة بطريقته المحفوظة (شامل/غير شامل): مصدر واحد لكل مسارات الإنشاء والتعديل والمرتجع ومعاينته وتوزيع ردّه،
    /// فلا يُعاد تسعير مستند بغير طريقته.
    /// </summary>
    private static PricedDocument PriceDocument(CreateInvoiceDto dto) => DocumentPricing.Price(
        dto.Items.Select(i => new PricedLineInput(i.Quantity, i.UnitPrice, i.Discount, i.VatRate, i.VatAmountOverride,
            i.ExciseTaxRate, i.ExciseRetailPrice, i.ExciseStandardPrice)).ToList(), dto.InvoiceDiscount, dto.PricingMethod);

    /// <summary>تسعير المرتجع كما يسعّره الإنشاء: معالجة الضريبة من الأصل ثم محرك التسعير، بأسطره بترتيب بنوده.</summary>
    private static (ReturnTotalsDto Totals, IReadOnlyList<PricedLine> Lines) PriceReturn(CreateInvoiceDto dto)
    {
        var errors = new List<string>();
        ApplyDocumentVatTreatment(dto, errors);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
        var p = PriceDocument(dto);
        return (new ReturnTotalsDto
        {
            Gross = p.Subtotal, ItemsDiscount = p.ItemsDiscountTotal, InvoiceDiscount = p.InvoiceDiscount, NetTotal = p.NetTotal,
            ExciseTotal = p.ExciseTaxTotal, VatTotal = p.VatTotal, GrandTotal = p.GrandTotal,
        }, p.Lines);
    }

    /// <summary>طلب مرتجع تجارة عامة من الأصل: بيانات الطرف والحسابات والمعالجة منه، وكل سطر بسعر أصله وخصمه النسبي.</summary>
    private static UpdateInvoiceDto BuildTradeReturn(
        InvoiceBase original, CreateReturnInvoiceRequestDto r, List<ReturnLineDto> lines,
        Dictionary<Guid, decimal> previouslyReturned, bool perSourceLine)
    {
        var originalLines = original.Lines.ToList();
        var dto = new UpdateInvoiceDto
        {
            Kind = original.Kind == InvoiceKind.Sales ? InvoiceKind.SalesReturn : InvoiceKind.PurchaseReturn,
            InvoiceType = original.Kind == InvoiceKind.Sales ? InvoiceType.CreditNote : InvoiceType.DebitNote,
            IsReturn = true, OriginalInvoiceId = original.Id, OriginalInvoiceNumber = original.InvoiceNumber, ReturnReason = r.ReturnReason,
            InvoiceNumber = r.ManualNumber ?? string.Empty, SystemGenerated = r.SystemGenerated, // رقم المرتجع اليدوي إن ضُبط نوعه يدوياً
            PartyId = original.PartyId, PartyName = original.PartyName, PartyVatNumber = original.PartyVatNumber, PartyCrNumber = original.PartyCrNumber, PartyIdType = original.PartyIdType, PartyIdNumber = original.PartyIdNumber,
            PartyAddress = original.PartyAddress, PartyPhone = original.PartyPhone, PartyEmail = original.PartyEmail,
            WarehouseId = original.WarehouseId, // المرتجع يعود إلى موقع فاتورته
            CostCenterId = original.CostCenterId,
            SellerEmployeeId = original.SellerEmployeeId, // المرتجع يُنسب لبائع فاتورته
            JournalCostCenters = JournalCostCenterMap.Unpack(original.JournalCostCentersJson), // المرتجع يعكس قيد أصله على مراكزه
            // الحسابات المحددة يدوياً على الأصل تتقدم في مرتجعه كما تقدمت فيه: المخزون يعود لحسابه والإيراد يُعكس من حسابه
            RevenueAccountCode = original.RevenueAccountCode, InventoryAccountCode = original.InventoryAccountCode, CogsAccountCode = original.CogsAccountCode,
            VatTreatment = original.VatTreatment, VatTreatmentReasonCode = original.VatTreatmentReasonCode, // المرتجع بمعالجة أصله
            PricingMethod = original.PricingMethod, // وبطريقة تسعيره: أسعار الأصل وخصومه تُقرأ كما صدرت
            PartyStreet = original.PartyStreet, PartyBuildingNo = original.PartyBuildingNo, PartyDistrict = original.PartyDistrict,
            PartyCity = original.PartyCity, PartyPostalCode = original.PartyPostalCode, PartyAdditionalNo = original.PartyAdditionalNo, PartyCountry = original.PartyCountry,
            PaymentMethod = r.RefundPaymentMethod ?? original.PaymentMethod, PaymentMethodCode = r.RefundPaymentMethod == null ? original.PaymentMethodCode : r.RefundPaymentMethodCode, CurrencyCode = original.CurrencyCode, ExchangeRate = original.ExchangeRate,
            Notes = ReturnNotes(original, r),
            Status = "posted",
        };
        if (r.ReturnDate.HasValue) dto.IssueDate = r.ReturnDate.Value;
        foreach (var l in lines)
        {
            var src = perSourceLine && l.OriginalInvoiceItemId is Guid sourceId
                ? originalLines.FirstOrDefault(i => i.Id == sourceId && i.ItemId == l.ItemId)
                : originalLines.FirstOrDefault(i => i.ItemId == l.ItemId);
            if (src == null) throw new ValidationFailedException(Messages.ItemNotInOriginalInvoice);
            var remaining = src.Quantity - previouslyReturned.GetValueOrDefault(perSourceLine ? src.Id : src.ItemId);
            if (l.Quantity <= 0 || l.Quantity > remaining)
                throw new ValidationFailedException(string.Format(Messages.ReturnQtyRange, src.ItemName, remaining));
            dto.Items.Add(new InvoiceItemDto
            {
                ItemId = src.ItemId, ItemName = src.ItemName, Sku = src.Sku, Unit = src.Unit, UnitFactor = InvoiceLineStock.Factor(src), Quantity = l.Quantity,
                UnitPrice = src.UnitPrice, UnitCost = src.UnitCost, VatRate = src.VatRate,
                ExciseTaxRate = src.ExciseTaxRate, ExciseRetailPrice = src.ExciseRetailPrice,
                ExciseStandardPrice = src.ExciseStandardPrice,
                VatCategory = src.VatCategory, VatExemptionReasonCode = src.VatExemptionReasonCode, VatOverrideReason = src.VatOverrideReason,
                RevenueAccountCode = src.RevenueAccountCode,
                Discount = src.Quantity == 0 ? 0 : Math.Round(src.Discount * l.Quantity / src.Quantity, 2),
                CostCenterId = src.CostCenterId,
                WarehouseId = SiteOf(original, src), // كل سطر يعود إلى موقع سطر أصله
                ProjectResourceId = perSourceLine ? src.ProjectResourceId : null,
                OriginalInvoiceItemId = perSourceLine ? src.Id : null,
            });
        }
        // خصم الفاتورة يُوزَّع نسبياً على الكميات المرتجعة.
        var origGross = originalLines.Sum(i => i.Quantity * i.UnitPrice - i.Discount);
        var retGross = dto.Items.Sum(i => i.Quantity * i.UnitPrice - i.Discount);
        dto.InvoiceDiscount = origGross == 0 ? 0 : DocumentPricing.Round(original.InvoiceDiscount * retGross / origGross);
        return dto;
    }

    internal static string ReturnNotes(InvoiceBase original, CreateReturnInvoiceRequestDto r)
        => string.IsNullOrWhiteSpace(r.Notes) ? $"مرتجع من الفاتورة {original.InvoiceNumber}" : $"مرتجع من الفاتورة {original.InvoiceNumber}: {r.Notes.Trim()}";

    /// <summary>
    /// رد المرتجع بأكثر من طريقة: المبالغ المدخلة تُوزَّع بنسبتها على إجمالي المرتجع المحسوب في السرفر (والكسر على آخر سطر)،
    /// فلا يفشل الحفظ لفرق تقريب بين تقدير الشاشة والإجمالي الفعلي. كل طريقة تُعتمد (فعّالة) ونوع الدفع من نوعها.
    /// يُستدعى بعد اكتمال أسطر المرتجع وخصمه.
    /// </summary>
    internal static async Task ApplyRefundSplitsAsync(ErpDbContext db, CreateInvoiceDto dto, List<InvoicePaymentSplitDto> splits, CancellationToken ct)
    {
        var lines = splits.Where(s => s.Amount > 0).ToList();
        if (lines.Count == 0) return;
        // طريقة ردٍّ يختارها المستخدم لا تكون استقطاع عقد (حسابه يحدده النظام)
        if (lines.Any(s => s.Method == PaymentMethod.ContractSettlement)) throw new ValidationFailedException(Messages.InvalidPaymentMethod);
        PaymentSplitRules.EnsureDistinctMethods(lines.Select(s => (s.Method, s.PaymentMethodCode)));
        var total = PriceDocument(dto).GrandTotal;
        var entered = lines.Sum(s => s.Amount);
        dto.PaymentSplits.Clear();
        var allocated = 0m;
        for (var i = 0; i < lines.Count; i++)
        {
            var s = lines[i];
            var amount = i == lines.Count - 1 ? total - allocated : DocumentPricing.Round(s.Amount * total / entered);
            allocated += amount;
            var method = await PaymentMethodRules.MethodForAsync(db, s.Method, s.PaymentMethodCode, ct);
            if (amount > 0) dto.PaymentSplits.Add(new InvoicePaymentSplitDto { Method = method, PaymentMethodCode = s.PaymentMethodCode, Amount = amount, Reference = s.Reference });
        }
        if (dto.PaymentSplits.Count == 0) return;
        dto.IsSplitPayment = true;
        dto.PaymentMethod = dto.PaymentSplits.Any(s => s.Method == PaymentMethod.Credit) ? PaymentMethod.Credit : dto.PaymentSplits[0].Method;
        dto.PaymentMethodCode = null;
    }

    /// <summary>بلا طريقة ردّ محددة: فاتورة دُفعت بأكثر من طريقة يُردّ مرتجعها بالتوزيع نفسه (بنسبة قيمة المرتجع).</summary>
    internal static void MirrorOriginalSplits(CreateInvoiceDto dto, InvoiceBase original)
    {
        if (original.PaymentSplits.Count == 0 || original.GrandTotal <= 0) return;
        var returned = PriceDocument(dto).GrandTotal;
        var allocated = 0m;
        for (var i = 0; i < original.PaymentSplits.Count; i++)
        {
            var split = original.PaymentSplits.ElementAt(i);
            var amount = i == original.PaymentSplits.Count - 1
                ? returned - allocated : DocumentPricing.Round(split.Amount * returned / original.GrandTotal);
            allocated += amount;
            if (amount > 0) dto.PaymentSplits.Add(new InvoicePaymentSplitDto { Method = split.Method, PaymentMethodCode = split.PaymentMethodCode, Amount = amount, Reference = split.Reference });
        }
        dto.IsSplitPayment = dto.PaymentSplits.Count > 0;
    }

    /// <summary>حذف مسودة فقط. الفاتورة المرحّلة مستند صادر لا يُحذف: تُلغى بمرتجع كامل (إشعار دائن/مدين).</summary>
    public Task DeleteAsync<T>(Guid id, CancellationToken ct) where T : InvoiceBase, new()
        => _tx.RunAsync(async token =>
        {
            var invoice = await Detailed(_db.Set<T>()).FirstOrDefaultAsync(i => i.Id == id, token) ?? throw new NotFoundException(Messages.InvoiceNotFound);
            await EnsureOpenPeriodAsync(invoice.IssueDate, token);
            // ما وصل للهيئة لا يُحذف أبداً، مقبولاً أو مرفوضاً: صار حلقة في سلسلة التجزئة والعدّاد، ويُصحَّح بمرتجع فقط
            if (invoice.ZatcaStatus != ZatcaSubmissionStatus.NotSubmitted || invoice.ZatcaHash != null)
                throw new ConflictException(string.Format(Messages.InvoiceSentToZatcaCannotBeDeleted, invoice.InvoiceNumber));
            // المرحّلة (قبل إرسالها للهيئة) تُحذف: يُحذف قيدها نهائياً (بلا قيد عكسي) وتُعكس حركاتها المخزنية وتعود سياراتها؛ المسودة تُحذف مباشرة
            var wasPosted = invoice.Status == "posted";
            // المرتجع المرحّل (قبل إرساله للهيئة) يُحذف كالفاتورة: يُحذف قيده وتُعكس حركاته وتعود كمياته متاحة للإرجاع من أصله
            if (wasPosted) await UnpostForEditAsync(invoice, token, forDelete: true, allowReturn: true);
            else EnsureNotLinkedToAnotherDocument(invoice);
            await ReleaseSourceAsync(invoice, token);
            await OnSavingAsync(invoice, deleting: true, token);
            var isReturnDoc = invoice.IsReturn || invoice.OriginalInvoiceId.HasValue;
            await _audit.LogAsync(wasPosted ? (isReturnDoc ? "RETURN_POSTED_DELETED" : "INVOICE_POSTED_DELETED") : "INVOICE_DRAFT_DELETED", typeof(T).Name, invoice.Id.ToString(), $"حذف {(wasPosted ? $"{invoice.InvoiceNumber} المرحّلة" : "مسودة")} {DescribeKind(invoice.Kind)} بإجمالي {invoice.GrandTotal:0.00} - {invoice.PartyName}", token);
            _db.RemoveRange(invoice.Lines.ToList());
            _db.RemoveRange(invoice.PaymentSplits);
            if (invoice is Invoice car)
            {
                _db.RemoveRange(car.VehicleLines);
                _db.RemoveRange(car.VehicleBatches);
            }
            _db.Remove(invoice);
            await _db.SaveChangesAsync(token);
        }, ct);

    /// <summary>مستند تجاري مصدر (عرض سعر، أمر بيع/شراء، طلب شراء): ارتباطه يبقى مع تعديل فاتورته، وحذفها يفكّه.</summary>
    private static bool IsCommercialSource(string? referenceType)
        => referenceType is "quotation" or "sales_order" or "purchase_order" or "material_requisition";

    /// <summary>
    /// المسودة المرتبطة بمستند تُعدَّل منه (نقطة بيع، عقد، مرتجع)؛ أما المحوَّلة من مستند تجاري فتُعدَّل وتُرحَّل وتُحذف
    /// هنا كالمرحّلة في <see cref="UnpostForEditAsync"/>.
    /// </summary>
    private static void EnsureNotLinkedToAnotherDocument(InvoiceBase invoice)
    {
        if (invoice.OriginalInvoiceId.HasValue
            || ((invoice.ReferenceType != null || invoice.ReferenceId.HasValue) && !IsCommercialSource(invoice.ReferenceType)))
            throw new ConflictException(Messages.InvoiceFromSourceDocument);
    }

    /// <summary>
    /// فاتورة شراء مستوردة من طلب شراء في الشاشة: الطلب يجب أن يكون معتمداً وغير محوَّل، ويُعلَّم محوَّلاً إليها
    /// (عكس <see cref="ReleaseSourceAsync"/>). التحويل المباشر من شاشة الطلبات يمرّ بهذا المسار أيضاً.
    /// </summary>
    private async Task ClaimSourceAsync(InvoiceBase invoice, CancellationToken ct)
    {
        if (invoice.ReferenceType != "material_requisition" || invoice.ReferenceId == null) return;
        if (invoice.Kind != InvoiceKind.Purchase) throw new ValidationFailedException(Messages.InvalidDocumentType);
        var m = await _db.Set<MaterialRequisition>().FirstOrDefaultAsync(x => x.Id == invoice.ReferenceId, ct)
            ?? throw new NotFoundException(Messages.PurchaseRequisitionNotFound);
        if (m.ConvertedInvoiceId != null || m.Status is "converted_to_invoice" or "converted_to_po")
            throw new ConflictException(string.Format(Messages.RequisitionAlreadyConverted, m.RequisitionNumber));
        if (m.Status != "approved") throw new ConflictException(Messages.ApproveRequisitionBeforeConversion);
        // طلب المشروع يُستلم بسند استلام (عهدة للمشروع) ثم يُفوتر منه، لا بفاتورة شراء للموقع العام
        if (m.ContractId != null) throw new ConflictException(Messages.ProjectRequisitionReceivedByGrn);
        // طلب عليه طلب عروض أسعار قائم يُشترى بأوامر الترسية فلا تتكرر الكمية بفاتورة مباشرة
        if (await PurchaseCycleService.HasActiveRfqAsync(_db, m.Id, ct))
            throw new ConflictException(string.Format(Messages.RequisitionHasRfq, m.RequisitionNumber));
        m.ConvertedInvoiceId = invoice.Id;
        m.Status = "converted_to_invoice";
    }

    /// <summary>تغيير مراكز التكلفة وحدها (المسار القائم): تعديل بعد الإرسال لا يمس البائع ولا المواقع.</summary>
    public Task<InvoiceDto> ReassignCostCentersAsync<T>(Guid id, ReassignInvoiceCostCentersDto r, CancellationToken ct) where T : InvoiceBase, new()
        => AmendPostedSalesCoreAsync<T>(id, new AmendPostedSalesInvoiceDto
        {
            CostCenterId = r.CostCenterId, Reason = r.Reason,
            Lines = r.Lines.Select(l => new AmendPostedSalesLineDto { LineId = l.LineId, CostCenterId = l.CostCenterId }).ToList(),
        }, touchSeller: false, ct);

    /// <summary>
    /// التعديل المسموح لفاتورة بيع مرحّلة (ولو أُرسلت للهيئة)، ولا يمس الفاتورة الصادرة (الأرقام والضريبة والـ XML):
    /// - البائع: بيان داخلي يُغيَّر بلا أثر محاسبي.
    /// - مراكز التكلفة (الترويسة = العام، والأسطر = الجدول): يُعاد بناء القيد في مكانه بالحسابات والمبالغ نفسها وتتغير أبعاده فقط.
    /// - موقع الصرف: لسطر لم يُصرف مخزونه (خدمة/غير مخزني) أو موقع الترويسة، ما دام القيد يبقى بالحسابات والمبالغ نفسها.
    ///   السطر المخزني صُرف عند الترحيل: يُرفض ويُعالج بتحويل مخزني.
    /// المرفقات (مستندات الأرشفة) عبر المرفقات المشتركة بمالك <c>sales_invoice</c>. ضمانة القيد: مجاميع كل حساب قبل وبعد متطابقة، وإلا يُرفض.
    /// </summary>
    public Task<InvoiceDto> AmendPostedSalesAsync<T>(Guid id, AmendPostedSalesInvoiceDto r, CancellationToken ct) where T : InvoiceBase, new()
        => AmendPostedSalesCoreAsync<T>(id, r, touchSeller: true, ct);

    private Task<InvoiceDto> AmendPostedSalesCoreAsync<T>(Guid id, AmendPostedSalesInvoiceDto r, bool touchSeller, CancellationToken ct) where T : InvoiceBase, new()
        => _tx.RunAsync(async token =>
        {
            var invoice = await Detailed(_db.Set<T>()).FirstOrDefaultAsync(i => i.Id == id, token) ?? throw new NotFoundException(Messages.InvoiceNotFound);
            if (invoice.Kind != InvoiceKind.Sales || invoice.IsReturn || invoice.OriginalInvoiceId != null || invoice.Status != "posted" || invoice.JournalEntryId is not Guid entryId)
                throw new ConflictException(Messages.CostCenterReassignPostedSalesOnly);
            if (string.IsNullOrWhiteSpace(r.Reason)) throw new ValidationFailedException(Messages.CostCenterReassignReasonRequired);
            if (r.Lines.Any(l => invoice.Lines.All(x => x.Id != l.LineId)) || r.Lines.Select(l => l.LineId).Distinct().Count() != r.Lines.Count)
                throw new ValidationFailedException(Messages.SomeDocumentLinesInvalid);

            var lineOf = invoice.Lines.ToDictionary(l => l.Id);
            var sellerChanged = touchSeller && r.SellerEmployeeId != invoice.SellerEmployeeId;
            var centersChanged = r.CostCenterId != invoice.CostCenterId || r.Lines.Any(l => lineOf[l.LineId].CostCenterId != (l.CostCenterId ?? r.CostCenterId));
            var headerSiteChanged = r.WarehouseId is Guid newHeader && newHeader != invoice.WarehouseId;
            var siteChanges = r.Lines.Where(l => l.WarehouseId is Guid w && w != SiteOf(invoice, lineOf[l.LineId]))
                .Select(l => (Line: lineOf[l.LineId], Site: l.WarehouseId!.Value)).ToList();
            if (!sellerChanged && !centersChanged && !headerSiteChanged && siteChanges.Count == 0) return await GetAsync<T>(id, token);

            var changes = new List<string>();
            if (sellerChanged)
            {
                var from = invoice.SellerName;
                await ApplySellerAsync(invoice, r.SellerEmployeeId, requireActive: true, token);
                changes.Add($"البائع: {from ?? "-"} ← {invoice.SellerName ?? "-"}");
            }

            if (centersChanged || headerSiteChanged || siteChanges.Count > 0)
            {
                if (invoice is Invoice { VehicleLines.Count: > 0 } || (invoice.ReferenceType != null && !IsCommercialSource(invoice.ReferenceType)))
                    throw new ConflictException(Messages.InvoiceFromSourceDocument);
                await EnsureOpenPeriodAsync(invoice.IssueDate, token);
                if (centersChanged)
                {
                    await LineDimensions.EnsureAsync(_db, r.Lines.Select(l => ((Guid?)null, l.CostCenterId)).Append(((Guid?)null, r.CostCenterId)), token);
                    await CostCenterRules.EnsureCanAssignAsync(_permissions, true, token);
                }
                // موقع الصرف: السطر المخزني صُرف مخزونه عند الترحيل فلا يُنقل إلا بتحويل مخزني
                foreach (var (line, _) in siteChanges)
                    if (!InvoiceLineStock.IsNonStock(line)) throw new ConflictException(string.Format(Messages.InvoiceLineStockIssued, line.ItemName));
                var newSites = siteChanges.Select(c => c.Site).Concat(headerSiteChanged ? new[] { r.WarehouseId!.Value } : Array.Empty<Guid>()).Distinct().ToList();
                var sites = await _db.Set<Warehouse>().AsNoTracking().Where(w => newSites.Contains(w.Id))
                    .Select(w => new { w.Id, w.Status, w.NameAr }).ToDictionaryAsync(w => w.Id, token);
                foreach (var site in newSites)
                {
                    if (!sites.TryGetValue(site, out var w)) throw new ValidationFailedException(Messages.WarehouseNotFound);
                    if (w.Status != "active") throw new ValidationFailedException(Messages.WarehouseInactive);
                    if (!_db.IsWarehouseAllowed(site)) throw new ForbiddenException(Messages.WarehouseNotAllowed);
                }

                var before = await AccountTotalsAsync(entryId, token);
                if (headerSiteChanged)
                {
                    // الأسطر القديمة بلا موقع تتبع الترويسة: تُثبَّت على موقعها الحالي فلا ينقل تغييرُ الترويسة مخزونَها
                    foreach (var line in invoice.Lines.Where(l => l.WarehouseId == null)) line.WarehouseId = invoice.WarehouseId;
                    var header = sites[r.WarehouseId!.Value];
                    changes.Add($"موقع الفاتورة: {invoice.WarehouseName} ← {header.NameAr}");
                    invoice.WarehouseId = header.Id; invoice.WarehouseName = header.NameAr;
                }
                foreach (var (line, site) in siteChanges)
                {
                    changes.Add($"موقع «{line.ItemName}» ← {sites[site].NameAr}");
                    line.WarehouseId = site;
                }
                if (centersChanged)
                {
                    changes.Add("مراكز التكلفة");
                    invoice.CostCenterId = r.CostCenterId;
                    foreach (var l in r.Lines) lineOf[l.LineId].CostCenterId = l.CostCenterId ?? r.CostCenterId; // كالحفظ: السطر بلا مركز يأخذ العام
                }

                // التكلفة: قيمة ما صُرف فعلاً لكل موقع (حركات الترحيل الأخير) موزعة على أسطره المخزنية بتكلفتها، وتكلفة بنود الخدمات كما رُحّلت
                var fx = invoice.ExchangeRate <= 0 ? 1 : invoice.ExchangeRate;
                var baseTotal = DocumentPricing.Round(invoice.GrandTotal * fx);
                var baseVat = DocumentPricing.Round(invoice.VatTotal * fx);
                var baseNet = baseTotal - baseVat;
                var costByCenter = new CostCenterTotals();
                foreach (var (site, value) in await PostedStockValueBySiteAsync(invoice.Id, token))
                {
                    var weights = new CostCenterTotals();
                    foreach (var line in invoice.Lines.Where(l => !InvoiceLineStock.IsNonStock(l) && SiteKey(SiteOf(invoice, l)) == site))
                        weights.Add(line.CostCenterId, Math.Max(0, line.Quantity * line.UnitCost), SiteOf(invoice, line));
                    if (weights.Total <= 0) throw new ConflictException(Messages.CostCenterReassignUnbalanced);
                    var share = CostCenterTotals.Apportion(-value, weights);
                    foreach (var slice in share.Slices) costByCenter.Add(slice.Center, share[slice], slice.Site);
                }
                foreach (var svc in invoice.Lines.Where(i => InvoiceLineStock.IsNonStock(i) && i.UnitCost > 0))
                    costByCenter.Add(svc.CostCenterId, Math.Round(svc.Quantity * svc.UnitCost, 2), SiteOf(invoice, svc));
                if (Math.Abs(costByCenter.Total - invoice.TotalCost) > 0.01m) throw new ConflictException(Messages.CostCenterReassignUnbalanced);

                var reposted = await _posting.PostSaleAsync(new SalePostingRequest
                {
                    Module = invoice.Module, Date = invoice.IssueDate, Description = $"{DescribeKind(invoice.Kind)} {invoice.InvoiceNumber} - {invoice.PartyName}",
                    SourceType = SourceTypeFor(invoice.Kind), SourceId = invoice.Id, ReplaceEntryId = entryId,
                    SourceNumber = invoice.InvoiceNumber, IsReturn = false, PartyAccountCode = await PartyAccountAsync(invoice, true, token),
                    NetAmount = baseNet, ExciseTaxAmount = DocumentPricing.Round(invoice.ExciseTaxTotal * fx),
                    VatAmount = baseVat, CostAmount = invoice.TotalCost, Payments = ToBase(await ResolvePaymentsAsync(invoice, token), fx, invoice.GrandTotal, baseTotal),
                    RevenueAccountCode = invoice.RevenueAccountCode, InventoryAccountCode = invoice.InventoryAccountCode, CogsAccountCode = invoice.CogsAccountCode, WarehouseId = invoice.WarehouseId,
                    RevenueLines = RevenueByLine(invoice, fx, baseNet), CostLines = costByCenter.ToAmounts(),
                    CostCenterByAccount = JournalCostCenterMap.Map(invoice.JournalCostCentersJson),
                }, token);
                await _db.SaveChangesAsync(token);
                invoice.JournalEntryId = reposted.JournalEntryId;
                await _db.SaveChangesAsync(token);
                var after = await AccountTotalsAsync(reposted.JournalEntryId, token);
                // تغيير موقع يبدّل حسابات القيد (حسابات الموقع) ليس تعديلاً للأبعاد: يُرفض كله
                if (before.Count != after.Count || before.Any(b => !after.TryGetValue(b.Key, out var a) || a != b.Value))
                    throw new ConflictException(headerSiteChanged || siteChanges.Count > 0 ? Messages.InvoiceSiteChangeAltersJournal : Messages.CostCenterReassignUnbalanced);

                // بيانات التسليم من الفاتورة تتبع مراكز أسطرها ومواقعها (مستند تشغيلي بلا قيد ولا حركة مخزون)
                foreach (var item in await _db.Set<DeliveryNoteItem>()
                             .Where(i => i.DeliveryNote.SourceType == DeliveryNoteSources.SalesInvoice && i.DeliveryNote.SourceId == invoice.Id && i.SourceLineId != null).ToListAsync(token))
                    if (lineOf.TryGetValue(item.SourceLineId!.Value, out var src)) { item.CostCenterId = src.CostCenterId; item.WarehouseId = SiteOf(invoice, src); }
            }

            await _audit.LogAsync(touchSeller ? "INVOICE_AMENDED_AFTER_POSTING" : "INVOICE_COST_CENTERS_REASSIGNED", typeof(T).Name, invoice.Id.ToString(),
                $"{invoice.InvoiceNumber}: {string.Join("، ", changes)} — {r.Reason?.Trim()}", token);
            await _db.SaveChangesAsync(token);
            return await GetAsync<T>(id, token);
        }, ct);

    /// <summary>مجموع المدين والدائن لكل حساب في القيد (بلا الأبعاد).</summary>
    private async Task<Dictionary<string, (decimal Debit, decimal Credit)>> AccountTotalsAsync(Guid entryId, CancellationToken ct)
        => (await _db.Set<JournalEntryLine>().AsNoTracking().Where(l => l.JournalEntryId == entryId)
                .GroupBy(l => l.AccountCode).Select(g => new { g.Key, D = g.Sum(l => l.Debit), C = g.Sum(l => l.Credit) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => (x.D, x.C));

    /// <summary>
    /// حذف فاتورة مرحلية من بيان اتفاقية متتبَّع (من البيان أو بدفعة): تُحذف أسطر فوترتها وتُطرح كمياتها من المفوتر،
    /// فتعود الكميات المؤكدة قابلة للفوترة مرة واحدة.
    /// </summary>
    private async Task ReleaseTrackedDeliveryBillingAsync(InvoiceBase invoice, CancellationToken ct)
    {
        var billed = await _db.Set<DeliveryNoteInvoiceLine>().Where(l => l.InvoiceId == invoice.Id).ToListAsync(ct);
        if (billed.Count == 0) return;
        foreach (var noteId in billed.Select(l => l.DeliveryNoteId).Distinct())
        {
            var note = await _db.Set<DeliveryNote>().Include(x => x.Items).FirstAsync(x => x.Id == noteId, ct);
            foreach (var line in billed.Where(l => l.DeliveryNoteId == noteId))
                if (note.Items.FirstOrDefault(i => i.Id == line.DeliveryNoteItemId) is { } item) item.InvoicedQty = Math.Max(0, item.InvoicedQty - line.Quantity);
            if (note.InvoiceId == invoice.Id)
            {
                var previous = await _db.Set<DeliveryNoteInvoiceLine>().AsNoTracking()
                    .Where(l => l.DeliveryNoteId == noteId && l.InvoiceId != invoice.Id).OrderByDescending(l => l.CreatedAt)
                    .Select(l => new { l.InvoiceId, l.InvoiceNumber }).FirstOrDefaultAsync(ct);
                note.InvoiceId = previous?.InvoiceId; note.InvoiceNumber = previous?.InvoiceNumber;
            }
            DeliverySources.RefreshTrackedStatus(note);
            _db.Add(new DeliveryNoteEvent
            {
                DeliveryNoteId = note.Id, EventType = "invoice_released", FromStatus = note.DeliveryStatus, ToStatus = note.DeliveryStatus,
                Notes = invoice.InvoiceNumber, OccurredAt = DateTime.UtcNow,
            });
        }
        _db.RemoveRange(billed);
    }

    /// <summary>عند حذف فاتورة محوّلة من مستند تجاري يُفَك الارتباط ليعود المستند قابلاً للتحويل.</summary>
    private async Task ReleaseSourceAsync(InvoiceBase invoice, CancellationToken ct)
    {
        await ReleaseTrackedDeliveryBillingAsync(invoice, ct);
        if (invoice.ReferenceId == null) return;
        var sourceId = invoice.ReferenceId.Value;
        switch (invoice.ReferenceType)
        {
            case "quotation":
                var q = await _db.Set<Quotation>().FirstOrDefaultAsync(x => x.Id == sourceId && x.ConvertedInvoiceId == invoice.Id, ct);
                if (q != null) { q.ConvertedInvoiceId = null; q.Status = "accepted"; }
                break;
            case "sales_order" or "purchase_order":
                var o = await _db.Set<CommercialOrder>().FirstOrDefaultAsync(x => x.Id == sourceId && x.ConvertedInvoiceId == invoice.Id, ct);
                if (o != null) { o.ConvertedInvoiceId = null; o.Status = "confirmed"; }
                break;
            case "material_requisition":
                var m = await _db.Set<MaterialRequisition>().FirstOrDefaultAsync(x => x.Id == sourceId && x.ConvertedInvoiceId == invoice.Id, ct);
                if (m != null) { m.ConvertedInvoiceId = null; m.Status = "approved"; }
                break;
            case "delivery_note":
                // حذف فاتورة البيان يعيده قابلاً للفوترة بدل أن يبقى مرتبطاً بفاتورة محذوفة (المتتبَّع حُرِّر أعلاه)
                // وفاتورة البيانات المستقلة المجمّعة تعيدها كلها (المرجع أولها، والبقية مربوطة بالفاتورة نفسها)
                foreach (var n in await _db.Set<DeliveryNote>().Include(x => x.Items)
                             .Where(x => x.InvoiceId == invoice.Id && (x.Id == sourceId || (x.ContractId == null && x.SourceType == DeliveryNoteSources.Manual)))
                             .ToListAsync(ct))
                {
                    if (DeliveryTrackingStatuses.IsTracked(n.DeliveryStatus)) continue;
                    n.InvoiceId = null; n.InvoiceNumber = null;
                    n.Status = n.Items.Any(i => i.ReturnedQty > 0) ? DeliveryNoteStatus.PartiallyReturned : DeliveryNoteStatus.Delivered;
                }
                break;
            case PurchaseOrderReceipts.InvoiceReferenceType:
                // فاتورة مجمّعة لسندات استلام أمر شراء: حذفها يعيد كل سنداتها قابلة للفوترة
                foreach (var billed in await _db.Set<DeliveryNote>().Include(x => x.Items)
                             .Where(x => x.PurchaseOrderId == sourceId && x.InvoiceId == invoice.Id).ToListAsync(ct))
                {
                    billed.InvoiceId = null; billed.InvoiceNumber = null;
                    billed.Status = billed.Items.Any(i => i.ReturnedQty > 0) ? DeliveryNoteStatus.PartiallyReturned : DeliveryNoteStatus.Delivered;
                }
                break;
        }
    }

    public async Task<ZatcaSubmitResultDto> SubmitToZatcaAsync<T>(Guid id, CancellationToken ct) where T : InvoiceBase
    {
        if (!await _db.Set<T>().AnyAsync(i => i.Id == id, ct)) throw new NotFoundException(Messages.InvoiceNotFound);
        return await _zatca.SubmitInvoiceAsync(id, ct);
    }

    // ---------------- القيد المحاسبي للفاتورة ----------------
    /// <summary>
    /// يُنفَّذ البناء والترحيل الحقيقيان داخل معاملة تُلغى دائماً، فتطابق المعاينة الترحيل الفعلي حرفياً
    /// (تكلفة المخزون بالمتوسط، تكلفة المركبات، حسابات الدفع والطرف، الحد الائتماني) دون أي أثر محفوظ أو رقم مستهلك.
    /// </summary>
    public async Task<InvoiceJournalDto> PreviewJournalAsync<T>(CreateInvoiceDto r, CancellationToken ct) where T : InvoiceBase, new()
    {
        if (_db.Database.CurrentTransaction != null) throw new InvalidOperationException(Messages.JournalPreviewInsideTransaction);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var invoice = await BuildAsync<T>(r, ct);
            await EnsureJournalVisibleAsync(invoice, ct);
            invoice.Status = "draft";
            invoice.InvoiceNumber = "PREVIEW-" + invoice.Id.ToString("N")[..8];
            _db.Add(invoice);
            await _db.SaveChangesAsync(ct);
            await FinalizeAsync(invoice, ct, preview: true);
            var journal = await JournalOfAsync(invoice, ct);
            journal.IsPreview = true;
            journal.EntryNumber = null;
            return journal;
        }
        finally
        {
            await tx.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
        }
    }

    public async Task<InvoiceJournalDto> GetJournalAsync<T>(Guid id, CancellationToken ct) where T : InvoiceBase, new()
    {
        var invoice = await Detailed(_db.Set<T>().AsNoTracking()).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException(Messages.InvoiceNotFound);
        await EnsureJournalVisibleAsync(invoice, ct);
        if (invoice.JournalEntryId == null) throw new ConflictException(Messages.InvoiceNotPostedUsePreview);
        return await JournalOfAsync(invoice, ct);
    }

    private async Task EnsureJournalVisibleAsync(InvoiceBase invoice, CancellationToken ct)
    {
        if (invoice is Invoice car) foreach (var ext in _extensions) await ext.EnsureJournalVisibleAsync(car, ct);
    }

    private async Task<InvoiceJournalDto> JournalOfAsync(InvoiceBase invoice, CancellationToken ct)
    {
        var entry = await _db.Set<JournalEntry>().AsNoTracking().Include(e => e.Lines)
            .FirstAsync(e => e.Id == invoice.JournalEntryId, ct);
        var centerIds = entry.Lines.Where(l => l.CostCenterId.HasValue).Select(l => l.CostCenterId!.Value).Distinct().ToList();
        var centers = await _db.Set<CostCenter>().AsNoTracking().Where(c => centerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        return new InvoiceJournalDto
        {
            EntryNumber = entry.EntryNumber, Date = entry.Date, Description = entry.Description,
            Lines = entry.Lines.OrderByDescending(l => l.Debit > 0).ThenBy(l => l.AccountCode)
                .Select(l =>
                {
                    var center = l.CostCenterId is Guid c ? centers.GetValueOrDefault(c) : null;
                    return new InvoiceJournalLineDto
                    {
                        AccountCode = l.AccountCode, AccountName = l.AccountName, Debit = l.Debit, Credit = l.Credit, Notes = l.Notes,
                        CostCenterId = l.CostCenterId, CostCenterCode = center?.Code, CostCenterName = center?.NameAr,
                    };
                })
                .ToList(),
            TotalDebit = entry.TotalDebit, TotalCredit = entry.TotalCredit,
            Subtotal = invoice.Subtotal, VatTotal = invoice.VatTotal, GrandTotal = invoice.GrandTotal,
            TotalCost = invoice.TotalCost, GrossProfit = invoice.GrossProfit,
        };
    }

    /// <summary>
    /// التصنيف الضريبي يُعرَّف على الصنف. فاتورة المبيعات: كل سطر صنف يأخذ تصنيفه ونسبته وسببه من تعريفه (أو من لقطته في النسخة
    /// المحفوظة عند التعديل)، ولا يغيّره إلا تجاوز بسبب وصلاحية (<see cref="ItemTaxTreatment"/>).
    /// المشتريات: السطر الذي لم يُحدَّد تصنيفه ويرتبط بصنف صفري أو معفى (بسببه) يرثهما منه، وغيره على نسبة سطره (ضريبة المورد).
    /// </summary>
    private async Task ApplyProductVatDefaultsAsync(CreateInvoiceDto r, CancellationToken ct)
    {
        _taxOverrides = new();
        if (r.Kind == InvoiceKind.Sales && !r.IsReturn)
        {
            var itemIds = r.Items.Where(i => i.ItemId != Guid.Empty).Select(i => i.ItemId).Distinct().ToList();
            if (itemIds.Count == 0) return;
            var products = await _db.Set<Product>().AsNoTracking().Where(p => itemIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
            var previous = r.ExistingInvoiceId is Guid existingId
                ? await _db.Set<InvoiceItemBase>().AsNoTracking().Where(l => l.InvoiceId == existingId && l.ItemId != Guid.Empty).ToListAsync(ct)
                : new List<InvoiceItemBase>();
            _taxOverrides = await ItemTaxTreatment.ApplyAsync(r.Items, products, previous,
                () => _permissions.HasPermissionAsync(PermissionService.InvoiceTaxTreatmentOverrideScreenId, ScreenAction.Edit, ct));
            return;
        }
        var ids = r.Items.Where(i => i.ItemId != Guid.Empty && i.VatCategory == null).Select(i => i.ItemId).Distinct().ToList();
        if (ids.Count == 0) return;
        var defined = await _db.Set<Product>().AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.VatCategory != VatCategory.Standard && p.VatExemptionReasonCode != null)
            .ToDictionaryAsync(p => p.Id, ct);
        foreach (var item in r.Items.Where(i => i.VatCategory == null && defined.ContainsKey(i.ItemId)))
        {
            var p = defined[item.ItemId];
            item.VatCategory = p.VatCategory;
            item.VatRate = 0;
            item.VatAmountOverride = null;
            item.VatExemptionReasonCode ??= p.VatExemptionReasonCode;
        }
    }

    private async Task ApplyProductExciseDefaultsAsync(CreateInvoiceDto r, CancellationToken ct)
    {
        // Returns are based on the original invoice snapshot. Purchases are outside this feature's scope.
        if (r.Kind is InvoiceKind.SalesReturn or InvoiceKind.PurchaseReturn) return;
        if (r.Kind != InvoiceKind.Sales)
        {
            foreach (var item in r.Items)
            {
                item.ExciseTaxRate = 0;
                item.ExciseRetailPrice = 0;
                item.ExciseStandardPrice = 0;
            }
            return;
        }

        var ids = r.Items.Where(i => i.ItemId != Guid.Empty).Select(i => i.ItemId).Distinct().ToList();
        var products = await _db.Set<Product>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var previous = r.ExistingInvoiceId.HasValue
            ? (await _db.Set<InvoiceItemBase>().AsNoTracking().Where(i => i.InvoiceId == r.ExistingInvoiceId.Value && ids.Contains(i.ItemId))
                .ToListAsync(ct)).GroupBy(i => i.ItemId).ToDictionary(g => g.Key, g => g.First())
            : new Dictionary<Guid, InvoiceItemBase>();
        foreach (var item in r.Items)
        {
            if (item.ItemId != Guid.Empty && previous.TryGetValue(item.ItemId, out var oldLine))
            {
                item.ExciseTaxRate = oldLine.ExciseTaxRate;
                item.ExciseRetailPrice = oldLine.ExciseRetailPrice;
                item.ExciseStandardPrice = oldLine.ExciseStandardPrice;
            }
            else if (item.ItemId != Guid.Empty && products.TryGetValue(item.ItemId, out var product))
            {
                item.ExciseTaxRate = product.ExciseTaxRate;
                item.ExciseRetailPrice = product.ExciseRetailPrice;
                item.ExciseStandardPrice = product.ExciseStandardPrice;
            }
            else
            {
                item.ExciseTaxRate = 0;
                item.ExciseRetailPrice = 0;
                item.ExciseStandardPrice = 0;
            }
        }
    }

    /// <summary>كل تجاوز جديد للتصنيف الضريبي: سطر في سجل التدقيق بالقيمة الأصلية والجديدة والسبب.</summary>
    private async Task LogTaxOverridesAsync(InvoiceBase invoice, CancellationToken ct)
    {
        foreach (var o in _taxOverrides)
            await _audit.LogAsync("INVOICE_TAX_TREATMENT_OVERRIDE", invoice.GetType().Name, invoice.Id.ToString(), $"{invoice.InvoiceNumber} — {o.Describe()}", ct);
        _taxOverrides = new();
    }

    /// <summary>
    /// فاتورة معارض: أسطر سيارات، أو مستند مرجعي تملكه وحدة المعارض (عقد بيع، أمر شراء)، أو مرتجع لأصل في جدول المعارض.
    /// تُكتب في جدول Invoice وحده؛ وكل ما عداها تجارة عامة في جدول نوعها.
    /// </summary>
    private async Task<bool> IsShowroomRequestAsync(CreateInvoiceDto r, CancellationToken ct)
    {
        if (r.VehicleLines.Count > 0 || r.VehicleBatches.Count > 0) return true;
        if (r.ReferenceId.HasValue)
            foreach (var ext in _extensions)
                if (await ext.OwnsReferenceAsync(r.ReferenceType, r.ReferenceId.Value, ct)) return true;
        return r.OriginalInvoiceId.HasValue && await _db.Set<Invoice>().AnyAsync(i => i.Id == r.OriginalInvoiceId, ct);
    }

    // ---------------- البناء والتحقق (بدون أي أثر جانبي) ----------------
    private async Task<T> BuildAsync<T>(CreateInvoiceDto r, CancellationToken ct) where T : InvoiceBase, new()
    {
        // كل مستند في جدوله: فواتير المعارض في Invoice، والتجارة العامة في جدول نوعها
        var showroom = IsShowroom<T>();
        if (showroom != await IsShowroomRequestAsync(r, ct))
            throw new ValidationFailedException(showroom ? Messages.ShowroomInvoicesOnly : Messages.ShowroomInvoiceNotTradeDocument);
        if (KindOf<T>() is { } kind && r.Kind != kind) throw new ValidationFailedException(Messages.InvalidDocumentType);

        if (r.ItemsDerivedFromVehicles) r.Items.Clear(); // إعادة تنفيذ بعد تعارض تزامن: البنود تُشتق من جديد
        // أسطر الوحدات (سيارات المعارض) تشتق r.Items وتتحقق منها
        IInvoiceExtension? owner = null;
        Func<Invoice, CancellationToken, Task>? attachLines = null;
        if (showroom)
            foreach (var ext in _extensions)
                if ((attachLines = await ext.ResolveAsync(r, ct)) != null) { owner = ext; break; }
        r.ItemsDerivedFromVehicles = attachLines != null;
        await ApplyProductVatDefaultsAsync(r, ct);
        await ApplyProductExciseDefaultsAsync(r, ct);
        await EnsureVatTreatmentAllowedAsync(r, ct);
        await EnsurePricingMethodAllowedAsync<T>(r, showroom, ct);
        var rentalCostCenters = await ValidateRentalResourceSelectionsAsync<T>(r, ct);
        foreach (var item in r.Items)
            if (item.ProjectResourceId is Guid resourceId && rentalCostCenters.TryGetValue(resourceId, out var costCenterId))
                item.CostCenterId = costCenterId;
        await EnsureCostCenterAllowedAsync(r, ct);
        var errors = new List<string>();
        ApplyDocumentVatTreatment(r, errors);
        if (!Enum.IsDefined(r.Kind)) errors.Add(Messages.InvalidDocumentType);
        if (!Enum.IsDefined(r.InvoiceType)) errors.Add(Messages.InvalidInvoiceType);
        if (r.Items.Count == 0) errors.Add(Messages.AtLeastOneItemRequired);
        if (r.InvoiceDiscount < 0) errors.Add(Messages.InvoiceDiscountCannotBeNegative);
        if (r.ExchangeRate <= 0) errors.Add(Messages.ExchangeRateMustBePositive);
        // التصنيف الضريبي غير الأساسي لا يحمل ضريبة
        if (r.Items.Any(i => i.VatCategory is VatCategory.ZeroRated or VatCategory.Exempt or VatCategory.OutOfScope && (i.VatRate > 0 || i.VatAmountOverride > 0)))
            errors.Add(Messages.VatCategoryRateMismatch);
        // هامش الربح لسطر سيارة مبيعات في فاتورة معرض وحده: ضريبته مبلغ ثابت (لا نسبة)
        if (r.Items.Any(i => i.VatCategory == VatCategory.MarginScheme
                && (!showroom || r.Kind is not (InvoiceKind.Sales or InvoiceKind.SalesReturn) || i.VatRate > 0 || i.ItemId != Guid.Empty)))
            errors.Add(Messages.MarginSchemeItemNotAllowed);
        ValidateExemptionReasons(r, errors);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);

        var isSales = r.Kind is InvoiceKind.Sales or InvoiceKind.SalesReturn;
        var isReturn = r.Kind is InvoiceKind.SalesReturn or InvoiceKind.PurchaseReturn;
        if (isReturn && r.OriginalInvoiceId == null) throw new ValidationFailedException(Messages.ReturnNeedsOriginalInvoice);
        if (isSales && r.Kind == InvoiceKind.Sales && r.InvoiceType is InvoiceType.CreditNote or InvoiceType.DebitNote)
            throw new ValidationFailedException(Messages.RegularInvoiceTypeInvalid);
        if (r.Kind == InvoiceKind.Purchase && r.InvoiceType == InvoiceType.TaxInvoice && !r.PartyId.HasValue)
            throw new ValidationFailedException(Messages.TaxPurchaseRequiresSupplier);

        var priced = PriceDocument(r);

        var productIds = r.Items.Where(i => i.ItemId != Guid.Empty).Select(i => i.ItemId).Distinct().ToList();
        if (r.Items.Any(i => i.ItemId == Guid.Empty && string.IsNullOrWhiteSpace(i.ItemName)))
            throw new ValidationFailedException(Messages.ServiceLineNeedsDescription);
        var products = await _db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var missing = productIds.Where(id => !products.ContainsKey(id)).ToList();
        if (missing.Count > 0) throw new ValidationFailedException(Messages.ItemsNotFoundInInvoice);
        // المجمّع والمركّب لا وجود لهما إلا حركة بيع: لا يُشتريان (تُشترى مكوناتهما)
        var kitIds = products.Values.Where(p => ProductItemTypes.SellsComponents(p.ItemType)).Select(p => p.Id).ToList();
        if (kitIds.Count > 0 && r.Kind is (InvoiceKind.Purchase or InvoiceKind.PurchaseReturn))
            throw new ValidationFailedException($"«{products[kitIds[0]].NameAr}» صنف مجمّع أو مركّب لا يُشترى: تُشترى مكوناته.");
        var kitComponents = await ProductComponents.LoadAsync(_db, kitIds, ct);
        var componentIds = kitComponents.Values.SelectMany(c => c).Where(c => c.ComponentProductId.HasValue).Select(c => c.ComponentProductId!.Value).Distinct().ToList();
        var componentProducts = await _db.Set<Product>().AsNoTracking().Where(p => componentIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Sku, p.NameAr, p.Unit }).ToDictionaryAsync(p => p.Id, ct);
        // المراكز من المنشأة ونشطة قبل حفظ المسودة نفسها (المرتجع يتبع مراكز أصله ولو أُوقفت بعده)
        r.JournalCostCenters = JournalCostCenterMap.Normalize(r.JournalCostCenters);
        await CostCenterRules.EnsureUsableAsync(_db, r.Items.Select(i => i.CostCenterId).Append(r.CostCenterId)
            .Concat(r.JournalCostCenters.Select(j => (Guid?)j.CostCenterId)), ct, requireActive: !isReturn);

        var invoice = Mapper.Map<T>(r);
        invoice.JournalCostCentersJson = JournalCostCenterMap.Pack(r.JournalCostCenters);
        invoice.Id = Guid.NewGuid();
        invoice.Uuid = Guid.NewGuid();
        invoice.Status = "draft";
        invoice.IsReturn = isReturn;
        invoice.Module = showroom ? owner?.Module ?? PlatformModules.CarShowroom : PlatformModules.Accounting;
        // تاريخ ووقت الإصدار بتوقيت المملكة (كما يُطبعان)، ويُحوَّلان للتوقيت العالمي في رمز QR
        invoice.IssueDate = r.IssueDate == default ? SaudiTime.Now : r.IssueDate;
        invoice.IssueTime = string.IsNullOrWhiteSpace(r.IssueTime) ? SaudiTime.Now.ToString("HH:mm:ss") : r.IssueTime;
        var baseCurrency = await _db.Set<Tenant>().AsNoTracking().Select(t => t.Currency).FirstAsync(ct);
        invoice.CurrencyCode = string.IsNullOrWhiteSpace(r.CurrencyCode) ? baseCurrency : r.CurrencyCode.Trim().ToUpperInvariant();
        if (invoice.CurrencyCode == baseCurrency) invoice.ExchangeRate = 1; // العملة الأساسية لا تُحوَّل
        // مستند بعملة أجنبية ميزة متقدمة (الباقة الصغيرة بالعملة الأساسية فقط)
        else if (!(await _entitlements.GetAsync(ct)).HasFeature(PlanFeatures.MultiCurrency))
            throw new ValidationFailedException(string.Format(Messages.FeatureNotInPlan, PlanFeatures.NameAr(PlanFeatures.MultiCurrency)));
        invoice.Subtotal = priced.NetTotal;
        invoice.ItemsDiscountTotal = priced.ItemsDiscountTotal;
        // خصم الفاتورة يُحفظ بطريقة تسعيرها (الشامل كما أُدخل) ليُعاد تسعيرها ومرتجعها منه؛ وبقية الإجماليات صافية دائماً
        invoice.InvoiceDiscount = r.PricingMethod == InvoicePricingMethod.Inclusive ? r.InvoiceDiscount : priced.InvoiceDiscount;
        invoice.DiscountTotal = priced.DiscountTotal;
        invoice.ExciseTaxTotal = priced.ExciseTaxTotal;
        invoice.VatTotal = priced.VatTotal;
        invoice.GrandTotal = priced.GrandTotal;
        invoice.ZatcaStatus = ZatcaSubmissionStatus.NotSubmitted;
        // خيار الشاشة «إرسال للهيئة»؛ إن لم يُرسل فبحسب إعداد المنشأة (الإرسال التلقائي عند الحفظ)
        invoice.SendToZatca = isSales && (r.SendToZatca
            ?? await _db.Set<Tenant>().AsNoTracking().Select(t => t.ZatcaConfig.AutoSubmitOnInvoiceSave).FirstAsync(ct));
        invoice.ZatcaQrCode = null; invoice.ZatcaHash = null; invoice.ZatcaUblXml = null; invoice.ZatcaPih = null;
        invoice.JournalEntryId = null;

        // حالة التسليم «غير مسلّمة»: لفاتورة البيع العامة وحدها (لا نقطة البيع ولا الفواتير الآلية من بيان أو مستند مصدر)،
        // وبلا أصناف مجمّعة/مركّبة (مكوناتها تُصرف مع الفاتورة)
        // وفاتورة الشراء «غير مستلمة» بالقواعد نفسها: قيمتها على «بضاعة مشتراة لم تُستلم» حتى يُدخلها بيان الاستلام
        if (invoice is SalesInvoice { DeliveryDeferred: true } or PurchaseInvoice { DeliveryDeferred: true })
        {
            if (invoice.Kind is not (InvoiceKind.Sales or InvoiceKind.Purchase) || isReturn
                || (r.ReferenceType != null && !IsCommercialSource(r.ReferenceType)))
                throw new ValidationFailedException(Messages.InvoiceDeliveryDeferredNotAllowed);
            if (r.Items.Any(i => products.TryGetValue(i.ItemId, out var kp) && ProductItemTypes.SellsComponents(ProductItemTypes.Normalize(kp.ItemType))))
                throw new ValidationFailedException(Messages.InvoiceDeliveryDeferredKitLines);
        }

        // وحدة كل سطر ومعاملها إلى الأساسية: البيع من وحدات الصنف، والمرتجع من سطر أصله؛ وغيرهما بالوحدة الأساسية كما كان
        var lineUnits = await ResolveLineUnitsAsync(r, products, ct);

        // الأسطر: الأرقام من السرفر دائماً، وبيانات الصنف من الكتالوج إن لم تُرسل.
        invoice.ClearLines();
        for (var i = 0; i < r.Items.Count; i++)
        {
            var src = r.Items[i]; var pl = priced.Lines[i];
            products.TryGetValue(src.ItemId, out var p); // بند الخدمة: بلا صنف
            var line = invoice.NewLine();
            line.ItemId = src.ItemId;
            line.ItemName = string.IsNullOrWhiteSpace(src.ItemName) ? p!.NameAr : src.ItemName;
            line.Sku = string.IsNullOrWhiteSpace(src.Sku) ? p?.Sku ?? string.Empty : src.Sku;
            line.Unit = lineUnits[i].Unit ?? (string.IsNullOrWhiteSpace(src.Unit) ? p?.Unit ?? string.Empty : src.Unit);
            line.UnitFactor = lineUnits[i].Factor;
            line.Quantity = src.Quantity; line.UnitPrice = src.UnitPrice; line.Discount = src.Discount;
            line.ItemType = p == null ? ProductItemTypes.Service : ProductItemTypes.Normalize(p.ItemType);
            // بند الخدمة يحمل تكلفته (مثل تكلفة السيارة) لأن لا مخزون يحسبها؛ والصنف الخدمي تكلفته المعيارية (والمرتجع تكلفة أصله)
            line.UnitCost = p == null ? src.UnitCost
                : line.ItemType == ProductItemTypes.Service ? (isReturn && src.UnitCost > 0 ? src.UnitCost : p.StandardCost ?? 0) : 0;
            if (p != null && ProductItemTypes.SellsComponents(line.ItemType))
            {
                var parts = kitComponents.GetValueOrDefault(p.Id)?.Where(c => c.Kind == ProductComponentKinds.Item && c.ComponentProductId.HasValue).ToList();
                if (parts == null || parts.Count == 0)
                    throw new ValidationFailedException($"«{p.NameAr}» صنف مجمّع أو مركّب بلا مكونات: عرّف مكوناته في شاشة الأصناف.");
                InvoiceLineStock.SetComponents(line, parts.Select(c =>
                {
                    var cp = componentProducts[c.ComponentProductId!.Value];
                    return new InvoiceLineComponent
                    {
                        ItemId = cp.Id, Sku = cp.Sku, ItemName = cp.NameAr, Unit = cp.Unit,
                        // المكوّن لكل وحدة أساسية من المجمّع: كمية السطر بوحدته × معاملها
                        QuantityPerUnit = c.Quantity, Quantity = Math.Round(src.Quantity * line.UnitFactor * c.Quantity, 4),
                    };
                }).ToList());
            }
            line.VatAmountOverride = src.VatAmountOverride;
            line.VatCategory = src.VatCategory ?? (src.VatRate > 0 || src.VatAmountOverride > 0 ? VatCategory.Standard : VatCategory.ZeroRated);
            line.VatExemptionReasonCode = VatExemptionReasons.Find(src.VatExemptionReasonCode)?.Code;
            line.VatOverrideReason = isSales && !string.IsNullOrWhiteSpace(src.VatOverrideReason) ? src.VatOverrideReason.Trim() : null;
            line.ExciseTaxRate = pl.ExciseTaxRate; line.ExciseRetailPrice = pl.ExciseRetailPrice;
            line.ExciseStandardPrice = pl.ExciseStandardPrice; line.ExciseTaxBaseAmount = pl.ExciseTaxBaseAmount;
            line.ExciseTaxAmount = pl.ExciseTaxAmount;
            line.VatRate = src.VatRate; line.VatAmount = pl.VatAmount; line.TotalBeforeVat = pl.Net; line.TotalAfterVat = pl.Total;
            // مركز الترويسة افتراضي فقط: يُحفظ المركز الفعلي على السطر فلا يغيّر تعديلُ الافتراضي لاحقاً أثرَ المستند
            line.CostCenterId = src.ProjectResourceId is Guid rentalId && rentalCostCenters.TryGetValue(rentalId, out var rentalCenter)
                ? rentalCenter : src.CostCenterId ?? r.CostCenterId;
            line.ProjectResourceId = src.ProjectResourceId;
            line.OriginalInvoiceItemId = src.OriginalInvoiceItemId;
            line.RevenueAccountCode = isSales && !string.IsNullOrWhiteSpace(src.RevenueAccountCode) ? src.RevenueAccountCode.Trim() : null;
            // التشغيلة للصنف المتتبَّع فقط (يُخصِّصها الترحيل ويكتب ما خُصِّص فعلاً)
            if (p?.TracksLots == true)
            {
                line.ItemBatchId = src.ItemBatchId;
                line.BatchNumber = string.IsNullOrWhiteSpace(src.BatchNumber) ? null : src.BatchNumber.Trim();
                line.ProductionDate = src.ProductionDate?.Date;
                line.ExpiryDate = src.ExpiryDate?.Date;
            }
            invoice.AddLine(line);
        }

        if (attachLines != null) await attachLines((Invoice)(object)invoice, ct);
        // هامش ربح السيارات المستعملة المؤهلة: علّقت الهيئة إلزام الفوترة الإلكترونية (المرحلة الثانية) لها منذ مايو 2023 ولا رمز
        // تصنيف لها في مواصفاتها، فلا تُرسل فاتورتها للهيئة (وتبقى بمتطلبات المرحلة الأولى: رمز QR المبسط)
        if (invoice.Lines.Any(l => l.VatCategory == VatCategory.MarginScheme)) invoice.SendToZatca = false;

        // الطرف
        var vatRegisteredCustomer = false;
        if (r.PartyId.HasValue)
        {
            if (isSales)
            {
                var c = await _db.Set<Customer>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.PartyId, ct)
                    ?? throw new ValidationFailedException(Messages.CustomerNotFound);
                vatRegisteredCustomer = c.IsVatRegistered;
                if (string.IsNullOrWhiteSpace(invoice.PartyName)) invoice.PartyName = c.NameAr;
                invoice.PartyVatNumber ??= c.VatNumber; invoice.PartyCrNumber ??= c.CrNumber; invoice.PartyIdType ??= c.IdType; invoice.PartyIdNumber ??= c.IdNumber;
                invoice.PartyPhone ??= c.Phone; invoice.PartyEmail ??= c.Email;
                // العنوان الوطني من بطاقة العميل ما لم يُرسل مع الفاتورة
                invoice.PartyStreet ??= c.Street; invoice.PartyBuildingNo ??= c.BuildingNo; invoice.PartyDistrict ??= c.District;
                invoice.PartyCity ??= c.City; invoice.PartyPostalCode ??= c.PostalCode; invoice.PartyAdditionalNo ??= c.AdditionalNo;
                if (string.IsNullOrWhiteSpace(invoice.PartyCountry)) invoice.PartyCountry = c.Country;
            }
            else
            {
                var s = await _db.Set<Supplier>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.PartyId, ct)
                    ?? throw new ValidationFailedException(Messages.SupplierNotFound);
                if (string.IsNullOrWhiteSpace(invoice.PartyName)) invoice.PartyName = s.NameAr;
                invoice.PartyVatNumber ??= s.VatNumber; invoice.PartyCrNumber ??= s.CrNumber; invoice.PartyIdType ??= s.IdType; invoice.PartyIdNumber ??= s.IdNumber;
                invoice.PartyPhone ??= s.Phone; invoice.PartyEmail ??= s.Email;
                invoice.PartyStreet ??= s.Street; invoice.PartyBuildingNo ??= s.BuildingNo; invoice.PartyDistrict ??= s.District;
                invoice.PartyCity ??= s.City; invoice.PartyPostalCode ??= s.PostalCode; invoice.PartyAdditionalNo ??= s.AdditionalNo;
                if (string.IsNullOrWhiteSpace(invoice.PartyCountry)) invoice.PartyCountry = s.Country;
            }
        }
        if (string.IsNullOrWhiteSpace(invoice.PartyName)) invoice.PartyName = isSales ? DefaultCashCustomer : "مورد نقدي";
        // الموقع: المحدَّد في الفاتورة أو الافتراضي للمنشأة
        var warehouse = await WarehouseStocks.ResolveAsync(_db, r.WarehouseId, ct);
        if (r.WarehouseId.HasValue && warehouse.Status != "active") throw new ValidationFailedException(Messages.WarehouseInactive);
        invoice.WarehouseId = warehouse.Id; invoice.WarehouseName = warehouse.NameAr;
        await ApplyLineWarehousesAsync(invoice, r, isReturn, ct);
        await ApplySellerAsync(invoice, r.Kind is InvoiceKind.Sales or InvoiceKind.SalesReturn ? r.SellerEmployeeId : null, requireActive: !isReturn, ct);

        invoice.PartyCountry =string.IsNullOrWhiteSpace(invoice.PartyCountry) ? "SA" : invoice.PartyCountry.Trim().ToUpperInvariant();
        var domesticParty = invoice.PartyCountry == "SA";
        if (domesticParty && !string.IsNullOrWhiteSpace(invoice.PartyVatNumber) && !SaudiVat.IsValid(invoice.PartyVatNumber))
            throw new ValidationFailedException(Messages.PartyVatNumberInvalid);
        // فاتورة بيع المعارض: نوعها من الحالة الضريبية للعميل لا من الشاشة — ضريبية (B2B) للمسجَّل، ومبسطة (B2C) لغيره
        // وللبيع بلا عميل (الافتراضي)
        if (showroom && r.Kind == InvoiceKind.Sales) invoice.InvoiceType = vatRegisteredCustomer ? InvoiceType.TaxInvoice : InvoiceType.Simplified;
        if (r.Kind == InvoiceKind.Sales && invoice.InvoiceType == InvoiceType.TaxInvoice) ValidateTaxInvoiceBuyer(invoice, domesticParty);

        // استقطاع العقد يُرحِّل على حساب يحدده رمزه: مسموح لفواتير المستخلصات التي ينشئها النظام وحدها (والمرتجع يرثه من فاتورته)
        if (!isReturn && !r.SystemGenerated
            && (r.PaymentMethod == PaymentMethod.ContractSettlement || r.PaymentSplits.Any(s => s.Method == PaymentMethod.ContractSettlement)))
            throw new ValidationFailedException(Messages.InvalidPaymentMethod);
        // الدفع: الطريقة المختارة تُعتمد (فعّالة) ونوعها يحدّد نوع الدفع. المرتجع يرث طرق فاتورته (وطريقة الرد تُعتمد في CreateReturnAsync).
        if (!isReturn && !r.IsSplitPayment)
            invoice.PaymentMethod = await PaymentMethodRules.MethodForAsync(_db, r.PaymentMethod, r.PaymentMethodCode, ct);
        invoice.PaymentSplits.Clear();
        if (r.IsSplitPayment)
        {
            if (r.PaymentSplits.Count == 0 || r.PaymentSplits.Any(s => s.Amount <= 0))
                throw new ValidationFailedException(Messages.SplitPaymentsNeedPositiveAmounts);
            if (PaymentSplitRules.TotalProblem(r.PaymentSplits.Sum(s => s.Amount), invoice.GrandTotal) is { } totalProblem)
                throw new ValidationFailedException(totalProblem);
            // طريقة واحدة لكل سطر (المرتجع يرث أسطر فاتورته كما هي)
            if (!isReturn) PaymentSplitRules.EnsureDistinctMethods(r.PaymentSplits.Select(s => (s.Method, s.PaymentMethodCode)));
            foreach (var s in r.PaymentSplits)
            {
                var method = isReturn ? s.Method : await PaymentMethodRules.MethodForAsync(_db, s.Method, s.PaymentMethodCode, ct);
                invoice.PaymentSplits.Add(new InvoicePaymentSplit { Method = method, PaymentMethodCode = s.PaymentMethodCode, Amount = s.Amount, Reference = s.Reference });
            }
        }
        // الباقي النقدي للعميل: بيان فقط، يُقبل مع دفع فيه نقد (غير المرتجع) وإلا يُصفَّر
        var paysCash = invoice.PaymentSplits.Count > 0 ? invoice.PaymentSplits.Any(s => s.Method == PaymentMethod.Cash) : invoice.PaymentMethod == PaymentMethod.Cash;
        invoice.ChangeAmount = !isReturn && paysCash ? Math.Max(0, Math.Round(r.ChangeAmount, 2)) : 0;
        var hasCredit = invoice.PaymentMethod == PaymentMethod.Credit || invoice.PaymentSplits.Any(s => s.Method == PaymentMethod.Credit);
        if (hasCredit && !r.PartyId.HasValue)
            throw new ValidationFailedException(isSales ? Messages.CreditSaleRequiresCustomer : Messages.CreditPurchaseRequiresSupplier);

        // فواتير المستخلصات معفاة: سقفها العقد لا الحد الائتماني (انظر CreateInvoiceDto.SkipCreditLimit)
        if (hasCredit && r.Kind == InvoiceKind.Sales && !r.SkipCreditLimit)
        {
            var customer = await _db.Set<Customer>().AsNoTracking().FirstAsync(x => x.Id == r.PartyId, ct);
            var creditPart = (invoice.PaymentMethod == PaymentMethod.Credit && !r.IsSplitPayment
                ? invoice.GrandTotal : invoice.PaymentSplits.Where(s => s.Method == PaymentMethod.Credit).Sum(s => s.Amount)) * invoice.ExchangeRate;
            if (customer.CreditLimit > 0 && customer.CurrentBalance + creditPart > customer.CreditLimit)
                throw new ConflictException(string.Format(Messages.CreditLimitExceeded, customer.CreditLimit, customer.CurrentBalance));
        }

        // المرتجع: تحقّق من الفاتورة الأصلية (في جدول المعارض للمعارض، وفي جدول نوعها للتجارة العامة)
        if (isReturn)
        {
            var orig = await _db.Set<InvoiceBase>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == r.OriginalInvoiceId, ct)
                ?? throw new ValidationFailedException(Messages.OriginalInvoiceNotFoundDot);
            var expected = r.Kind == InvoiceKind.SalesReturn ? InvoiceKind.Sales : InvoiceKind.Purchase;
            var expectedTable = showroom ? typeof(Invoice) : expected == InvoiceKind.Sales ? typeof(SalesInvoice) : typeof(PurchaseInvoice);
            if (orig.Kind != expected || orig.Status != "posted" || orig.GetType() != expectedTable) throw new ConflictException(Messages.OriginalInvoiceNotReturnable);
            // فترة الإرجاع من إعدادات المنشأة: منع نهائي بعد انقضائها (كل مسارات الإرجاع تمر من هنا).
            // تعديل مرتجع قائم بتاريخه نفسه لا يُعاد فحصه: أُنشئ داخل المدة، وتقصيرها لاحقاً لا يقفله
            var savedDate = r.ExistingInvoiceId is Guid existingId
                ? await _db.Set<InvoiceBase>().AsNoTracking().Where(i => i.Id == existingId).Select(i => (DateTime?)i.IssueDate).FirstOrDefaultAsync(ct)
                : null;
            var keepsDate = savedDate.HasValue && savedDate.Value.Date == invoice.IssueDate.Date;
            if (!keepsDate) await EnsureWithinReturnPeriodAsync(orig, invoice.IssueDate, ct);
            invoice.OriginalInvoiceNumber = orig.InvoiceNumber;
            invoice.Module = orig.Module;
        }
        return invoice;
    }

    private async Task<Dictionary<Guid, Guid>> ValidateRentalResourceSelectionsAsync<T>(CreateInvoiceDto request, CancellationToken ct)
        where T : InvoiceBase, new()
    {
        var selectedIds = request.Items.Where(item => item.ProjectResourceId.HasValue)
            .Select(item => item.ProjectResourceId!.Value).Distinct().ToList();
        if (selectedIds.Count == 0)
        {
            if (request.Kind == InvoiceKind.PurchaseReturn && request.IsReturn
                && request.OriginalInvoiceId is Guid originalInvoiceId
                && await HasRentalPurchaseLinesAsync(originalInvoiceId, ct))
                await ValidateReturnLineOriginsAsync<T>(request, ct);
            return new();
        }

        if (typeof(T) == typeof(PurchaseInvoice) && request.Kind == InvoiceKind.Purchase)
        {
            if (request.PartyId is not Guid supplierId) throw new ValidationFailedException("يجب اختيار مورد لربط سطر إيجار بمعدة مستأجرة.");
            var resources = await (from resource in _db.Set<ProjectResource>().AsNoTracking()
                                   join contract in _db.Set<CommercialContract>().AsNoTracking() on resource.ContractId equals contract.Id
                                   where selectedIds.Contains(resource.Id)
                                   select new { resource.Id, resource.Kind, resource.SupplierId, contract.CostCenterId })
                .ToDictionaryAsync(resource => resource.Id, ct);
            if (resources.Count != selectedIds.Count
                || resources.Values.Any(resource => resource.Kind != ProjectResourceKinds.RentedEquipment
                    || resource.SupplierId != supplierId || resource.CostCenterId == null))
                throw new ValidationFailedException("مرجع الإيجار غير صالح: اختر معدة مستأجرة مرتبطة بالمورد ومشروع له مركز تكلفة.");
            return resources.ToDictionary(resource => resource.Key, resource => resource.Value.CostCenterId!.Value);
        }

        if (typeof(T) == typeof(PurchaseReturn) && request.Kind == InvoiceKind.PurchaseReturn && request.IsReturn
            && request.OriginalInvoiceId is Guid originalId
            && await HasRentalPurchaseLinesAsync(originalId, ct))
        {
            await ValidateReturnLineOriginsAsync<T>(request, ct);
            return new();
        }
        throw new ValidationFailedException("لا يمكن ربط المعدة المستأجرة إلا بسطر فاتورة شراء للمورد.");
    }

    private Task<bool> HasRentalPurchaseLinesAsync(Guid invoiceId, CancellationToken ct)
        => _db.Set<PurchaseInvoice>().AnyAsync(invoice => invoice.Id == invoiceId
            && invoice.Items.Any(item => item.ProjectResourceId.HasValue), ct);

    private async Task ValidateReturnLineOriginsAsync<T>(CreateInvoiceDto request, CancellationToken ct)
        where T : InvoiceBase, new()
    {
        var returnItems = request.Items.Where(item => item.OriginalInvoiceItemId.HasValue).ToList();
        if (typeof(T) == typeof(PurchaseReturn)
            && request.Kind == InvoiceKind.PurchaseReturn && request.Items.Any(item => !item.OriginalInvoiceItemId.HasValue))
            throw new ValidationFailedException("يجب ربط كل سطر مرتجع بسطره في الفاتورة الأصلية.");
        if (request.Items.Any(item => item.ProjectResourceId.HasValue && !item.OriginalInvoiceItemId.HasValue))
            throw new ValidationFailedException("يجب ربط سطر إيجار مرتجع بسطره في الفاتورة الأصلية.");
        if (returnItems.Count == 0) return;
        var originalLineIds = returnItems.Select(item => item.OriginalInvoiceItemId!.Value).Distinct().ToList();
        var originals = await _db.Set<InvoiceItemBase>().AsNoTracking()
            .Where(item => originalLineIds.Contains(item.Id) && item.InvoiceId == request.OriginalInvoiceId)
            .ToDictionaryAsync(item => item.Id, ct);
        if (originals.Count != originalLineIds.Count || returnItems.Any(item =>
                !originals.TryGetValue(item.OriginalInvoiceItemId!.Value, out var original)
                || original.ItemId != item.ItemId || original.ProjectResourceId != item.ProjectResourceId))
            throw new ValidationFailedException("بيانات سطر المرتجع لا تطابق الفاتورة الأصلية.");
    }

    /// <summary>
    /// معالجة الفاتورة غير الأساسية (صفري / معفى / صادرات) تتغلّب على تصنيف كل سطر: الأسطر كلها بنسبة 0 وتصنيف المعالجة
    /// وسببها، ويسقط مبلغ الضريبة الثابت (هامش الربح). تُطبَّق بعد اشتقاق أسطر السيارات وافتراضات الأصناف وقبل التسعير،
    /// فالفاتورة والقيد والإقرار ورمز الهيئة كلها من الأسطر المحفوظة بعد الفرض.
    /// </summary>
    /// <summary>
    /// اختيار معالجة غير أساسية يتجاوز تصنيف كل الأصناف دفعة واحدة، فيحتاج صلاحية تجاوز التصنيف الضريبي نفسها؛
    /// المرتجع يرث معالجة أصله، وتعديل فاتورة بمعالجتها المحفوظة نفسها لا يُعدّ تجاوزاً جديداً.
    /// </summary>
    private async Task EnsureVatTreatmentAllowedAsync(CreateInvoiceDto r, CancellationToken ct)
    {
        if (r.VatTreatment == InvoiceVatTreatment.Standard || r.OriginalInvoiceId.HasValue) return;
        if (r.ExistingInvoiceId is Guid id
            && await _db.Set<InvoiceBase>().AsNoTracking().AnyAsync(i => i.Id == id && i.VatTreatment == r.VatTreatment, ct)) return;
        if (!await _permissions.HasPermissionAsync(PermissionService.InvoiceTaxTreatmentOverrideScreenId, ScreenAction.Edit, ct))
            throw new ForbiddenException(Messages.InvoiceTaxTreatmentOverrideForbidden);
    }

    /// <summary>
    /// طريقة التسعير يختارها المستخدم بصلاحيتها: «غير شامل» بـ <see cref="PermissionService.InvoiceVatExclusivePricingScreenId"/>
    /// و«شامل» بـ <see cref="PermissionService.InvoiceVatInclusivePricingScreenId"/>. المرتجع يرث طريقة أصله، والمستند الذي ينشئه النظام
    /// (تحويل من عرض/أمر/سند، مستخلص...) غير شامل دائماً، وتعديل فاتورة بطريقتها المحفوظة نفسها لا يُعدّ اختياراً جديداً (كالمعالجة الضريبية).
    /// فواتير المعارض بطريقة «غير شامل» وحدها ولا يتغير سلوكها.
    /// </summary>
    private async Task EnsurePricingMethodAllowedAsync<T>(CreateInvoiceDto r, bool showroom, CancellationToken ct) where T : InvoiceBase
    {
        if (!Enum.IsDefined(r.PricingMethod)) throw new ValidationFailedException(Messages.InvoicePricingMethodInvalid);
        var exclusive = r.PricingMethod == InvoicePricingMethod.Exclusive;
        if (showroom)
        {
            if (!exclusive) throw new ValidationFailedException(Messages.VatInclusivePricingShowroomNotAllowed);
            return;
        }
        if (r.OriginalInvoiceId.HasValue || (r.SystemGenerated && exclusive)) return;
        // بيع نقطة البيع شامل حين يكون سعر بطاقة الصنف شاملاً (إعداد المنشأة، لا اختيار المستخدم)
        if (r.SystemGenerated && r.Kind == InvoiceKind.Sales && r.ReferenceType == "pos_transaction"
            && await _db.Set<AccountingSettings>().AsNoTracking().Select(s => s.ItemPricesIncludeVat).FirstOrDefaultAsync(ct)) return;
        if (r.ExistingInvoiceId is Guid id
            && await _db.Set<T>().AsNoTracking().AnyAsync(i => i.Id == id && i.PricingMethod == r.PricingMethod, ct)) return;
        // الشامل في المبيعات معطّل للمنشأة افتراضياً: لا يتيحه حتى المالك والمدير حتى يُفعَّل في إعدادات الحسابات
        if (!exclusive && r.Kind == InvoiceKind.Sales
            && !await _db.Set<AccountingSettings>().AsNoTracking().Select(s => s.AllowSalesVatInclusivePricing).FirstOrDefaultAsync(ct))
            throw new ForbiddenException(Messages.SalesVatInclusivePricingDisabled);
        var screen = exclusive ? PermissionService.InvoiceVatExclusivePricingScreenId : PermissionService.InvoiceVatInclusivePricingScreenId;
        if (!await _permissions.HasPermissionAsync(screen, ScreenAction.Edit, ct))
            throw new ForbiddenException(exclusive ? Messages.VatExclusivePricingForbidden : Messages.VatInclusivePricingForbidden);
    }

    /// <summary>
    /// مركز التكلفة الذي يختاره المستخدم (الافتراضي أو مركز سطر) يحتاج صلاحية تعيينه؛ والمرتجع يتبع مراكز أصله، والفاتورة الناتجة
    /// عن مستند مصدر تحمل مركزه. تعديل مستند بمراكزه المحفوظة نفسها (ولو تغيّرت أسطره) لا يُعدّ تعييناً جديداً.
    /// </summary>
    private async Task EnsureCostCenterAllowedAsync(CreateInvoiceDto r, CancellationToken ct)
    {
        if (r.CostCenterFromSource || r.OriginalInvoiceId.HasValue) return;
        bool changed;
        var journal = JournalCostCenterMap.Pack(r.JournalCostCenters);
        if (r.ExistingInvoiceId is Guid id)
        {
            var saved = await _db.Set<InvoiceBase>().AsNoTracking().Where(i => i.Id == id)
                .Select(i => new { i.CostCenterId, i.JournalCostCentersJson }).FirstOrDefaultAsync(ct);
            var savedLines = await _db.Set<InvoiceItemBase>().AsNoTracking().Where(l => l.InvoiceId == id && l.CostCenterId != null)
                .Select(l => l.CostCenterId).Distinct().ToListAsync(ct);
            changed = r.CostCenterId != saved?.CostCenterId || r.Items.Any(i => i.CostCenterId.HasValue && !savedLines.Contains(i.CostCenterId))
                || journal != JournalCostCenterMap.Pack(JournalCostCenterMap.Unpack(saved?.JournalCostCentersJson)); // مراكز قسم القيد
        }
        else changed = r.CostCenterId.HasValue || r.Items.Any(i => i.CostCenterId.HasValue) || journal != null;
        await CostCenterRules.EnsureCanAssignAsync(_permissions, changed, ct);
    }

    private static void ApplyDocumentVatTreatment(CreateInvoiceDto r, List<string> errors)
    {
        if (!Enum.IsDefined(r.VatTreatment)) { errors.Add(Messages.VatTreatmentInvalid); return; }
        if (r.VatTreatment == InvoiceVatTreatment.Standard) { r.VatTreatmentReasonCode = null; return; }

        var category = r.VatTreatment == InvoiceVatTreatment.Exempt ? VatCategory.Exempt : VatCategory.ZeroRated;
        var reason = VatExemptionReasons.Find(r.VatTreatmentReasonCode)
            ?? (r.VatTreatment == InvoiceVatTreatment.Export ? VatExemptionReasons.Find("VATEX-SA-32") : null);
        if (reason != null && (reason.Category != category || (r.VatTreatment == InvoiceVatTreatment.Export && !reason.IsExport)))
        { errors.Add(Messages.VatTreatmentReasonMismatch); return; }
        var isSales = r.Kind is InvoiceKind.Sales or InvoiceKind.SalesReturn;
        if (isSales && reason == null) { errors.Add(Messages.VatTreatmentReasonRequired); return; }

        r.VatTreatmentReasonCode = reason?.Code;
        foreach (var item in r.Items)
        {
            item.VatCategory = category;
            item.VatRate = 0;
            item.VatAmountOverride = null;
            item.VatOverrideReason = null; // قرار على مستوى المستند لا تجاوز لتصنيف صنف
            item.VatExemptionReasonCode = reason?.Code;
        }
    }

    /// <summary>
    /// سبب الإعفاء: إن أُرسل فهو رمز معروف يطابق تصنيف سطره؛ وفي فاتورة المبيعات يلزم لكل سطر حُدِّد له تصنيف غير أساسي
    /// (السطر بنسبة 0 بلا تصنيف — كبيع نقطة البيع لصنف صفري — يبقى صفرياً بلا سبب).
    /// </summary>
    private static void ValidateExemptionReasons(CreateInvoiceDto r, List<string> errors)
    {
        var isSales = r.Kind is InvoiceKind.Sales or InvoiceKind.SalesReturn;
        foreach (var item in r.Items)
        {
            var reason = VatExemptionReasons.Find(item.VatExemptionReasonCode);
            if (!string.IsNullOrWhiteSpace(item.VatExemptionReasonCode) && reason == null) { errors.Add(Messages.VatExemptionReasonUnknown); return; }
            var category = item.VatCategory ?? (reason?.Category);
            if (reason != null && category != reason.Category) { errors.Add(Messages.VatExemptionReasonCategoryMismatch); return; }
            if (reason != null) item.VatCategory ??= reason.Category;
            if (isSales && reason == null && !r.IsReturn && item.VatCategory is VatCategory.ZeroRated or VatCategory.Exempt or VatCategory.OutOfScope)
            { errors.Add(Messages.VatExemptionReasonRequired); return; }
        }
    }

    /// <summary>
    /// الفاتورة الضريبية (B2B) تحمل بيانات المشتري الإلزامية: اسمه، رقمه الضريبي (للمشتري داخل المملكة)، وعنوانه:
    /// الشارع والمدينة دائماً، ورقم المبنى (4 أرقام) والحي والرمز البريدي (5 أرقام) للعنوان الوطني السعودي.
    /// </summary>
    private static void ValidateTaxInvoiceBuyer(InvoiceBase invoice, bool domestic)
    {
        var missing = new List<string>();
        if (!invoice.PartyId.HasValue && invoice.PartyName == DefaultCashCustomer) missing.Add(Messages.BuyerFieldName);
        if (domestic && string.IsNullOrWhiteSpace(invoice.PartyVatNumber)) missing.Add(Messages.BuyerFieldVatNumber);
        if (string.IsNullOrWhiteSpace(invoice.PartyStreet)) missing.Add(Messages.BuyerFieldStreet);
        if (string.IsNullOrWhiteSpace(invoice.PartyCity)) missing.Add(Messages.BuyerFieldCity);
        if (domestic)
        {
            if (!IsDigits(invoice.PartyBuildingNo, 4)) missing.Add(Messages.BuyerFieldBuildingNo);
            if (string.IsNullOrWhiteSpace(invoice.PartyDistrict)) missing.Add(Messages.BuyerFieldDistrict);
            if (!IsDigits(invoice.PartyPostalCode, 5)) missing.Add(Messages.BuyerFieldPostalCode);
        }
        if (missing.Count == 0) return;
        var message = string.Format(Messages.TaxInvoiceBuyerDataRequired, string.Join("، ", missing));
        throw new ValidationFailedException(message, missing.Prepend(message));
    }

    private static bool IsDigits(string? value, int length) => value?.Trim() is { } v && v.Length == length && v.All(char.IsAsciiDigit);

    // ---------------- التشغيلات على أسطر الفاتورة ----------------
    /// <summary>
    /// تشغيلة السطر في الحركة: الشراء برقمه وتاريخيه (وارد)؛ البيع ومرتجع الشراء بالتشغيلة المختارة وإلا الأقرب انتهاءً (صادر)؛
    /// المرتجع على أصل يعود لتشغيلات أصله ما لم تُختر تشغيلة، والمرتجع بلا أصل كالوارد برقمه أو تشغيلته المختارة.
    /// </summary>
    private static void ApplyLineBatch(InvoiceBase invoice, InvoiceItemBase item, RecordStockMovementDto movement,
        Dictionary<Guid, List<BatchAllocationDto>> returnPools)
    {
        movement.SourceLineId = item.Id;
        switch (invoice.Kind)
        {
            case InvoiceKind.Purchase:
                movement.BatchNumber = item.BatchNumber; movement.ProductionDate = item.ProductionDate; movement.ExpiryDate = item.ExpiryDate;
                break;
            case InvoiceKind.Sales:
                movement.ItemBatchId = item.ItemBatchId;
                break;
            default: // المرتجعات
                movement.ItemBatchId = item.ItemBatchId;
                if (item.ItemBatchId == null && returnPools.TryGetValue(item.ItemId, out var pool))
                    movement.BatchAllocations = ItemBatches.Take(pool, InvoiceLineStock.BaseQuantity(item));
                else if (item.ItemBatchId == null && invoice.Kind == InvoiceKind.SalesReturn)
                {
                    movement.BatchNumber = item.BatchNumber; movement.ProductionDate = item.ProductionDate; movement.ExpiryDate = item.ExpiryDate;
                }
                break;
        }
    }

    /// <summary>ما خُصِّص فعلاً يُكتب على السطر للعرض والطباعة: تشغيلة واحدة برقمها، أو ملخص التوزيع وأقرب انتهاء.</summary>
    private static void WriteBackBatches(InvoiceBase invoice, InvoiceItemBase item, List<BatchAllocationDto> parts)
    {
        if (parts.Count == 0) return;
        if (invoice.Kind == InvoiceKind.Purchase)
        {
            item.ExpiryDate = parts[0].ExpiryDate; // المقترح من مدة الصلاحية إن لم يُدخل
            return;
        }
        item.BatchNumber = ItemBatches.Summary(parts.Select(a => (a.BatchNumber, a.ExpiryDate, a.Quantity)));
        item.ExpiryDate = parts.Min(a => a.ExpiryDate);
    }

    /// <summary>تشغيلات أصل المرتجع لكل صنف (صادرة في البيع، واردة في الشراء) ناقص ما أعادته المرتجعات الأخرى عليه.</summary>
    private async Task<Dictionary<Guid, List<BatchAllocationDto>>> ReturnBatchPoolsAsync(InvoiceBase invoice, CancellationToken ct)
    {
        var pools = new Dictionary<Guid, List<BatchAllocationDto>>();
        if (invoice.Kind is not (InvoiceKind.SalesReturn or InvoiceKind.PurchaseReturn) || invoice.OriginalInvoiceId is not { } originalId) return pools;
        var otherReturns = await _db.Set<InvoiceBase>().AsNoTracking()
            .Where(i => i.OriginalInvoiceId == originalId && i.Id != invoice.Id && i.Kind == invoice.Kind).Select(i => i.Id).ToListAsync(ct);
        foreach (var itemId in invoice.Lines.Where(InvoiceLineStock.MovesItself).Select(i => i.ItemId).Distinct())
        {
            var pool = await ItemBatches.OfDocumentAsync(_db, "invoice", originalId, itemId, null, ct);
            if (pool.Count == 0) continue;
            foreach (var returnId in otherReturns)
                foreach (var done in await ItemBatches.OfDocumentAsync(_db, "invoice", returnId, itemId, null, ct))
                    if (pool.FirstOrDefault(a => a.ItemBatchId == done.ItemBatchId) is { } match) match.Quantity = Math.Max(0, match.Quantity - done.Quantity);
            pools[itemId] = pool.Where(a => a.Quantity > 0).ToList();
        }
        return pools;
    }

    // ---------------- الترحيل: مخزون + قيد + QR ----------------
    /// <param name="preview">معاينة القيد: يتخطى إنشاء/بيع المركبات و QR (لا أثر لهما على القيد) ليُسمح بمعاينة مسودة ناقصة الشواسيه.</param>
    /// <summary>
    /// مرتجع فاتورة «غير مسلّمة»: لكل سطر مخزني الكمية التي تعود للمخزون (بالأساسية) وتكلفة وحدتها. المرتجع يخرج من غير المسلَّم أولاً
    /// (بلا حركة)، وما زاد عنه = ما سُلّم فعلاً ويعود بتكلفة ما خرج ببيانات الفاتورة. الحساب تراكمي مع المرتجعات المرحّلة السابقة فلا يتكرر.
    /// </summary>
    private async Task<Dictionary<Guid, (decimal Qty, decimal UnitCost)>> DeferredReturnStockAsync(InvoiceBase ret, Guid originalId, CancellationToken ct)
    {
        var original = (await _db.Set<SalesInvoiceItem>().AsNoTracking().Where(i => i.InvoiceId == originalId).ToListAsync(ct)).Cast<InvoiceItemBase>().ToList();
        var issued = await DeferredDeliveryStock.IssuedByInvoiceLineAsync(_db, originalId, ct);
        var returned = await DeferredDeliveryStock.ReturnedBaseByLineAsync(_db, original, originalId, ret.Id, ct);
        var result = new Dictionary<Guid, (decimal Qty, decimal UnitCost)>();
        foreach (var item in ret.Lines.Where(InvoiceLineStock.MovesItself))
        {
            if (DeferredDeliveryStock.OriginOf(original, item.OriginalInvoiceItemId, item.ItemId) is not Guid originId) continue;
            var origin = original.First(o => o.Id == originId);
            var (deliveredQty, deliveredValue) = issued.GetValueOrDefault(originId);
            var undelivered = Math.Max(0, InvoiceLineStock.BaseQuantity(origin) - deliveredQty);
            var before = returned.GetValueOrDefault(originId);
            var after = before + InvoiceLineStock.BaseQuantity(item);
            returned[originId] = after;
            var stock = Math.Max(0, after - undelivered) - Math.Max(0, before - undelivered);
            result[item.Id] = (Math.Round(stock, 4), deliveredQty > 0 ? Math.Round(deliveredValue / deliveredQty, 4) : 0);
        }
        return result;
    }

    private async Task FinalizeAsync(InvoiceBase invoice, CancellationToken ct, bool preview = false)
    {
        if (invoice.InvoiceNumber.StartsWith(DraftPrefix, StringComparison.Ordinal))
        {
            // نقاط البيع بترقيم مستقل تلقائي دائماً (ومنه المزامنة أوف لاين)؛ الباقي بإعداد نوعه: البادئة والطول والنطاق والسنة المالية لتاريخ الفاتورة
            var pos = invoice.ReferenceType == "pos_transaction" && invoice.Kind == InvoiceKind.Sales;
            invoice.InvoiceNumber = await _numbers.AssignAsync(new DocumentNumberRequest
            {
                Key = pos ? "pos_invoice" : KeyFor(invoice.Kind), DefaultPrefix = pos ? "POS-" : PrefixFor(invoice.Kind),
                WarehouseId = invoice.WarehouseId, Date = invoice.IssueDate,
                Exists = (n, c) => _db.Set<InvoiceBase>().AnyAsync(i => i.InvoiceNumber == n, c),
            }, ct);
        }
        if (!preview && invoice is Invoice car)
            foreach (var ext in _extensions) await ext.OnPostingAsync(car, ct); // سجلات الوحدات (إنشاء المركبات وبيعها...)
        var isSales = invoice.Kind is InvoiceKind.Sales or InvoiceKind.SalesReturn;
        var tenant = await _db.Set<Tenant>().AsNoTracking().FirstAsync(ct);

        // الدفاتر والمخزون بالعملة الأساسية: مبالغ المستند تُحوَّل بسعر صرفه (1 للعملة الأساسية)
        var fx = invoice.ExchangeRate <= 0 ? 1 : invoice.ExchangeRate;
        var baseTotal = DocumentPricing.Round(invoice.GrandTotal * fx);
        var baseVat = DocumentPricing.Round(invoice.VatTotal * fx);
        var baseNet = baseTotal - baseVat;
        var payments = ToBase(await ResolvePaymentsAsync(invoice, ct), fx, invoice.GrandTotal, baseTotal);
        decimal totalCost = 0;
        var stockByCenter = new CostCenterTotals(); // تغيّر قيمة المخزون لكل مركز تكلفة من أسطره

        Dictionary<Guid, decimal> originalCosts = new();
        Dictionary<Guid, InvoiceItemBase> originalLines = new();
        if (invoice.Kind == InvoiceKind.SalesReturn && invoice.OriginalInvoiceId.HasValue)
        {
            originalLines = (await _db.Set<InvoiceItemBase>().AsNoTracking().Where(i => i.InvoiceId == invoice.OriginalInvoiceId).ToListAsync(ct))
                .GroupBy(i => i.ItemId).ToDictionary(g => g.Key, g => g.First());
            // تكلفة الوحدة الأساسية (تكلفة السطر لكل وحدة من وحدته ÷ معاملها)
            originalCosts = originalLines.ToDictionary(x => x.Key, x => InvoiceLineStock.BaseUnitCost(x.Value, x.Value.UnitCost));
        }

        // المرتجع على أصل: يعود لتشغيلات الأصل بترتيبها، بعد خصم ما أعادته مرتجعاته السابقة
        var returnPools = await ReturnBatchPoolsAsync(invoice, ct);

        // فاتورة «غير مسلّمة»: لا صرف ولا تكلفة الآن؛ بيانات التسليم منها تصرف الأصناف وترحّل تكلفتها (DeferredDeliveryStock)
        var deferredSale = invoice is SalesInvoice { DeliveryDeferred: true } && invoice.Kind == InvoiceKind.Sales;
        // مرتجع فاتورة «غير مسلّمة»: يخرج من غير المسلَّم أولاً بلا حركة، وما زاد عنه يعود للمخزون بتكلفة ما سُلّم
        var deferredReturn = invoice.Kind == InvoiceKind.SalesReturn && invoice.OriginalInvoiceId is Guid deferredOriginalId
            && await _db.Set<SalesInvoice>().AsNoTracking().AnyAsync(i => i.Id == deferredOriginalId && i.DeliveryDeferred, ct)
            ? await DeferredReturnStockAsync(invoice, deferredOriginalId, ct) : null;
        // فاتورة شراء «غير مستلمة» ومرتجعها: لا حركة مخزون؛ قيمة الأصناف بصافيها على «بضاعة مشتراة لم تُستلم» (بيان الاستلام يُدخلها).
        // المرتجع المالي لا يتجاوز غير المستلَم (EnsurePurchaseReturnFitsReceiptsAsync)، فيخرج من الحساب نفسه بلا حركة
        var goodsNotReceived = invoice is PurchaseInvoice { DeliveryDeferred: true } && invoice.Kind == InvoiceKind.Purchase
            || invoice.Kind == InvoiceKind.PurchaseReturn && invoice.OriginalInvoiceId is Guid purchaseOriginalId
                && await _db.Set<PurchaseInvoice>().AsNoTracking().AnyAsync(i => i.Id == purchaseOriginalId && i.DeliveryDeferred, ct);
        var noStockNow = deferredSale || goodsNotReceived;

        // المجمّع والمركّب: تُصرف مكوناته (أو تعود في المرتجع) بكمياتها، وتكلفة السطر مجموع قيمة ما خرج منها
        foreach (var item in invoice.Lines.Where(l => !noStockNow && InvoiceLineStock.SellsComponents(l)))
        {
            var components = InvoiceLineStock.Components(item);
            // المرتجع على أصل: المكونات التي خرجت فعلاً مع الأصل بتكلفتها، بنسبة الكمية المرتجعة
            if (invoice.Kind == InvoiceKind.SalesReturn && originalLines.TryGetValue(item.ItemId, out var origin) && origin.Quantity > 0
                && InvoiceLineStock.Components(origin) is { Count: > 0 } sold)
                components = sold.Select(c => new InvoiceLineComponent
                {
                    ItemId = c.ItemId, Sku = c.Sku, ItemName = c.ItemName, Unit = c.Unit, QuantityPerUnit = c.QuantityPerUnit,
                    Quantity = Math.Round(c.Quantity * item.Quantity / origin.Quantity, 4), UnitCost = c.UnitCost,
                }).ToList();
            if (components.Count == 0) throw new ValidationFailedException($"«{item.ItemName}» صنف مجمّع أو مركّب بلا مكونات.");
            decimal lineValue = 0;
            foreach (var c in components.Where(c => c.Quantity > 0))
            {
                var isSale = invoice.Kind == InvoiceKind.Sales;
                var inCost = c.UnitCost;
                if (!isSale && inCost <= 0)
                    inCost = await _db.Set<Product>().AsNoTracking().Where(p => p.Id == c.ItemId).Select(p => p.AverageCost).FirstOrDefaultAsync(ct);
                var recorded = await _inventory.RecordMovementAsync(new RecordStockMovementDto
                {
                    ItemId = c.ItemId, Quantity = c.Quantity, WarehouseId = SiteOf(invoice, item),
                    Type = isSale ? StockMovementType.OutSales : StockMovementType.AdjustmentIn, UnitCost = isSale ? 0 : inCost,
                    UnitPrice = VatPricing.NetUnitPrice(invoice.PricingMethod, item.UnitPrice, item.VatRate), ReferenceNumber = invoice.InvoiceNumber, Date = invoice.IssueDate,
                    Notes = $"مكوّن {item.ItemName}", SourceType = "invoice", SourceId = invoice.Id, SourceLineId = item.Id,
                }, ct);
                c.UnitCost = isSale ? recorded.UnitCost : inCost;
                lineValue += isSale ? -recorded.ValueChange : recorded.ValueChange;
            }
            InvoiceLineStock.SetComponents(item, components);
            item.UnitCost = item.Quantity == 0 ? 0 : Math.Round(lineValue / item.Quantity, 4);
            totalCost += lineValue;
            stockByCenter.Add(item.CostCenterId, lineValue, SiteOf(invoice, item));
        }

        foreach (var item in invoice.Lines.Where(l => !noStockNow && InvoiceLineStock.MovesItself(l)))
        {
            // مرتجع «غير المسلّمة»: الكمية العائدة للمخزون (بالأساسية) وتكلفتها مما سُلّم؛ صفر = من غير المسلَّم، بلا حركة ولا تكلفة
            decimal? deferredQty = null;
            if (deferredReturn != null)
            {
                var (qty, unitCost) = deferredReturn.GetValueOrDefault(item.Id);
                if (qty <= 0) { item.UnitCost = 0; continue; }
                deferredQty = qty;
                originalCosts[item.ItemId] = unitCost;
            }
            var movement = invoice.Kind switch
            {
                InvoiceKind.Sales => new RecordStockMovementDto { Type = StockMovementType.OutSales },
                // تكلفة الوحدة الأساسية: صافي السطر ÷ كميته بالأساسية (الكمية × معامل وحدته)
                InvoiceKind.Purchase => new RecordStockMovementDto { Type = StockMovementType.InPurchase, UnitCost = InvoiceLineStock.BaseQuantity(item) == 0 ? 0 : Math.Round(item.TotalBeforeVat * fx / InvoiceLineStock.BaseQuantity(item), 4) },
                InvoiceKind.SalesReturn => new RecordStockMovementDto { Type = StockMovementType.AdjustmentIn, UnitCost = originalCosts.GetValueOrDefault(item.ItemId) },
                _ => new RecordStockMovementDto { Type = StockMovementType.AdjustmentOut },
            };
            // المخزون بالوحدة الأساسية: الكمية × معامل وحدة السطر، وسعر الوحدة الأساسية = سعر وحدة السطر ÷ المعامل
            var factor = InvoiceLineStock.Factor(item);
            movement.ItemId = item.ItemId; movement.Quantity = deferredQty ?? InvoiceLineStock.BaseQuantity(item);
            movement.UnitPrice = Math.Round(VatPricing.NetUnitPrice(invoice.PricingMethod, item.UnitPrice, item.VatRate) / factor, 4);
            movement.ReferenceNumber = invoice.InvoiceNumber;
            movement.Date = invoice.IssueDate;
            movement.SourceType = "invoice"; movement.SourceId = invoice.Id;
            movement.WarehouseId = SiteOf(invoice, item);
            ApplyLineBatch(invoice, item, movement, returnPools);

            var recorded = await _inventory.RecordMovementAsync(movement, ct);
            WriteBackBatches(invoice, item, recorded.Batches);
            // تكلفة السطر لكل وحدة من وحدته (الحركة بتكلفة الوحدة الأساسية)
            item.UnitCost = Math.Round((invoice.Kind == InvoiceKind.Purchase ? movement.UnitCost : recorded.UnitCost) * factor, 4);
            // تغيّر القيمة الدفترية للصنف لا الكمية × التكلفة: حساب المخزون يبقى مساوياً لقيمة الأصناف بلا هللات تقريب
            // بإشارته: الإضافة بآخر سعر شراء أقل تُنقص قيمة الرصيد كله
            var valueChange = invoice.Kind is InvoiceKind.Purchase or InvoiceKind.SalesReturn ? recorded.ValueChange : -recorded.ValueChange;
            // مرتجع جزؤه من غير المسلَّم: تكلفة السطر لكل وحدة من قيمة ما عاد فعلاً على كمية السطر كلها
            if (deferredQty != null) item.UnitCost = item.Quantity == 0 ? 0 : Math.Round(valueChange / item.Quantity, 4);
            totalCost += valueChange;
            stockByCenter.Add(item.CostCenterId, valueChange, SiteOf(invoice, item));
        }
        if (goodsNotReceived)
        {
            // صافي كل سطر مخزني (بعد خصم الفاتورة) قيمةً على حساب البضاعة غير المستلمة، بمركزه وموقعه، فلا فرق سعر الآن
            foreach (var (line, amount) in LineNets(invoice, fx, baseNet).Where(x => !InvoiceLineStock.IsNonStock(x.Line)))
            {
                line.UnitCost = line.Quantity == 0 ? 0 : Math.Round(amount / line.Quantity, 4);
                totalCost += amount;
                stockByCenter.Add(line.CostCenterId, amount, SiteOf(invoice, line));
            }
            await DefaultAccounts.EnsureAsync(_db, ct, DefaultAccounts.GoodsNotReceived);
        }
        var stockCost = totalCost; // قيمة ما دخل/خرج من المخزون فعلاً (الأصناف المخزنية فقط)
        var costByCenter = stockByCenter.Copy(); // تكلفة المبيعات لكل مركز: مخزون أسطره ثم تكلفة بنود خدماته
        foreach (var svc in invoice.Lines.Where(i => InvoiceLineStock.IsNonStock(i) && i.UnitCost > 0))
        {
            var svcCost = Math.Round(svc.Quantity * svc.UnitCost, 2);
            totalCost += svcCost;
            costByCenter.Add(svc.CostCenterId, svcCost, SiteOf(invoice, svc));
        }
        invoice.TotalCost = totalCost; // بالعملة الأساسية
        invoice.GrossProfit = isSales ? (invoice.IsReturn ? -(baseNet - totalCost) : baseNet - totalCost) : 0;

        // الجرد الدوري: الأصناف تُقيَّم في بضاعة آخر المدة، فحساب مخزون يدوي على مستند أصناف يُثبتها مرتين (السيارات خارجه)
        if (!string.IsNullOrWhiteSpace(invoice.InventoryAccountCode) && invoice is not Invoice { VehicleLines.Count: > 0 }
            && invoice.Lines.Any(i => !InvoiceLineStock.IsNonStock(i)) && (await PostingAccounts.SettingsAsync(_db, ct)).IsPeriodic(invoice.IssueDate))
            throw new ValidationFailedException(Messages.PeriodicManualInventoryAccount);

        // مشتريات: البنود غير المخزنية (خدمات/مصروفات) لا تدخل حساب المخزون، إلا إن حدّد المستند حساب مخزون صراحةً
        // (شراء السيارات: بنودها وصفية وهي مخزون فعلاً).
        var expenseAmount = isSales || !string.IsNullOrWhiteSpace(invoice.InventoryAccountCode) ? 0
            : Math.Min(baseNet, DocumentPricing.Round(invoice.Lines.Where(InvoiceLineStock.IsNonStock).Sum(i => i.TotalBeforeVat) * fx));
        if (expenseAmount > 0) await DefaultAccounts.EnsureAsync(_db, ct, DefaultAccounts.PurchasedServices);
        // المخزون بتغيّر قيمته الدفترية لا بسعر المستند: مرتجع المشتريات يخرج بتكلفته الحالية، والشراء بالمعيارية يدخل بها،
        // وما زاد أو نقص عن صافي المستند فرق سعر. البنود غير المخزنية على حساب مخزون صريح تدخله بصافيها.
        decimal? inventoryAmount = null;
        string? varianceAccount = null;
        var hasVehicleLines = invoice is Invoice { VehicleLines.Count: > 0 };
        if (!isSales && !hasVehicleLines && invoice.Lines.Any(i => !InvoiceLineStock.IsNonStock(i)))
        {
            var nonStockNet = string.IsNullOrWhiteSpace(invoice.InventoryAccountCode) ? 0
                : DocumentPricing.Round(invoice.Lines.Where(InvoiceLineStock.IsNonStock).Sum(i => i.TotalBeforeVat) * fx);
            inventoryAmount = stockCost + nonStockNet;
            if (inventoryAmount != baseNet - expenseAmount)
            {
                var policy = await _db.Set<CostingPolicy>().AsNoTracking().FirstOrDefaultAsync(ct);
                // حساب الانحرافات المحدد في السياسة: للمعيارية، ولإيقاف تحديث التكلفة عند الشراء (الفرق كله فرق سعر)
                varianceAccount = policy is { Method: CostingMethod.Standard } or { RecalculateOnNewPurchase: false }
                        && !string.IsNullOrWhiteSpace(policy.StandardCostVarianceAccountCode)
                    ? policy.StandardCostVarianceAccountCode.Trim() : DefaultAccounts.PurchasePriceVariance;
                await DefaultAccounts.EnsureAsync(_db, ct, varianceAccount);
            }
        }

        // القيد المحاسبي عبر المحرك المركزي
        var partyAccount = await PartyAccountAsync(invoice, isSales, ct);
        var description = $"{DescribeKind(invoice.Kind)} {invoice.InvoiceNumber} - {invoice.PartyName}";
        PostingResult posted;
        if (isSales)
            posted = await _posting.PostSaleAsync(new SalePostingRequest
            {
                Module = invoice.Module, Date = invoice.IssueDate, Description = description, SourceType = SourceTypeFor(invoice.Kind), SourceId = invoice.Id, ReplaceEntryId = invoice.JournalEntryId,
                SourceNumber = invoice.InvoiceNumber, IsReturn = invoice.IsReturn, PartyAccountCode = partyAccount,
                NetAmount = baseNet, ExciseTaxAmount = DocumentPricing.Round(invoice.ExciseTaxTotal * fx),
                VatAmount = baseVat, CostAmount = totalCost, Payments = payments,
                RevenueAccountCode = invoice.RevenueAccountCode, InventoryAccountCode = invoice.InventoryAccountCode, CogsAccountCode = invoice.CogsAccountCode, WarehouseId = invoice.WarehouseId,
                RevenueLines = RevenueByLine(invoice, fx, baseNet),
                CostLines = costByCenter.ToAmounts(),
                CostCenterByAccount = JournalCostCenterMap.Map(invoice.JournalCostCentersJson),
            }, ct);
        else
            posted = await _posting.PostPurchaseAsync(new PurchasePostingRequest
            {
                Module = invoice.Module, Date = invoice.IssueDate, Description = description, SourceType = SourceTypeFor(invoice.Kind), SourceId = invoice.Id, ReplaceEntryId = invoice.JournalEntryId,
                SourceNumber = invoice.InvoiceNumber, IsReturn = invoice.IsReturn, PartyAccountCode = partyAccount,
                NetAmount = baseNet, VatAmount = baseVat, Payments = payments,
                ExpenseAmount = expenseAmount, InventoryAmount = inventoryAmount, VarianceAccountCode = varianceAccount,
                // «غير مستلمة» ومرتجعها: الأصناف على «بضاعة مشتراة لم تُستلم» لا على مخزون الموقع (ولا المشتريات في الجرد الدوري)
                InventoryAccountCode = goodsNotReceived ? DefaultAccounts.GoodsNotReceived : invoice.InventoryAccountCode, WarehouseId = invoice.WarehouseId,
                CostCenterShares = PurchaseShares(invoice, fx, baseNet, expenseAmount, inventoryAmount, stockByCenter),
                CostCenterByAccount = JournalCostCenterMap.Map(invoice.JournalCostCentersJson),
            }, ct);
        invoice.JournalEntryId = posted.JournalEntryId;

        if (isSales && !preview)
            invoice.ZatcaQrCode = _zatca.GenerateTlvQr(tenant.NameAr, tenant.VatNumber,
                invoice.IssueDate.Date.Add(TimeSpan.TryParse(invoice.IssueTime, out var t) ? t : TimeSpan.Zero), invoice.GrandTotal, invoice.VatTotal);

        invoice.Status = "posted";
        await _db.SaveChangesAsync(ct);
        if (!preview)
        {
            // فاتورة مؤجر آلية مستأجرة (أو مرتجعه): تسوية استحقاق إيجارها بقيد مستقل؛ قيد الفاتورة الضريبي يبقى كما هو
            if (!isSales) await _rentals.OnPurchaseDocumentChangedAsync(invoice, ct);
            await _audit.LogAsync("INVOICE_POSTED", invoice.GetType().Name, invoice.Id.ToString(),
                $"ترحيل {DescribeKind(invoice.Kind)} {invoice.InvoiceNumber} بإجمالي {invoice.GrandTotal:0.00} {invoice.CurrencyCode} - {invoice.PartyName}", ct);
        }
    }

    private async Task<List<PaymentPosting>> ResolvePaymentsAsync(InvoiceBase invoice, CancellationToken ct)
    {
        var methods = await _db.Set<PaymentMethodItem>().AsNoTracking().ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(invoice.SettlementAccountCode)) // تحصيل عبر بوابة الدفع: كل الجزء غير الآجل لحساب التسوية
        {
            var paid = invoice.PaymentSplits.Count > 0 ? invoice.PaymentSplits.Where(s => s.Method != PaymentMethod.Credit).Sum(s => s.Amount)
                : invoice.PaymentMethod == PaymentMethod.Credit ? 0 : invoice.GrandTotal;
            return paid > 0 ? new() { new PaymentPosting(invoice.SettlementAccountCode, paid) } : new();
        }
        // الدفع بعربون سيارة يقفل حساب عربونات العملاء المحدَّد في إعدادات المعرض
        var byDeposit = invoice.PaymentMethod == PaymentMethod.CustomerDeposit || invoice.PaymentSplits.Any(s => s.Method == PaymentMethod.CustomerDeposit);
        var deposits = byDeposit ? (await ERP.Service.Services.CarShowroom.SalesCharges.SettingsAsync(_db, ct)).CustomerDepositsAccount : null;
        // صندوق الموقع للنقد (الموقع الرئيسي بلا صندوق خاص = الصندوق العام)
        var siteCash = (await PostingAccounts.SiteAsync(_db, invoice.WarehouseId, ct)).Cash;
        if (invoice.PaymentSplits.Count > 0)
            return invoice.PaymentSplits.Where(s => s.Method != PaymentMethod.Credit)
                .Select(s => new PaymentPosting(TreasuryResolver.Resolve(s.Method, methods, deposits, s.PaymentMethodCode, siteCash), s.Amount)).ToList();
        if (invoice.PaymentMethod == PaymentMethod.Credit || invoice.GrandTotal <= 0) return new();
        return new() { new PaymentPosting(TreasuryResolver.Resolve(invoice.PaymentMethod, methods, deposits, invoice.PaymentMethodCode, siteCash), invoice.GrandTotal) };
    }

    /// <summary>صافي كل سطر بالعملة الأساسية؛ كسر التقريب على آخر سطر ليطابق صافي المستند.</summary>
    private static List<(InvoiceItemBase Line, decimal Net)> LineNets(InvoiceBase invoice, decimal fx, decimal baseNet)
    {
        var lines = invoice.Lines.Select(i => (Line: i, Net: DocumentPricing.Round(i.TotalBeforeVat * fx))).ToList();
        if (lines.Count > 0) lines[^1] = (lines[^1].Line, baseNet - lines.Take(lines.Count - 1).Sum(l => l.Net));
        return lines;
    }

    /// <summary>إيراد كل سطر على حسابه ومركز تكلفته بالعملة الأساسية.</summary>
    private static List<RevenuePosting> RevenueByLine(InvoiceBase invoice, decimal fx, decimal baseNet)
        => LineNets(invoice, fx, baseNet).Select(l => new RevenuePosting(l.Line.RevenueAccountCode, l.Line.CostCenterId, l.Net, SiteOf(invoice, l.Line))).ToList();

    /// <summary>
    /// حصص مراكز التكلفة في قيد المشتريات: صافي كل مركز من أسطره، وخدماته بنسبة صافي بنود خدماته، ومخزونه تغيّر قيمة أصنافه
    /// الفعلي (ومعه بنوده غير المخزنية حين يُحدَّد حساب مخزون صريح). مجاميع الحصص = مبالغ المستند تماماً، وفرق السعر لكل مركز ما يبقى.
    /// </summary>
    private static List<PurchaseCostCenterShare> PurchaseShares(InvoiceBase invoice, decimal fx, decimal baseNet, decimal expenseAmount,
        decimal? inventoryAmount, CostCenterTotals stockByCenter)
    {
        var net = new CostCenterTotals();
        var nonStock = new CostCenterTotals();
        foreach (var (line, amount) in LineNets(invoice, fx, baseNet))
        {
            net.Add(line.CostCenterId, amount, SiteOf(invoice, line));
            if (InvoiceLineStock.IsNonStock(line)) nonStock.Add(line.CostCenterId, amount, SiteOf(invoice, line));
        }
        var expense = CostCenterTotals.Apportion(expenseAmount, nonStock);
        CostCenterTotals? inventory = null;
        if (inventoryAmount is { } total)
        {
            inventory = stockByCenter.Copy();
            var nonStockInventory = CostCenterTotals.Apportion(total - stockByCenter.Total, nonStock);
            foreach (var slice in nonStockInventory.Slices) inventory.Add(slice.Center, nonStockInventory[slice], slice.Site);
        }
        return net.Slices.Union(expense.Slices).Union(inventory?.Slices ?? Enumerable.Empty<(Guid? Center, Guid? Site)>())
            .Select(s => new PurchaseCostCenterShare(s.Center, net[s], expense[s], inventory?[s], s.Site)).ToList();
    }

    /// <summary>يحوّل المدفوع إلى العملة الأساسية؛ السداد الكامل يطابق إجمالي المستند المحوَّل بلا كسر تقريب.</summary>
    private static List<PaymentPosting> ToBase(List<PaymentPosting> payments, decimal fx, decimal documentTotal, decimal baseTotal)
    {
        if (fx == 1 || payments.Count == 0) return payments;
        var converted = payments.Select(p => p with { Amount = DocumentPricing.Round(p.Amount * fx) }).ToList();
        if (payments.Sum(p => p.Amount) == documentTotal)
            converted[^1] = converted[^1] with { Amount = baseTotal - converted.Take(converted.Count - 1).Sum(p => p.Amount) };
        return converted;
    }

    private async Task<string?> PartyAccountAsync(InvoiceBase invoice, bool isSales, CancellationToken ct)
    {
        if (!invoice.PartyId.HasValue) return null;
        return isSales
            ? await _db.Set<Customer>().AsNoTracking().Where(c => c.Id == invoice.PartyId).Select(c => c.AccountCode).FirstOrDefaultAsync(ct)
            : await _db.Set<Supplier>().AsNoTracking().Where(s => s.Id == invoice.PartyId).Select(s => s.AccountCode).FirstOrDefaultAsync(ct);
    }

    /// <summary>بادئة الرقم المؤقت للمسودة قبل صرف رقمها المتسلسل عند الترحيل.</summary>
    private const string DraftPrefix = "DRAFT-";
    private const string DefaultCashCustomer = "عميل نقدي";

    /// <summary>
    /// فاتورة (تجارية أو معارض) من شاشة المستخدم ونوعها مضبوط يدوياً. نقاط البيع ومستندات النظام
    /// (التحويل، العقود، البيع والشراء السريع) تبقى تلقائية.
    /// </summary>
    private async Task<bool> IsManualAsync(InvoiceBase invoice, bool systemGenerated, CancellationToken ct)
        => !systemGenerated && invoice.ReferenceType != "pos_transaction"
            && (await _numbers.CounterAsync(KeyFor(invoice.Kind), PrefixFor(invoice.Kind), invoice.WarehouseId, invoice.IssueDate, ct)).Manual;

    /// <summary>الرقم اليدوي بعد التحقق (إلزامي، صيغة، طول، تكرار في كل جداول الفواتير)؛ null = الترقيم التلقائي.</summary>
    private async Task<string?> ManualNumberAsync(InvoiceBase invoice, string? requested, bool systemGenerated, CancellationToken ct)
    {
        if (!await IsManualAsync(invoice, systemGenerated, ct)) return null;
        var id = invoice.Id;
        return await _numbers.AssignAsync(new DocumentNumberRequest
        {
            Key = KeyFor(invoice.Kind), DefaultPrefix = PrefixFor(invoice.Kind), ManualNumber = requested, UserEntered = true,
            Exists = (n, c) => _db.Set<InvoiceBase>().AnyAsync(i => i.InvoiceNumber == n && i.Id != id, c),
        }, ct);
    }

    private static string KeyFor(InvoiceKind k) => k switch
    {
        InvoiceKind.Sales => "sales_invoice", InvoiceKind.Purchase => "purchase_invoice",
        InvoiceKind.SalesReturn => "sales_return", _ => "purchase_return",
    };

    private static string PrefixFor(InvoiceKind k) => k switch
    {
        InvoiceKind.Sales => "SINV-", InvoiceKind.Purchase => "PINV-", InvoiceKind.SalesReturn => "SRET-", _ => "PRET-",
    };

    private static string SourceTypeFor(InvoiceKind k) => k switch
    {
        InvoiceKind.Sales => "sales", InvoiceKind.Purchase => "purchase", InvoiceKind.SalesReturn => "sales_return", _ => "purchase_return",
    };

    private static string DescribeKind(InvoiceKind k) => k switch
    {
        InvoiceKind.Sales => "فاتورة مبيعات", InvoiceKind.Purchase => "فاتورة مشتريات",
        InvoiceKind.SalesReturn => "مرتجع مبيعات", _ => "مرتجع مشتريات",
    };
}
