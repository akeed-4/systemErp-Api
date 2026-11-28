using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using A = ERP.Core.Models.Accounting.LineApprovalStatuses;
using B = ERP.Core.Models.Accounting.LineBillingTypes;
using St = ERP.Core.Models.Accounting.WorkOrderStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// إنهاء أمر العمل (W4) عبر الخدمات القائمة (محرك فواتير المبيعات ومرتجعاتها والقيود). القواعد:
/// <list type="bullet">
/// <item>الفوترة لأمر «جاهز» بلا خدمة خارجية معلّقة: تُفوتر الأسطر الموافق عليها على العميل بأسعارها المثبّتة (العمالة والخارجي والرسوم
/// بصنفها الخدمي، والقطعة سطراً غير مخزني لأنها صُرفت للأمر)، بفاتورة مرحّلة آجلة مرجعها <see cref="ReferenceType"/> لا تُعدَّل ولا
/// تُرتجع إلا من الأمر. الأمر بلا مبلغ على العميل (ضمان، داخلي) يُنهى بلا فاتورة.</item>
/// <item>قيد التكلفة: ما رُحِّل على تحت التنفيذ لكل سطر (ساعات العمل والقطع الصافية) يُنقل: العميل والداخلي إلى تكلفة إيرادات الورشة،
/// والضمان والمجاملة إلى مصروف الضمان.</item>
/// <item>إلغاء الفاتورة قبل التسليم وبلا تحصيل عليها: مرتجع كامل نظامي، وعكس قيد التكلفة، ويعود الأمر «جاهزاً».</item>
/// <item>التسليم للمفوتر بعدّاد لا يقل عن الاستقبال واسم المستلم، وينشئ ضمان كل سطر منجز بشروطه (الخدمة أو الإعداد). الإغلاق للمُسلَّم.</item>
/// <item>المطالبة على ضمان نشط ضمن مدته وحده: أمر عمل ضمان للمركبة باستقبال جديد. الإبطال بسبب.</item>
/// </list>
/// </summary>
public class WorkOrderBillingService : IWorkOrderBillingService
{
    public const string ReferenceType = "workshop_work_order";
    private const string CostSourceType = "workshop_work_order_cost";
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(15);

    private readonly ErpDbContext _db;
    private readonly ISalesInvoiceService _sales;
    private readonly ISalesReturnService _returns;
    private readonly IAccountingPostingService _posting;
    private readonly IWorkOrderService _orders;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;
    private readonly IAuditService _audit;

    public WorkOrderBillingService(ErpDbContext db, ISalesInvoiceService sales, ISalesReturnService returns, IAccountingPostingService posting,
        IWorkOrderService orders, ITransactionRunner tx, ICurrentUser user, IPermissionService permissions, IAuditService audit)
    {
        _db = db; _sales = sales; _returns = returns; _posting = posting; _orders = orders; _tx = tx; _user = user; _permissions = permissions; _audit = audit;
    }

    private string? By => _user.Name ?? _user.Email;

    // ---------- الفوترة ----------

    private sealed record Plan(List<(WorkOrderLine Line, decimal Net, decimal Vat, decimal Cost)> Billed, List<(WorkOrderLine Line, decimal Cost)> Costed,
        int Excluded);

