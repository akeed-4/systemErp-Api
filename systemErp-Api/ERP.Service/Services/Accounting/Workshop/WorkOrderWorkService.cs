using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using A = ERP.Core.Models.Accounting.LineApprovalStatuses;
using ESt = ERP.Core.Models.Accounting.EstimateStatuses;
using K = ERP.Core.Models.Accounting.WorkOrderLineKinds;
using St = ERP.Core.Models.Accounting.WorkOrderStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// العمل على أمر العمل (W2). القواعد:
/// <list type="bullet">
/// <item>الفحص من قالب نشط بلقطة بنوده؛ المسودة تُعبَّأ (الحالة لبنود الحالة، والرقم والنص لغيرها)، والإكمال يتطلب إجابة كل بنود الحالة وصورة
/// لكل فاشل يشترطها، وفحص الجودة يتطلب صلاحية «الجودة». المكتمل لا يُعدَّل ولا يُحذف.</item>
/// <item>السطر بصنف: العمالة من خدمة معرّفة (صنفها وسعرها)، والقطعة صنف مخزني بسعر بيعه، والخارجي والرسوم صنف خدمي. السعر المخالف
/// والخصم يتطلبان «خصم يدوي». الضريبة نسبة الصنف. الموافق عليه مقفل؛ والمعروض في تقدير مُرسل لا يُعدَّل حتى الرد.</item>
/// <item>التقدير نسخة مجمّدة للأسطر التي تنتظر القرار (معلّقة أو مؤجلة) وتستبدل السابقة غير المجابة. الإرسال من «مستلم» أو «فحص» ينقل
/// الأمر «بانتظار الموافقة». الرد (بصلاحية الاعتماد) بقناته ومن رد وقرار لكل سطر، ومع موافقة سطر واحد ينتقل الأمر المنتظر «معتمداً».</item>
/// <item>تنفيذ سطر غير موافق يتطلب استثناءً موثقاً (سبب، ومرفق اختياري من مرفقات الأمر) بالشاشة المقيدة.</item>
/// </list>
/// </summary>
public class WorkOrderWorkService : IWorkOrderWorkService
{
    private const string Screen = PermissionCatalog.WorkshopOrdersScreenId;
    private const int MaxLines = 300;
    private static readonly string[] AfterApproval = { St.Approved, St.InProgress, St.Paused, St.Quality, St.Ready };
    private static readonly string[] ExecutionStatuses = { St.Approved, St.InProgress, St.Paused, St.Quality };

    private readonly ErpDbContext _db;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;
    private readonly IAuditService _audit;

    public WorkOrderWorkService(ErpDbContext db, ITransactionRunner tx, ICurrentUser user, IPermissionService permissions, IAuditService audit)
    {
        _db = db; _tx = tx; _user = user; _permissions = permissions; _audit = audit;
    }

    private string? By => _user.Name ?? _user.Email;

    // ---------- العرض ----------

    public async Task<WorkOrderWorkDto> GetAsync(Guid workOrderId, CancellationToken ct = default)
    {
        var o = await _db.Set<WorkOrder>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == workOrderId, ct)
            ?? throw new NotFoundException(Messages.WorkOrderNotFound);
        var canViewCost = await _permissions.HasPermissionAsync(PermissionCatalog.WorkshopCostViewScreenId, ScreenAction.View, ct);
        var lines = await _db.Set<WorkOrderLine>().AsNoTracking().Where(l => l.WorkOrderId == workOrderId).OrderBy(l => l.SortOrder).ToListAsync(ct);
        var exceptions = await _db.Set<WorkOrderExecutionException>().AsNoTracking().Where(x => x.WorkOrderId == workOrderId)
            .OrderBy(x => x.AuthorizedAt).ToListAsync(ct);
        var estimates = await _db.Set<WorkOrderEstimate>().AsNoTracking().Include(e => e.Lines).Where(e => e.WorkOrderId == workOrderId)
            .OrderByDescending(e => e.Version).ToListAsync(ct);
        var inspections = await InspectionDtosAsync(await _db.Set<WorkOrderInspection>().AsNoTracking().Include(i => i.Results)
            .Where(i => i.WorkOrderId == workOrderId).OrderBy(i => i.CreatedAt).ToListAsync(ct), lines, ct);