    /// <summary>الأسطر المفوترة وتكلفة كل سطر المرحّلة على تحت التنفيذ.</summary>
    private async Task<Plan> PlanAsync(Guid orderId, CancellationToken ct)
    {
        var lines = await _db.Set<WorkOrderLine>().AsNoTracking().Where(l => l.WorkOrderId == orderId).OrderBy(l => l.SortOrder).ToListAsync(ct);
        var labor = await _db.Set<WorkOrderTimeLog>().AsNoTracking()
            .Where(l => l.WorkOrderId == orderId && l.JournalEntryId != null && l.LineId != null)
            .GroupBy(l => l.LineId!.Value).Select(g => new { g.Key, Cost = g.Sum(l => l.Cost) }).ToDictionaryAsync(x => x.Key, x => x.Cost, ct);
        var parts = await _db.Set<WorkOrderPartIssue>().AsNoTracking().Where(i => i.WorkOrderId == orderId && i.JournalEntryId != null)
            .GroupBy(i => i.LineId).Select(g => new { g.Key, Cost = g.Sum(i => i.Kind == PartIssueKinds.Issue ? i.TotalCost : -i.TotalCost) })
            .ToDictionaryAsync(x => x.Key, x => x.Cost, ct);
        decimal CostOf(Guid id) => labor.GetValueOrDefault(id) + parts.GetValueOrDefault(id);
        var issued = await _db.Set<WorkOrderPartIssue>().AsNoTracking().Where(i => i.WorkOrderId == orderId)
            .GroupBy(i => i.LineId).Select(g => new { g.Key, Qty = g.Sum(i => i.Kind == PartIssueKinds.Issue ? i.Quantity : -i.Quantity) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);

        var billed = new List<(WorkOrderLine, decimal, decimal, decimal)>();
        var excluded = 0;
        foreach (var l in lines)
        {
            if (l.ApprovalStatus != A.Approved) { excluded++; continue; }
            if (l.BillingType != B.Customer) continue;
            var line = l;
            // القطعة تُفوتر بما صُرف فعلاً للأمر (ضمن كمية السطر الموافق عليها)، والخصم بنسبته
            if (l.Kind == WorkOrderLineKinds.Part)
            {
                var qty = Math.Min(l.Quantity, Math.Max(0, issued.GetValueOrDefault(l.Id)));
                if (qty <= 0) { excluded++; continue; }
                line = new WorkOrderLine
                {
                    Id = l.Id, Kind = l.Kind, ProductId = l.ProductId, Description = l.Description, Quantity = qty, UnitPrice = l.UnitPrice,
                    DiscountAmount = DocumentPricing.Round(l.DiscountAmount * qty / l.Quantity), VatRate = l.VatRate, BillingType = l.BillingType,
                };
            }
            var (net, vat) = WorkOrderRules.Amounts(line.Quantity, line.UnitPrice, line.DiscountAmount, line.VatRate);
            if (net > 0) billed.Add((line, net, vat, CostOf(l.Id)));
        }
        var costed = lines.Select(l => (l, CostOf(l.Id))).Where(x => x.Item2 != 0).ToList();
        return new Plan(billed, costed, excluded);
    }

    public async Task<WorkOrderBillingPreviewDto> PreviewAsync(Guid workOrderId, CancellationToken ct = default)
    {
        var o = await _db.Set<WorkOrder>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == workOrderId, ct) ?? throw new NotFoundException(Messages.WorkOrderNotFound);
        var plan = await PlanAsync(o.Id, ct);
        var cost = await _permissions.HasPermissionAsync(PermissionCatalog.WorkshopCostViewScreenId, ScreenAction.View, ct);
        return new WorkOrderBillingPreviewDto
        {
            WorkOrderId = o.Id, ExcludedCount = plan.Excluded,
            Lines = plan.Billed.Select(b => new BillingPreviewLineDto
            {
                LineId = b.Line.Id, Description = b.Line.Description, BillingType = b.Line.BillingType, Quantity = b.Line.Quantity, UnitPrice = b.Line.UnitPrice,
                Discount = b.Line.DiscountAmount, Net = b.Net, Vat = b.Vat, Cost = cost ? b.Cost : null,
            }).ToList(),
            Net = plan.Billed.Sum(b => b.Net), Vat = plan.Billed.Sum(b => b.Vat), Total = plan.Billed.Sum(b => b.Net + b.Vat),
            CostToRevenue = cost ? plan.Costed.Where(c => !ToWarranty(c.Line)).Sum(c => c.Cost) : null,
            CostToWarranty = cost ? plan.Costed.Where(c => ToWarranty(c.Line)).Sum(c => c.Cost) : null,
        };
    }

    private static bool ToWarranty(WorkOrderLine l) => l.BillingType is B.Warranty or B.Goodwill;

    public Task<WorkOrderDto> InvoiceAsync(Guid workOrderId, InvoiceWorkOrderDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var o = await _db.Set<WorkOrder>().FirstOrDefaultAsync(x => x.Id == workOrderId, token) ?? throw new NotFoundException(Messages.WorkOrderNotFound);
            if (o.Status != St.Ready) throw new ConflictException(Messages.WorkOrderInvoiceReadyOnly);
            if (await _db.Set<WorkOrderExternalService>().AnyAsync(x => x.WorkOrderId == o.Id && x.Status == ExternalServiceStatuses.Requested, token))
                throw new ConflictException(Messages.WorkOrderExternalPending);
            var day = (r.Date ?? SaudiTime.Today).Date;
            if (day > SaudiTime.Today || day < o.ReceivedAt.Date) throw new ValidationFailedException(Messages.WorkOrderInvoiceDateInvalid);
            var plan = await PlanAsync(o.Id, token);
            var note = $"أمر عمل الورشة {o.OrderNumber}{(o.PlateSnapshot == null ? string.Empty : " - " + o.PlateSnapshot)}";

            if (plan.Billed.Count > 0)
            {
                var customer = await _db.Set<Customer>().AsNoTracking().FirstAsync(c => c.Id == o.CustomerId, token);
                var invoice = await _sales.CreateAsync(new CreateInvoiceDto
                {
                    SystemGenerated = true, Kind = InvoiceKind.Sales, InvoiceType = TradeHelper.InvoiceTypeFor(customer.VatNumber), IssueDate = day,
                    PartyId = customer.Id, PartyName = customer.NameAr, PartyVatNumber = customer.VatNumber, PartyPhone = customer.Phone, PartyEmail = customer.Email,
                    PaymentMethod = PaymentMethod.Credit, Status = "posted", WarehouseId = o.WarehouseId,
                    Notes = string.IsNullOrWhiteSpace(r.Notes) ? note : $"{note} — {r.Notes.Trim()}",
                    ReferenceType = ReferenceType, ReferenceId = o.Id, ReferenceNumber = o.OrderNumber,
                    // القطعة صُرفت للأمر بحركة مخزونها: تُفوتر سطراً غير مخزني حتى لا تُصرف مرة ثانية
                    Items = plan.Billed.Select(b => new InvoiceItemDto
                    {
                        ItemId = b.Line.Kind == WorkOrderLineKinds.Part ? Guid.Empty : b.Line.ProductId, ItemName = b.Line.Description,
                        Unit = b.Line.Kind == WorkOrderLineKinds.Part ? "قطعة" : "خدمة", Quantity = b.Line.Quantity, UnitPrice = b.Line.UnitPrice,
                        Discount = b.Line.DiscountAmount, VatRate = b.Line.VatRate,
                    }).ToList(),
                }, token);
                o.InvoiceId = invoice.Id; o.InvoiceNumber = invoice.InvoiceNumber;
            }

            var total = DocumentPricing.Round(plan.Costed.Sum(c => c.Cost));
            // O10 (ب): الخدمات الخارجية المرتبطة بفاتورة المورد قُيِّدت هناك على مصروف الخدمات المشتراة؛ تُنقل إلى تكلفة الأمر بالقيد نفسه
            var lineKinds = await _db.Set<WorkOrderLine>().AsNoTracking().Where(l => l.WorkOrderId == o.Id).ToDictionaryAsync(l => l.Id, token);
            var externals = (await _db.Set<WorkOrderExternalService>().AsNoTracking()
                    .Where(x => x.WorkOrderId == o.Id && x.Status == ExternalServiceStatuses.Completed && x.PurchaseInvoiceId != null && x.ActualCost > 0)
                    .ToListAsync(token))
                .Select(x => (Line: lineKinds[x.LineId], Cost: DocumentPricing.Round(x.ActualCost ?? 0))).ToList();
            var externalTotal = externals.Sum(x => x.Cost);
            if (total > 0 || externalTotal > 0)
            {
                await ContractAccountsSetup.EnsureModuleMissingAsync(_db, ContractAccountsSetup.ModuleAccountSet.Workshop, token);
                var s = await _db.Set<AccountingSettings>().AsNoTracking().FirstAsync(token);
                string Need(string? code) => code ?? throw new ValidationFailedException(Messages.WorkshopAccountsMissing);
                var wip = Need(s.WorkshopWipAccountCode);
                var costNote = $"تكلفة {note}{(o.InvoiceNumber == null ? string.Empty : " - فاتورة " + o.InvoiceNumber)}";
                var lines = new List<PostingLine>();
                var toRevenue = DocumentPricing.Round(plan.Costed.Where(c => !ToWarranty(c.Line)).Sum(c => c.Cost) + externals.Where(x => !ToWarranty(x.Line)).Sum(x => x.Cost));
                var toWarranty = DocumentPricing.Round(plan.Costed.Where(c => ToWarranty(c.Line)).Sum(c => c.Cost) + externals.Where(x => ToWarranty(x.Line)).Sum(x => x.Cost));
                if (toRevenue != 0) lines.Add(new PostingLine(Need(s.WorkshopCostOfRevenueAccountCode), Math.Max(0, toRevenue), Math.Max(0, -toRevenue), costNote));
                if (toWarranty != 0) lines.Add(new PostingLine(Need(s.WorkshopWarrantyExpenseAccountCode), Math.Max(0, toWarranty), Math.Max(0, -toWarranty), costNote));
                if (total != 0) lines.Add(new PostingLine(wip, Math.Max(0, -total), Math.Max(0, total), costNote));
                if (externalTotal > 0)
                {
                    await DefaultAccounts.EnsureAsync(_db, token, DefaultAccounts.PurchasedServices);
                    lines.Add(new PostingLine(DefaultAccounts.PurchasedServices, 0, externalTotal, costNote));
                }
                o.CostJournalEntryId = (await _posting.PostAsync(new GenericPostingRequest
                {
                    Date = day, Description = costNote, SourceType = CostSourceType, SourceId = o.Id, SourceNumber = o.InvoiceNumber ?? o.OrderNumber,
                    WarehouseId = o.WarehouseId, Lines = lines,
                }, token)).JournalEntryId;
            }
            o.CostAmount = total + externalTotal;
            foreach (var res in await _db.Set<WorkOrderPartReservation>().Where(x => x.WorkOrderId == o.Id && x.Status == PartReservationStatuses.Active).ToListAsync(token))
                res.Status = PartReservationStatuses.Released;
            Move(o, St.Invoiced, o.InvoiceNumber);
            o.InvoicedAt = SaudiTime.Now;
            await _db.SaveChangesAsync(token);
            await LogAsync("WORK_ORDER_INVOICED", o, $"{o.InvoiceNumber ?? "—"} • {plan.Billed.Sum(b => b.Net + b.Vat):0.##} • cost {total:0.##}", token);
            return await _orders.GetAsync(o.Id, token);
        }, ct);

    public Task<WorkOrderDto> CancelInvoiceAsync(Guid workOrderId, WorkshopReasonDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var o = await _db.Set<WorkOrder>().FirstOrDefaultAsync(x => x.Id == workOrderId, token) ?? throw new NotFoundException(Messages.WorkOrderNotFound);
            if (o.Status != St.Invoiced) throw new ConflictException(Messages.WorkOrderNotInvoiced);
            var reason = Trim(r.Reason, 500) ?? throw new ValidationFailedException(Messages.WorkshopCancelReasonRequired);
            var text = $"إلغاء فاتورة أمر العمل {o.OrderNumber} ({o.InvoiceNumber}): {reason}";
            if (o.InvoiceId is Guid invoiceId)
            {
                if (await _db.Set<VoucherAllocation>().AnyAsync(a => a.InvoiceId == invoiceId, token)) throw new ConflictException(Messages.WorkOrderInvoiceHasPayments);
                await _returns.CreateFromInvoiceAsync(new CreateReturnInvoiceRequestDto
                {
                    OriginalInvoiceId = invoiceId, ReturnReason = text, SystemGenerated = true, ReturnDate = SaudiTime.Today,
                }, token);
            }
            if (o.CostJournalEntryId is Guid cost) await _posting.ReverseAsync(cost, text, token);
            o.InvoiceId = null; o.InvoiceNumber = null; o.CostJournalEntryId = null; o.CostAmount = 0; o.InvoicedAt = null;
            Move(o, St.Ready, reason);
            await _db.SaveChangesAsync(token);
            await LogAsync("WORK_ORDER_INVOICE_CANCELLED", o, text, token);
            return await _orders.GetAsync(o.Id, token);
        }, ct);

    // ---------- التسليم والإغلاق ----------

    public Task<WorkOrderDto> DeliverAsync(Guid workOrderId, DeliverWorkOrderDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var o = await _db.Set<WorkOrder>().FirstOrDefaultAsync(x => x.Id == workOrderId, token) ?? throw new NotFoundException(Messages.WorkOrderNotFound);
            if (o.Status != St.Invoiced) throw new ConflictException(Messages.WorkOrderNotInvoiced);
            var odometer = decimal.Round(r.OdometerOut, 0);
            if (odometer < o.OdometerIn) throw new ValidationFailedException(string.Format(Messages.WorkOrderOdometerBackwards, odometer, o.OdometerIn));
            var to = Trim(r.DeliveredTo, 200) ?? throw new ValidationFailedException(Messages.WorkOrderDeliveredToRequired);
            var now = SaudiTime.Now;
            var at = r.DeliveredAt is DateTime d ? (d.Kind == DateTimeKind.Utc ? SaudiTime.FromUtc(d) : DateTime.SpecifyKind(d, DateTimeKind.Unspecified)) : now;
            if (at > now + FutureTolerance || at < o.ReceivedAt) throw new ValidationFailedException(Messages.WorkOrderDeliveryTimeInvalid);

            o.OdometerOut = odometer; o.DeliveredAt = at; o.DeliveredTo = to; o.DeliveredBy = By;
            if (!string.IsNullOrWhiteSpace(r.Notes)) o.Notes = Trim($"{o.Notes} {r.Notes}".Trim(), 1000);
            var vehicle = await _db.Set<ServiceVehicle>().FirstAsync(v => v.Id == o.VehicleId, token);
            if (odometer > vehicle.LastOdometer) vehicle.LastOdometer = odometer;

            // ضمان كل سطر منجز بشروطه: الخدمة المعرّفة، وإلا افتراض الإعداد
            var settings = await _db.Set<WorkshopSettings>().AsNoTracking().FirstOrDefaultAsync(token);
            var executable = await WorkOrderRules.ExecutableAsync(_db, o.Id, token);
            var lines = await _db.Set<WorkOrderLine>().AsNoTracking()
                .Where(l => l.WorkOrderId == o.Id && executable.Contains(l.Id) && l.ExecutionStatus == LineExecutionStatuses.Done).ToListAsync(token);
            var serviceIds = lines.Where(l => l.ServiceId != null).Select(l => l.ServiceId!.Value).ToList();
            var services = await _db.Set<WorkshopServiceDefinition>().AsNoTracking().Where(s => serviceIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, token);
            foreach (var l in lines)
            {
                var service = l.ServiceId is Guid sid ? services.GetValueOrDefault(sid) : null;
                var months = service?.WarrantyMonths ?? settings?.DefaultWarrantyMonths ?? 0;
                var km = service?.WarrantyKm ?? settings?.DefaultWarrantyKm ?? 0;
                if (months <= 0 && km <= 0) continue;
                _db.Add(new WorkshopWarranty
                {
                    WorkOrderId = o.Id, LineId = l.Id, VehicleId = o.VehicleId, Description = l.Description, StartDate = at.Date,
                    EndDate = months > 0 ? at.Date.AddMonths(months) : null, KmLimit = km > 0 ? odometer + km : null, Status = WarrantyStatuses.Active,
                });
            }
            WorkshopFollowUpRules.AddOnDelivery(_db, settings, o, at, odometer, By);
            Move(o, St.Delivered, to);
            await _db.SaveChangesAsync(token);
            await LogAsync("WORK_ORDER_DELIVERED", o, $"{to} • {odometer:0}", token);
            return await _orders.GetAsync(o.Id, token);
        }, ct);

    public async Task<WorkOrderDto> CloseAsync(Guid workOrderId, CancellationToken ct = default)
    {
        var o = await _db.Set<WorkOrder>().FirstOrDefaultAsync(x => x.Id == workOrderId, ct) ?? throw new NotFoundException(Messages.WorkOrderNotFound);
        if (o.Status != St.Delivered) throw new ConflictException(Messages.WorkOrderNotDelivered);
        o.ClosedAt = SaudiTime.Now;
        Move(o, St.Closed, null);
        await _db.SaveChangesAsync(ct);
        await LogAsync("WORK_ORDER_CLOSED", o, null, ct);
        return await _orders.GetAsync(o.Id, ct);
    }

    // ---------- الضمان ----------

    public async Task<List<WarrantyDto>> WarrantiesAsync(Guid? vehicleId, Guid? workOrderId, CancellationToken ct = default)
    {
        var q = _db.Set<WorkshopWarranty>().AsNoTracking();
        if (vehicleId is Guid v) q = q.Where(w => w.VehicleId == v);
        if (workOrderId is Guid o) q = q.Where(w => w.WorkOrderId == o);
        var list = await q.OrderByDescending(w => w.StartDate).Take(1000).ToListAsync(ct);
        var orderIds = list.Select(w => w.WorkOrderId).Concat(list.Where(w => w.ClaimWorkOrderId != null).Select(w => w.ClaimWorkOrderId!.Value)).Distinct().ToList();
        var orders = await _db.Set<WorkOrder>().AsNoTracking().Where(x => orderIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => new { x.OrderNumber, x.CustomerName, x.PlateSnapshot }, ct);
        var vehicleIds = list.Select(w => w.VehicleId).Distinct().ToList();
        var vehicles = await _db.Set<ServiceVehicle>().AsNoTracking().Where(x => vehicleIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var today = SaudiTime.Today;
        return list.Select(w =>
        {
            var order = orders.GetValueOrDefault(w.WorkOrderId);
            var vehicle = vehicles.GetValueOrDefault(w.VehicleId);
            return new WarrantyDto
            {
                Id = w.Id, WorkOrderId = w.WorkOrderId, OrderNumber = order?.OrderNumber, LineId = w.LineId, VehicleId = w.VehicleId,
                VehicleLabel = vehicle == null ? null : $"{vehicle.Make} {vehicle.Model}".Trim(), PlateNumber = vehicle?.PlateNumber ?? order?.PlateSnapshot,
                CustomerName = order?.CustomerName, Description = w.Description, StartDate = w.StartDate, EndDate = w.EndDate, KmLimit = w.KmLimit,
                Status = w.Status == WarrantyStatuses.Active && w.EndDate is DateTime end && end < today ? "expired" : w.Status,
                ClaimWorkOrderId = w.ClaimWorkOrderId, ClaimOrderNumber = w.ClaimWorkOrderId is Guid c ? orders.GetValueOrDefault(c)?.OrderNumber : null,
                VoidReason = w.VoidReason,
            };
        }).ToList();
    }

    public Task<WorkOrderDto> ClaimWarrantyAsync(Guid warrantyId, ClaimWarrantyDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var w = await _db.Set<WorkshopWarranty>().FirstOrDefaultAsync(x => x.Id == warrantyId, token) ?? throw new NotFoundException(Messages.WarrantyNotFound);
            if (w.Status != WarrantyStatuses.Active) throw new ConflictException(Messages.WarrantyNotActive);
            if (w.EndDate is DateTime end && end < SaudiTime.Today) throw new ConflictException(Messages.WarrantyExpired);
            if (w.KmLimit is decimal limit && r.OdometerIn > limit) throw new ConflictException(string.Format(Messages.WarrantyKmExceeded, limit));
            var vehicle = await _db.Set<ServiceVehicle>().AsNoTracking().FirstAsync(v => v.Id == w.VehicleId, token);
            var reception = await _orders.CreateReceptionAsync(new SaveWorkshopReceptionDto
            {
                CustomerId = vehicle.CustomerId, WarehouseId = r.WarehouseId,
                Vehicles = new List<ReceptionVehicleDto>
                {
                    new() { VehicleId = vehicle.Id, OdometerIn = r.OdometerIn, Complaint = Trim(r.Complaint, 2000) ?? $"مطالبة ضمان: {w.Description}" },
                },
            }, token);
            var orderId = reception.Orders.Single().Id;
            var o = await _db.Set<WorkOrder>().FirstAsync(x => x.Id == orderId, token);
            o.IsWarrantyJob = true; o.WarrantyId = w.Id;
            w.Status = WarrantyStatuses.Claimed; w.ClaimWorkOrderId = o.Id;
            await _db.SaveChangesAsync(token);
            await LogAsync("WORK_ORDER_WARRANTY_CLAIMED", o, w.Description, token);
            return await _orders.GetAsync(o.Id, token);
        }, ct);

    public async Task<WarrantyDto> VoidWarrantyAsync(Guid warrantyId, WorkshopReasonDto r, CancellationToken ct = default)
    {
        var w = await _db.Set<WorkshopWarranty>().FirstOrDefaultAsync(x => x.Id == warrantyId, ct) ?? throw new NotFoundException(Messages.WarrantyNotFound);
        if (w.Status != WarrantyStatuses.Active) throw new ConflictException(Messages.WarrantyNotActive);
        w.VoidReason = Trim(r.Reason, 500) ?? throw new ValidationFailedException(Messages.WorkshopCancelReasonRequired);
        w.Status = WarrantyStatuses.Void;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("WORKSHOP_WARRANTY_VOIDED", nameof(WorkshopWarranty), w.Id.ToString(), $"{w.Description} • {w.VoidReason}", ct);
        return (await WarrantiesAsync(null, w.WorkOrderId, ct)).First(x => x.Id == w.Id);
    }

    // ---------- مساعدات ----------

    private void Move(WorkOrder o, string to, string? reason)
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