        var serviceIds = lines.Where(l => l.ServiceId != null).Select(l => l.ServiceId!.Value).Distinct().ToList();
        var services = await _db.Set<WorkshopServiceDefinition>().AsNoTracking().Where(s => serviceIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Code, ct);
        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await _db.Set<Product>().AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Sku, ct);
        var excepted = exceptions.Select(x => x.LineId).ToHashSet();
        var versions = estimates.ToDictionary(e => e.Id, e => e.Version);

        var lineDtos = lines.Select(l =>
        {
            var (net, vat) = WorkOrderRules.Amounts(l.Quantity, l.UnitPrice, l.DiscountAmount, l.VatRate);
            return new WorkOrderLineDto
            {
                Id = l.Id, Kind = l.Kind, ServiceId = l.ServiceId, ServiceCode = l.ServiceId is Guid s ? services.GetValueOrDefault(s) : null, ProductId = l.ProductId,
                ProductSku = products.GetValueOrDefault(l.ProductId), Description = l.Description, Quantity = l.Quantity, UnitPrice = l.UnitPrice,
                PriceOverridden = l.PriceOverridden, DiscountAmount = l.DiscountAmount, VatRate = l.VatRate, NetAmount = net, VatAmount = vat, TotalAmount = net + vat,
                ApprovalStatus = l.ApprovalStatus, ApprovedEstimateId = l.ApprovedEstimateId,
                ApprovedVersion = l.ApprovedEstimateId is Guid e && versions.TryGetValue(e, out var v) ? v : null, BillingType = l.BillingType,
                ExecutionStatus = l.ExecutionStatus, EstimatedCost = canViewCost ? l.EstimatedCost : null, AddedAfterApproval = l.AddedAfterApproval,
                SourceResultId = l.SourceResultId, HasException = excepted.Contains(l.Id),
                CanExecute = l.ApprovalStatus == A.Approved || excepted.Contains(l.Id), Notes = l.Notes, SortOrder = l.SortOrder,
            };
        }).ToList();
        var customer = lineDtos.Where(l => l.BillingType == LineBillingTypes.Customer).ToList();
        return new WorkOrderWorkDto
        {
            WorkOrderId = o.Id, Status = o.Status, CanViewCost = canViewCost, Lines = lineDtos, Inspections = inspections,
            Totals = new WorkOrderTotalsDto
            {
                ApprovedNet = customer.Where(l => l.ApprovalStatus == A.Approved).Sum(l => l.NetAmount),
                ApprovedVat = customer.Where(l => l.ApprovalStatus == A.Approved).Sum(l => l.VatAmount),
                ApprovedTotal = customer.Where(l => l.ApprovalStatus == A.Approved).Sum(l => l.TotalAmount),
                PendingTotal = customer.Where(l => l.ApprovalStatus is A.Pending or A.Deferred).Sum(l => l.TotalAmount),
                PendingCount = lineDtos.Count(l => l.ApprovalStatus is A.Pending or A.Deferred),
                ApprovedCount = lineDtos.Count(l => l.ApprovalStatus == A.Approved), RejectedCount = lineDtos.Count(l => l.ApprovalStatus == A.Rejected),
            },
            Estimates = estimates.Select(e => EstimateDto(e, o.OrderNumber)).ToList(),
            Exceptions = exceptions.Select(x => new ExecutionExceptionDto
            {
                Id = x.Id, LineId = x.LineId, LineDescription = lines.FirstOrDefault(l => l.Id == x.LineId)?.Description, Reason = x.Reason,
                AttachmentId = x.AttachmentId, AuthorizedBy = x.AuthorizedBy, AuthorizedAt = x.AuthorizedAt,
            }).ToList(),
        };
    }

    // ---------- الفحص ----------

    public Task<WorkOrderInspectionDto> CreateInspectionAsync(Guid workOrderId, CreateInspectionDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var o = await EditableOrderAsync(workOrderId, token);
            var t = await _db.Set<InspectionTemplate>().AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == request.TemplateId && x.IsActive, token)
                ?? throw new ValidationFailedException(Messages.InspectionTemplateNotFound);
            var i = new WorkOrderInspection
            {
                WorkOrderId = o.Id, TemplateId = t.Id, Usage = t.Usage, TemplateName = t.NameAr, Status = InspectionStatuses.Draft,
                InspectorName = Trim(request.InspectorName, 200) ?? By, InspectorUserId = _user.UserId, CreatedBy = By,
            };
            foreach (var item in t.Items.OrderBy(x => x.SortOrder))
                i.Results.Add(new WorkOrderInspectionResult
                {
                    TemplateItemId = item.Id, Section = item.Section, TextAr = item.TextAr, TextEn = item.TextEn, ResultType = item.ResultType,
                    PhotoRequiredOnFail = item.PhotoRequiredOnFail, SortOrder = item.SortOrder,
                });
            _db.Add(i);
            await _db.SaveChangesAsync(token);
            await LogAsync("WORK_ORDER_INSPECTION_CREATED", o, $"{t.Usage} • {t.NameAr}", token);
            return await InspectionAsync(i.Id, token);
        }, ct);

    public async Task<WorkOrderInspectionDto> SaveInspectionAsync(Guid workOrderId, Guid inspectionId, SaveInspectionResultsDto request, CancellationToken ct = default)
    {
        await EditableOrderAsync(workOrderId, ct);
        var i = await DraftInspectionAsync(workOrderId, inspectionId, ct);
        foreach (var r in request.Results)
        {
            var result = i.Results.FirstOrDefault(x => x.Id == r.ResultId) ?? throw new ValidationFailedException(Messages.InspectionResultNotFound);
            switch (result.ResultType)
            {
                case InspectionResultTypes.Condition:
                    var c = string.IsNullOrWhiteSpace(r.Condition) ? null : r.Condition.Trim().ToLowerInvariant();
                    if (c != null && !InspectionConditions.IsValid(c)) throw new ValidationFailedException(Messages.InspectionConditionInvalid);
                    result.Condition = c;
                    break;
                case InspectionResultTypes.Number:
                    result.NumberValue = r.NumberValue is decimal n ? decimal.Round(n, 4) : null;
                    break;
                default:
                    result.TextValue = Trim(r.TextValue, 500);
                    break;
            }
            result.Note = Trim(r.Note, 500);
        }
        i.InspectorName = Trim(request.InspectorName, 200) ?? i.InspectorName;
        i.Summary = Trim(request.Summary, 2000);
        await _db.SaveChangesAsync(ct);
        return await InspectionAsync(i.Id, ct);
    }

    public async Task<WorkOrderInspectionDto> CompleteInspectionAsync(Guid workOrderId, Guid inspectionId, CancellationToken ct = default)
    {
        var o = await EditableOrderAsync(workOrderId, ct);
        var i = await DraftInspectionAsync(workOrderId, inspectionId, ct);
        if (i.Usage == InspectionUsages.Quality) await _permissions.DemandAsync(PermissionCatalog.WorkshopQualityScreenId, ScreenAction.Approve, ct: ct);
        var unanswered = i.Results.Where(r => r.ResultType == InspectionResultTypes.Condition && r.Condition == null).Select(r => r.TextAr).ToList();
        if (unanswered.Count > 0) throw new ValidationFailedException(string.Format(Messages.InspectionUnanswered, string.Join("، ", unanswered.Take(5))));
        var needPhoto = i.Results.Where(r => r.PhotoRequiredOnFail && r.Condition == InspectionConditions.Fail).ToList();
        if (needPhoto.Count > 0)
        {
            var ids = needPhoto.Select(r => r.Id).ToList();
            var withPhoto = await _db.Set<DocumentAttachment>().AsNoTracking()
                .Where(a => a.OwnerType == WorkshopAttachmentOwnerTypes.InspectionResult && ids.Contains(a.OwnerId)).Select(a => a.OwnerId).Distinct().ToListAsync(ct);
            var missing = needPhoto.Where(r => !withPhoto.Contains(r.Id)).Select(r => r.TextAr).ToList();
            if (missing.Count > 0) throw new ValidationFailedException(string.Format(Messages.InspectionPhotoRequired, string.Join("، ", missing.Take(5))));
        }
        i.Status = InspectionStatuses.Completed; i.CompletedAt = SaudiTime.Now;
        await _db.SaveChangesAsync(ct);
        await LogAsync("WORK_ORDER_INSPECTION_COMPLETED", o, $"{i.Usage} • {i.TemplateName}", ct);
        return await InspectionAsync(i.Id, ct);
    }

    public Task DeleteInspectionAsync(Guid workOrderId, Guid inspectionId, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var o = await EditableOrderAsync(workOrderId, token);
            var i = await DraftInspectionAsync(workOrderId, inspectionId, token);
            var resultIds = i.Results.Select(r => r.Id).ToList();
            if (await _db.Set<WorkOrderLine>().AnyAsync(l => l.SourceResultId != null && resultIds.Contains(l.SourceResultId.Value), token))
                throw new ConflictException(Messages.InspectionHasLines);
            await DocumentAttachmentService.RemoveForOwnersAsync(_db, WorkshopAttachmentOwnerTypes.InspectionResult, resultIds, token);
            _db.Remove(i);
            await _db.SaveChangesAsync(token);
            await LogAsync("WORK_ORDER_INSPECTION_DELETED", o, i.TemplateName, token);
        }, ct);

    private async Task<WorkOrderInspection> DraftInspectionAsync(Guid workOrderId, Guid inspectionId, CancellationToken ct)
    {
        var i = await _db.Set<WorkOrderInspection>().Include(x => x.Results).FirstOrDefaultAsync(x => x.Id == inspectionId && x.WorkOrderId == workOrderId, ct)
            ?? throw new NotFoundException(Messages.InspectionNotFound);
        if (i.Status != InspectionStatuses.Draft) throw new ConflictException(Messages.InspectionCompleted);
        return i;
    }

    private async Task<WorkOrderInspectionDto> InspectionAsync(Guid id, CancellationToken ct)
    {
        var i = await _db.Set<WorkOrderInspection>().AsNoTracking().Include(x => x.Results).FirstAsync(x => x.Id == id, ct);
        var lines = await _db.Set<WorkOrderLine>().AsNoTracking().Where(l => l.WorkOrderId == i.WorkOrderId).ToListAsync(ct);
        return (await InspectionDtosAsync(new List<WorkOrderInspection> { i }, lines, ct)).Single();
    }

    private async Task<List<WorkOrderInspectionDto>> InspectionDtosAsync(List<WorkOrderInspection> list, List<WorkOrderLine> lines, CancellationToken ct)
    {
        var resultIds = list.SelectMany(i => i.Results.Select(r => r.Id)).ToList();
        var photos = resultIds.Count == 0 ? new Dictionary<Guid, int>() : await _db.Set<DocumentAttachment>().AsNoTracking()
            .Where(a => a.OwnerType == WorkshopAttachmentOwnerTypes.InspectionResult && resultIds.Contains(a.OwnerId))
            .GroupBy(a => a.OwnerId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var suggested = lines.Where(l => l.SourceResultId != null).GroupBy(l => l.SourceResultId!.Value).ToDictionary(g => g.Key, g => g.First().Id);
        return list.Select(i => new WorkOrderInspectionDto
        {
            Id = i.Id, WorkOrderId = i.WorkOrderId, TemplateId = i.TemplateId, Usage = i.Usage, TemplateName = i.TemplateName, Status = i.Status,
            InspectorName = i.InspectorName, CompletedAt = i.CompletedAt, Summary = i.Summary, CreatedBy = i.CreatedBy, CreatedAt = i.CreatedAt,
            FailCount = i.Results.Count(r => r.Condition == InspectionConditions.Fail),
            AttentionCount = i.Results.Count(r => r.Condition == InspectionConditions.Attention),
            Results = i.Results.OrderBy(r => r.SortOrder).Select(r => new InspectionResultDto
            {
                Id = r.Id, Section = r.Section, TextAr = r.TextAr, TextEn = r.TextEn, ResultType = r.ResultType, PhotoRequiredOnFail = r.PhotoRequiredOnFail,
                Condition = r.Condition, NumberValue = r.NumberValue, TextValue = r.TextValue, Note = r.Note, SortOrder = r.SortOrder,
                PhotoCount = photos.GetValueOrDefault(r.Id), SuggestedLineId = suggested.TryGetValue(r.Id, out var lid) ? lid : null,
            }).ToList(),
        }).ToList();
    }

    // ---------- الأسطر ----------

    public Task<WorkOrderWorkDto> AddLineAsync(Guid workOrderId, SaveWorkOrderLineDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var o = await EditableOrderAsync(workOrderId, token);
            var count = await _db.Set<WorkOrderLine>().CountAsync(l => l.WorkOrderId == o.Id, token);
            if (count >= MaxLines) throw new ValidationFailedException(string.Format(Messages.WorkOrderTooManyLines, MaxLines));
            var line = new WorkOrderLine
            {
                WorkOrderId = o.Id, ApprovalStatus = A.Pending, AddedAfterApproval = AfterApproval.Contains(o.Status), CreatedBy = By,
                SortOrder = count == 0 ? 1 : await _db.Set<WorkOrderLine>().Where(l => l.WorkOrderId == o.Id).MaxAsync(l => l.SortOrder, token) + 1,
            };
            await ApplyLineAsync(o, line, request, token);
            _db.Add(line);
            await _db.SaveChangesAsync(token);
            await LogAsync("WORK_ORDER_LINE_ADDED", o, $"{line.Kind} • {line.Description}", token);
            return await GetAsync(o.Id, token);
        }, ct);

    public Task<WorkOrderWorkDto> UpdateLineAsync(Guid workOrderId, Guid lineId, SaveWorkOrderLineDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var o = await EditableOrderAsync(workOrderId, token);
            var line = await ChangeableLineAsync(o.Id, lineId, token);
            await ApplyLineAsync(o, line, request, token);
            // التعديل يعيد عرض السطر للقرار
            line.ApprovalStatus = A.Pending; line.DecidedAt = null;
            await _db.SaveChangesAsync(token);
            await LogAsync("WORK_ORDER_LINE_UPDATED", o, line.Description, token);
            return await GetAsync(o.Id, token);
        }, ct);

    public Task<WorkOrderWorkDto> DeleteLineAsync(Guid workOrderId, Guid lineId, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var o = await EditableOrderAsync(workOrderId, token);
            var line = await ChangeableLineAsync(o.Id, lineId, token);
            if (await _db.Set<WorkOrderEstimateLine>().AnyAsync(l => l.LineId == lineId && l.Estimate.Status != ESt.Draft, token))
                throw new ConflictException(Messages.WorkOrderLineInEstimate);
            _db.RemoveRange(await _db.Set<WorkOrderEstimateLine>().Where(l => l.LineId == lineId).ToListAsync(token));
            _db.Remove(line);
            await _db.SaveChangesAsync(token);
            await LogAsync("WORK_ORDER_LINE_DELETED", o, line.Description, token);
            return await GetAsync(o.Id, token);
        }, ct);

    public async Task<WorkOrderWorkDto> ChangeExecutionAsync(Guid workOrderId, Guid lineId, ChangeLineExecutionDto request, CancellationToken ct = default)
    {
        var o = await OrderAsync(workOrderId, ct);
        if (!ExecutionStatuses.Contains(o.Status)) throw new ConflictException(Messages.WorkOrderNotInExecution);
        var line = await _db.Set<WorkOrderLine>().FirstOrDefaultAsync(l => l.Id == lineId && l.WorkOrderId == o.Id, ct)
            ?? throw new NotFoundException(Messages.WorkOrderLineNotFound);
        var status = request.ExecutionStatus?.Trim().ToLowerInvariant();
        if (!LineExecutionStatuses.IsValid(status)) throw new ValidationFailedException(Messages.WorkOrderExecutionStatusInvalid);
        if (status != LineExecutionStatuses.NotStarted) await WorkOrderRules.EnsureExecutableAsync(_db, o.Id, line.Id, ct);
        line.ExecutionStatus = status!;
        await _db.SaveChangesAsync(ct);
        await LogAsync("WORK_ORDER_LINE_EXECUTION", o, $"{line.Description} • {status}", ct);
        return await GetAsync(o.Id, ct);
    }

    /// <summary>سطر يُعدَّل أو يُحذف: غير موافق عليه، ولا معروض في تقدير مُرسل، ولا عليه استثناء.</summary>
    private async Task<WorkOrderLine> ChangeableLineAsync(Guid workOrderId, Guid lineId, CancellationToken ct)
    {
        var line = await _db.Set<WorkOrderLine>().FirstOrDefaultAsync(l => l.Id == lineId && l.WorkOrderId == workOrderId, ct)
            ?? throw new NotFoundException(Messages.WorkOrderLineNotFound);
        if (line.ApprovalStatus == A.Approved) throw new ConflictException(Messages.WorkOrderLineApprovedLocked);
        if (await _db.Set<WorkOrderEstimateLine>().AnyAsync(l => l.LineId == lineId && l.Estimate.Status == ESt.Sent, ct))
            throw new ConflictException(Messages.WorkOrderLineInSentEstimate);
        if (await _db.Set<WorkOrderExecutionException>().AnyAsync(x => x.LineId == lineId, ct)) throw new ConflictException(Messages.WorkOrderLineApprovedLocked);
        if (await _db.Set<WorkOrderPartReservation>().AnyAsync(x => x.LineId == lineId, ct) || await _db.Set<WorkOrderPartIssue>().AnyAsync(x => x.LineId == lineId, ct)
            || await _db.Set<WorkOrderTimeLog>().AnyAsync(x => x.LineId == lineId, ct) || await _db.Set<WorkOrderExternalService>().AnyAsync(x => x.LineId == lineId, ct))
            throw new ConflictException(Messages.WorkOrderLineHasExecution);
        return line;
    }

    private async Task ApplyLineAsync(WorkOrder o, WorkOrderLine line, SaveWorkOrderLineDto r, CancellationToken ct)
    {
        var kind = r.Kind?.Trim().ToLowerInvariant();
        if (!WorkOrderLineKinds.IsValid(kind)) throw new ValidationFailedException(Messages.WorkOrderLineKindInvalid);
        if (r.Quantity <= 0) throw new ValidationFailedException(Messages.WorkOrderLineQuantityInvalid);
        if (r.UnitPrice is < 0 || r.DiscountAmount < 0 || r.EstimatedCost is < 0) throw new ValidationFailedException(Messages.WorkshopSettingsNegative);
        var billing = string.IsNullOrWhiteSpace(r.BillingType) ? LineBillingTypes.Customer : r.BillingType.Trim().ToLowerInvariant();
        if (!LineBillingTypes.IsValid(billing)) throw new ValidationFailedException(Messages.WorkOrderLineBillingInvalid);

        Guid productId;
        Guid? serviceId = null;
        string? defaultDescription;
        decimal defaultPrice, defaultUnitCost = 0;
        if (kind == K.Labor)
        {
            var s = await _db.Set<WorkshopServiceDefinition>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.ServiceId && x.IsActive, ct)
                ?? throw new ValidationFailedException(Messages.WorkshopServiceNotFound);
            var labor = (await _db.Set<WorkshopSettings>().AsNoTracking().FirstOrDefaultAsync(ct))?.DefaultLaborRate ?? 0;
            productId = s.ProductId; serviceId = s.Id; defaultDescription = s.NameAr; defaultPrice = WorkshopPricing.ServicePrice(s, labor);
        }
        else
        {
            var expected = kind == K.Part ? ProductItemTypes.Stock : ProductItemTypes.Service;
            var p = await _db.Set<Product>().AsNoTracking().Where(x => x.Id == r.ProductId)
                .Select(x => new { x.Id, x.ItemType, x.NameAr, x.SellingPrice, x.AverageCost }).FirstOrDefaultAsync(ct);
            if (p == null || p.ItemType != expected)
                throw new ValidationFailedException(kind == K.Part ? Messages.WorkOrderLinePartProductInvalid : Messages.WorkshopServiceProductInvalid);
            productId = p.Id; defaultDescription = kind == K.Part ? p.NameAr : null; defaultPrice = p.SellingPrice; defaultUnitCost = kind == K.Part ? p.AverageCost : 0;
        }
        var description = Trim(r.Description, 500) ?? defaultDescription ?? throw new ValidationFailedException(Messages.WorkOrderLineDescriptionRequired);
        var price = decimal.Round(r.UnitPrice ?? defaultPrice, 4);
        var overridden = r.UnitPrice is decimal manual && decimal.Round(manual, 4) != decimal.Round(defaultPrice, 4);
        var discount = decimal.Round(r.DiscountAmount, 2);
        if ((overridden || discount > 0) && !await _permissions.HasPermissionAsync(Screen, ScreenAction.Discount, ct))
            throw new ForbiddenException(Messages.WorkOrderLinePriceForbidden);
        var quantity = decimal.Round(r.Quantity, 4);
        if (discount > decimal.Round(quantity * price, 2)) throw new ValidationFailedException(Messages.WorkOrderLineDiscountTooLarge);
        if (r.SourceResultId is Guid source && !await (from res in _db.Set<WorkOrderInspectionResult>()
                                                       join i in _db.Set<WorkOrderInspection>() on res.InspectionId equals i.Id
                                                       where res.Id == source && i.WorkOrderId == o.Id
                                                       select res.Id).AnyAsync(ct))
            throw new ValidationFailedException(Messages.InspectionResultNotFound);
        var vat = await _db.Set<Product>().AsNoTracking().Where(x => x.Id == productId).Select(x => x.VatRate).FirstAsync(ct);

        line.Kind = kind!; line.ServiceId = serviceId; line.ProductId = productId; line.Description = description; line.Quantity = quantity;
        line.UnitPrice = price; line.PriceOverridden = overridden; line.DiscountAmount = discount; line.VatRate = vat; line.BillingType = billing;
        line.EstimatedCost = decimal.Round(r.EstimatedCost ?? quantity * defaultUnitCost, 2);
        line.SourceResultId = r.SourceResultId ?? line.SourceResultId; line.Notes = Trim(r.Notes, 500);
    }

    // ---------- التقديرات ----------

    public Task<WorkOrderEstimateDto> CreateEstimateAsync(Guid workOrderId, CreateEstimateDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var o = await EditableOrderAsync(workOrderId, token);
            var lines = await _db.Set<WorkOrderLine>().Where(l => l.WorkOrderId == o.Id && (l.ApprovalStatus == A.Pending || l.ApprovalStatus == A.Deferred))
                .OrderBy(l => l.SortOrder).ToListAsync(token);
            if (lines.Count == 0) throw new ValidationFailedException(Messages.EstimateNoPendingLines);
            // نسخة جديدة تستبدل غير المجابة
            foreach (var old in await _db.Set<WorkOrderEstimate>().Where(e => e.WorkOrderId == o.Id && (e.Status == ESt.Draft || e.Status == ESt.Sent)).ToListAsync(token))
                old.Status = ESt.Superseded;
            var version = (await _db.Set<WorkOrderEstimate>().Where(e => e.WorkOrderId == o.Id).Select(e => (int?)e.Version).MaxAsync(token) ?? 0) + 1;
            var e = new WorkOrderEstimate { WorkOrderId = o.Id, Version = version, Status = ESt.Draft, Notes = Trim(request.Notes, 1000), CreatedBy = By };
            foreach (var l in lines)
            {
                var (net, vat) = WorkOrderRules.Amounts(l.Quantity, l.UnitPrice, l.DiscountAmount, l.VatRate);
                e.Lines.Add(new WorkOrderEstimateLine
                {
                    LineId = l.Id, Kind = l.Kind, Description = l.Description, Quantity = l.Quantity, UnitPrice = l.UnitPrice, DiscountAmount = l.DiscountAmount,
                    VatRate = l.VatRate, NetAmount = net, VatAmount = vat, BillingType = l.BillingType, SortOrder = l.SortOrder,
                });
            }
            var charged = e.Lines.Where(x => x.BillingType == LineBillingTypes.Customer).ToList();
            e.Subtotal = charged.Sum(x => decimal.Round(x.Quantity * x.UnitPrice, 2)); e.DiscountTotal = charged.Sum(x => x.DiscountAmount);
            e.VatTotal = charged.Sum(x => x.VatAmount); e.Total = charged.Sum(x => x.NetAmount + x.VatAmount);
            _db.Add(e);
            await _db.SaveChangesAsync(token);
            await LogAsync("WORK_ORDER_ESTIMATE_CREATED", o, $"v{version} • {e.Total:0.##}", token);
            return await EstimateAsync(o, e.Id, token);
        }, ct);

    public Task<WorkOrderEstimateDto> SendEstimateAsync(Guid workOrderId, Guid estimateId, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var o = await EditableOrderAsync(workOrderId, token);
            var e = await _db.Set<WorkOrderEstimate>().FirstOrDefaultAsync(x => x.Id == estimateId && x.WorkOrderId == o.Id, token)
                ?? throw new NotFoundException(Messages.EstimateNotFound);
            if (e.Status != ESt.Draft) throw new ConflictException(Messages.EstimateNotDraft);
            if (o.Status is St.Received or St.Inspection)
            {
                await WorkOrderRules.EnsureTransitionAsync(_db, o, o.Status, St.AwaitingApproval, token);
                Move(o, St.AwaitingApproval, $"v{e.Version}");
            }
            e.Status = ESt.Sent; e.SentAt = SaudiTime.Now;
            await _db.SaveChangesAsync(token);
            await LogAsync("WORK_ORDER_ESTIMATE_SENT", o, $"v{e.Version}", token);
            return await EstimateAsync(o, e.Id, token);
        }, ct);

    public Task<WorkOrderEstimateDto> RespondEstimateAsync(Guid workOrderId, Guid estimateId, RespondEstimateDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            await _permissions.DemandAsync(Screen, ScreenAction.Approve, ct: token);
            var o = await EditableOrderAsync(workOrderId, token);
            var e = await _db.Set<WorkOrderEstimate>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == estimateId && x.WorkOrderId == o.Id, token)
                ?? throw new NotFoundException(Messages.EstimateNotFound);
            if (e.Status != ESt.Sent) throw new ConflictException(Messages.EstimateNotSent);
            var channel = r.Channel?.Trim().ToLowerInvariant();
            if (!EstimateChannels.IsValid(channel)) throw new ValidationFailedException(Messages.EstimateChannelInvalid);
            var who = Trim(r.RespondedByName, 200) ?? throw new ValidationFailedException(Messages.EstimateRespondentRequired);
            var decisions = r.Decisions.GroupBy(d => d.LineId).ToDictionary(g => g.Key, g => g.Last().Decision?.Trim().ToLowerInvariant());
            if (e.Lines.Any(l => !decisions.ContainsKey(l.LineId)) || decisions.Keys.Any(id => e.Lines.All(l => l.LineId != id)))
                throw new ValidationFailedException(Messages.EstimateDecisionsIncomplete);
            if (decisions.Values.Any(d => !A.IsDecision(d))) throw new ValidationFailedException(Messages.EstimateDecisionInvalid);
            var now = SaudiTime.Now;
            var at = r.RespondedAt is DateTime t ? (t.Kind == DateTimeKind.Utc ? SaudiTime.FromUtc(t) : DateTime.SpecifyKind(t, DateTimeKind.Unspecified)) : now;
            if (at > now.AddMinutes(15) || (e.SentAt is DateTime sent && at < sent)) throw new ValidationFailedException(Messages.EstimateResponseTimeInvalid);

            var ids = e.Lines.Select(l => l.LineId).ToList();
            var lines = await _db.Set<WorkOrderLine>().Where(l => ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id, token);
            foreach (var el in e.Lines)
            {
                var decision = decisions[el.LineId]!;
                el.Decision = decision;
                var line = lines[el.LineId];
                line.ApprovalStatus = decision; line.DecidedAt = at;
                line.ApprovedEstimateId = decision == A.Approved ? e.Id : null;
            }
            e.Status = ESt.Responded; e.RespondedAt = at; e.ResponseChannel = channel; e.RespondedByName = who; e.ResponseNotes = Trim(r.Notes, 1000);
            e.RecordedBy = By;
            if (o.Status == St.AwaitingApproval && decisions.Values.Any(d => d == A.Approved)) Move(o, St.Approved, $"v{e.Version} • {who}");
            await _db.SaveChangesAsync(token);
            await LogAsync("WORK_ORDER_ESTIMATE_RESPONDED", o,
                $"v{e.Version} • {channel} • {who} • {decisions.Values.Count(d => d == A.Approved)}/{decisions.Count}", token);
            return await EstimateAsync(o, e.Id, token);
        }, ct);

    public async Task DeleteEstimateAsync(Guid workOrderId, Guid estimateId, CancellationToken ct = default)
    {
        var o = await EditableOrderAsync(workOrderId, ct);
        var e = await _db.Set<WorkOrderEstimate>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == estimateId && x.WorkOrderId == o.Id, ct)
            ?? throw new NotFoundException(Messages.EstimateNotFound);
        if (e.Status != ESt.Draft) throw new ConflictException(Messages.EstimateNotDraft);
        _db.Remove(e);
        await _db.SaveChangesAsync(ct);
        await LogAsync("WORK_ORDER_ESTIMATE_DELETED", o, $"v{e.Version}", ct);
    }

    private async Task<WorkOrderEstimateDto> EstimateAsync(WorkOrder o, Guid id, CancellationToken ct)
        => EstimateDto(await _db.Set<WorkOrderEstimate>().AsNoTracking().Include(e => e.Lines).FirstAsync(e => e.Id == id, ct), o.OrderNumber);

    private static WorkOrderEstimateDto EstimateDto(WorkOrderEstimate e, string orderNumber) => new()
    {
        Id = e.Id, WorkOrderId = e.WorkOrderId, Reference = $"{orderNumber}-E{e.Version}", Version = e.Version, Status = e.Status, Subtotal = e.Subtotal,
        DiscountTotal = e.DiscountTotal, VatTotal = e.VatTotal, Total = e.Total, SentAt = e.SentAt, RespondedAt = e.RespondedAt,
        ResponseChannel = e.ResponseChannel, RespondedByName = e.RespondedByName, ResponseNotes = e.ResponseNotes, Notes = e.Notes,
        CreatedBy = e.CreatedBy, RecordedBy = e.RecordedBy, CreatedAt = e.CreatedAt,
        Lines = e.Lines.OrderBy(l => l.SortOrder).Select(l => new EstimateLineDto
        {
            Id = l.Id, LineId = l.LineId, Kind = l.Kind, Description = l.Description, Quantity = l.Quantity, UnitPrice = l.UnitPrice,
            DiscountAmount = l.DiscountAmount, VatRate = l.VatRate, NetAmount = l.NetAmount, VatAmount = l.VatAmount, BillingType = l.BillingType,
            Decision = l.Decision, SortOrder = l.SortOrder,
        }).ToList(),
    };

    // ---------- الاستثناء ----------

    public Task<WorkOrderWorkDto> AddExceptionAsync(Guid workOrderId, Guid lineId, CreateExecutionExceptionDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            await _permissions.DemandAsync(PermissionCatalog.WorkshopUnapprovedExecutionScreenId, ScreenAction.Approve, ct: token);
            var o = await EditableOrderAsync(workOrderId, token);
            var line = await _db.Set<WorkOrderLine>().AsNoTracking().FirstOrDefaultAsync(l => l.Id == lineId && l.WorkOrderId == o.Id, token)
                ?? throw new NotFoundException(Messages.WorkOrderLineNotFound);
            if (line.ApprovalStatus == A.Approved) throw new ConflictException(Messages.ExecutionExceptionNotNeeded);
            if (await _db.Set<WorkOrderExecutionException>().AnyAsync(x => x.LineId == lineId, token)) throw new ConflictException(Messages.ExecutionExceptionExists);
            var reason = Trim(r.Reason, 1000) ?? throw new ValidationFailedException(Messages.ExecutionExceptionReasonRequired);
            if (r.AttachmentId is Guid att && !await _db.Set<DocumentAttachment>().AnyAsync(a => a.Id == att && a.OwnerType == WorkshopAttachmentOwnerTypes.WorkOrder
                                                                                                    && a.OwnerId == o.Id, token))
                throw new ValidationFailedException(Messages.ExecutionExceptionAttachmentInvalid);
            _db.Add(new WorkOrderExecutionException
            {
                WorkOrderId = o.Id, LineId = lineId, Reason = reason, AttachmentId = r.AttachmentId, AuthorizedBy = By, AuthorizedByUserId = _user.UserId,
                AuthorizedAt = SaudiTime.Now,
            });
            await _db.SaveChangesAsync(token);
            await LogAsync("WORK_ORDER_EXECUTION_EXCEPTION", o, $"{line.Description} • {reason}", token);
            return await GetAsync(o.Id, token);
        }, ct);

    // ---------- مساعدات ----------

    private async Task<WorkOrder> OrderAsync(Guid id, CancellationToken ct)
        => await _db.Set<WorkOrder>().FirstOrDefaultAsync(o => o.Id == id, ct) ?? throw new NotFoundException(Messages.WorkOrderNotFound);

    private async Task<WorkOrder> EditableOrderAsync(Guid id, CancellationToken ct)
    {
        var o = await OrderAsync(id, ct);
        if (!St.Editable.Contains(o.Status)) throw new ConflictException(Messages.WorkOrderNotEditable);
        return o;
    }

    /// <summary>انتقال تلقائي من التقدير بسجله (الإرسال أو الرد).</summary>
    private void Move(WorkOrder o, string to, string reason)
    {
        var from = o.Status;
        o.Status = to; o.StageId = null; o.PauseReasonId = null;
        _db.Add(new WorkOrderStatusHistory
        {
            WorkOrderId = o.Id, FromStatus = from, ToStatus = to, Reason = reason, ChangedBy = By, ChangedByUserId = _user.UserId, ChangedAt = SaudiTime.Now,
        });
    }

    private Task LogAsync(string action, WorkOrder o, string? details, CancellationToken ct)
        => _audit.LogAsync(action, nameof(WorkOrder), o.Id.ToString(), $"{o.OrderNumber}{(details == null ? string.Empty : " • " + details)}", ct);

    private static string? Trim(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) ? null : v.Length > max ? v[..max] : v;
    }
}
