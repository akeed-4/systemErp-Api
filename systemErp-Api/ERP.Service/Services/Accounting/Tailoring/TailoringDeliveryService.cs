using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using D = ERP.Core.Models.Accounting.TailoringDeliveryStatuses;
using G = ERP.Core.Models.Accounting.TailoringGarmentStatuses;
using St = ERP.Core.Models.Accounting.TailoringOrderStatuses;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// الجودة والتسليم والفوترة. القواعد:
/// <list type="bullet">
/// <item>الفحص لقطع في مرحلة جودة: الاعتماد (<c>tailoring-quality</c> اعتماد) يجعلها «جاهزة» بقواعد الإنتاج، والرفض (رفض) يفتح طلب
/// تعديل بالملاحظة.</item>
/// <item>التسليم لقطع «جاهزة» من أمر قائم. صافي القطعة نصيبها من صافي سطرها، وآخر قطع السطر تأخذ باقيه. الاسترداد = صافي الدفعة ×
/// صافي التسليم ÷ صافي الأمر، وآخر تسليم يسترد الباقي كله.</item>
/// <item>فاتورة ضريبية واحدة للتسليم بالخاضع = الصافي − الاسترداد (ضريبة العربون حُصّلت في فاتورته)، والاسترداد بقيد مدين الدفعات
/// المقدمة / دائن الإيراد نفسه. خاضع صفر = تسوية بلا فاتورة. التكلفة المرحّلة للقطع (موادها وأجورها ونصيبها من مواد الأمر العامة، وآخر
/// تسليم يأخذ الباقي) تُنقل من تحت التنفيذ إلى تكلفة الإيرادات.</item>
/// <item>الإلغاء بسبب قبل إغلاق الأمر وبلا سندات على فاتورته: مرتجع نظامي، وعكس القيدين، وعودة القطع «جاهزة».</item>
/// <item>الإغلاق بعد تسليم كل القطع، وينقل ما قُيّد على الأمر بعد آخر تسليم إلى تكلفة الإيرادات.</item>
/// </list>
/// </summary>
public class TailoringDeliveryService : ITailoringDeliveryService
{
    public const string ReferenceType = "tailoring_delivery";
    private const string CostSourceType = "tailoring_delivery_cost";
    private const string CloseSourceType = "tailoring_close_cost";
    private const int MaxPieces = 500, MaxSignatureLength = 400_000;

    private readonly ErpDbContext _db;
    private readonly ITailoringProductionService _production;
    private readonly ISalesInvoiceService _sales;
    private readonly ISalesReturnService _returns;
    private readonly IAccountingPostingService _posting;
    private readonly INumberSequenceService _numbers;
    private readonly ITransactionRunner _tx;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;
    private readonly IAuditService _audit;

    public TailoringDeliveryService(ErpDbContext db, ITailoringProductionService production, ISalesInvoiceService sales, ISalesReturnService returns,
        IAccountingPostingService posting, INumberSequenceService numbers, ITransactionRunner tx, ICurrentUser user, IPermissionService permissions, IAuditService audit)
    {
        _db = db; _production = production; _sales = sales; _returns = returns; _posting = posting; _numbers = numbers; _tx = tx; _user = user;
        _permissions = permissions; _audit = audit;
    }

    private string? By => _user.Name ?? _user.Email;
    private Task<bool> CanSeeCostAsync(CancellationToken ct) => _permissions.HasPermissionAsync(PermissionCatalog.TailoringCostViewScreenId, ScreenAction.View, ct);

    // ---------- الجودة ----------

    public Task InspectAsync(TailoringInspectDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var ids = r.GarmentIds.Distinct().ToList();
            if (ids.Count == 0) throw new ValidationFailedException(Messages.TailoringNoGarments);
            if (ids.Count > MaxPieces) throw new ValidationFailedException(Messages.TailoringOrderInvalid);
            await _permissions.DemandAsync(PermissionCatalog.TailoringQualityScreenId, r.Approved ? ScreenAction.Approve : ScreenAction.Reject, null, token);
            var garments = await _db.Set<TailoringGarment>().AsNoTracking().Where(g => ids.Contains(g.Id)).Select(g => new { g.Id, g.Code, g.Status }).ToListAsync(token);
            if (garments.Count != ids.Count) throw new NotFoundException(Messages.TailoringGarmentNotFound);
            var wrong = garments.FirstOrDefault(g => g.Status != G.Quality);
            if (wrong != null) throw new ConflictException(string.Format(Messages.TailoringNotInQuality, wrong.Code));
            var note = Trim(r.Note, 2000);
            if (!r.Approved && note == null) throw new ValidationFailedException(Messages.TailoringModificationInstructionsRequired);
            foreach (var g in garments)
            {
                if (r.Approved) await _production.MoveAsync(g.Id, new TailoringMoveDto { Ready = true, Note = note ?? Messages.TailoringHistoryQualityApproved }, token);
                else await _production.CreateModificationAsync(new SaveTailoringModificationDto { GarmentId = g.Id, Instructions = note!, EmployeeId = r.EmployeeId }, token);
            }
            await _audit.LogAsync(r.Approved ? "TAILORING_QUALITY_APPROVED" : "TAILORING_QUALITY_REJECTED", nameof(TailoringGarment),
                string.Join(',', garments.Select(g => g.Code)), note ?? "-", token);
        }, ct);

    // ---------- القراءة ----------

    public async Task<TailoringOrderDeliveryDto> OrderAsync(Guid orderId, CancellationToken ct = default)
    {
        var o = await _db.Set<TailoringOrder>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == orderId, ct) ?? throw new NotFoundException(Messages.TailoringOrderNotFound);
        var garments = await (from g in _db.Set<TailoringGarment>().AsNoTracking()
                              join l in _db.Set<TailoringOrderLine>().AsNoTracking() on g.LineId equals l.Id
                              where g.OrderId == orderId && g.Status != G.Cancelled
                              orderby g.Code
                              select new { g, l.GarmentTypeName, l.WearerName }).ToListAsync(ct);
        var deliveries = await ListAsync(orderId, null, null, ct);
        var active = deliveries.Where(d => d.Status == D.Posted).ToList();
        return new TailoringOrderDeliveryDto
        {
            OrderId = o.Id, OrderNumber = o.OrderNumber, Status = o.Status, CustomerName = o.CustomerName, CustomerPhone = o.CustomerPhone,
            NetTotal = o.NetTotal, GrandTotal = o.GrandTotal, AdvanceAmount = o.AdvanceAmount, AdvanceNet = o.AdvanceNet, AdvanceRecovered = o.AdvanceRecovered,
            DeliveredTotal = active.Sum(d => d.TotalAmount), PieceCount = garments.Count, DeliveredCount = garments.Count(x => x.g.Status == G.Delivered),
            CanClose = o.Status == St.Delivered,
            ReadyPieces = garments.Where(x => x.g.Status == G.Ready).Select(x => new TailoringDeliveryPieceDto
            {
                GarmentId = x.g.Id, Code = x.g.Code, GarmentTypeName = x.GarmentTypeName, WearerName = x.WearerName,
            }).ToList(),
            PendingPieces = garments.Where(x => x.g.Status is not (G.Ready or G.Delivered)).Select(x => new TailoringGarmentDto
            {
                Id = x.g.Id, LineId = x.g.LineId, PieceNo = x.g.PieceNo, Code = x.g.Code, Status = x.g.Status,
            }).ToList(),
            Deliveries = deliveries,
        };
    }

    public async Task<List<TailoringDeliveryDto>> ListAsync(Guid? orderId, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var q = _db.Set<TailoringDelivery>().AsNoTracking();
        if (orderId is Guid oid) q = q.Where(d => d.OrderId == oid);
        if (from is DateTime f) q = q.Where(d => d.Date >= f.Date);
        if (to is DateTime t) q = q.Where(d => d.Date <= t.Date);
        var rows = await q.OrderByDescending(d => d.Date).ThenByDescending(d => d.CreatedAt).Take(500).ToListAsync(ct);
        return await MapAsync(rows, false, ct);
    }

    public async Task<TailoringDeliveryDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var d = await _db.Set<TailoringDelivery>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException(Messages.TailoringDeliveryNotFound);
        return (await MapAsync(new List<TailoringDelivery> { d }, true, ct))[0];
    }

    public async Task<TailoringDeliveryAmountsDto> PreviewAsync(TailoringDeliveryPreviewRequestDto r, CancellationToken ct = default)
    {
        var o = await _db.Set<TailoringOrder>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == r.OrderId, ct) ?? throw new NotFoundException(Messages.TailoringOrderNotFound);
        return (await PlanAsync(o, r.GarmentIds, ct)).Amounts;
    }

    // ---------- التسليم ----------

    public async Task<TailoringDeliveryDto> DeliverAsync(DeliverTailoringDto r, CancellationToken ct = default)
    {
        var (deliveryId, invoiceId) = await _tx.RunAsync(async token =>
        {
            var o = await _db.Set<TailoringOrder>().FirstOrDefaultAsync(x => x.Id == r.OrderId, token) ?? throw new NotFoundException(Messages.TailoringOrderNotFound);
            if (!St.Open.Contains(o.Status)) throw new ConflictException(Messages.TailoringOrderNotConfirmed);
            if (r.PaymentMethod is not (PaymentMethod.Cash or PaymentMethod.BankCard or PaymentMethod.BankTransfer or PaymentMethod.Credit))
                throw new ValidationFailedException(Messages.InvalidPaymentMethod);
            var day = (r.Date ?? SaudiTime.Today).Date;
            if (day > SaudiTime.Today || day < o.OrderDate) throw new ValidationFailedException(Messages.TailoringDeliveryDateInvalid);
            var signature = string.IsNullOrWhiteSpace(r.SignatureImage) ? null : r.SignatureImage.Trim();
            if (signature != null && (!signature.StartsWith("data:image/png;base64,", StringComparison.Ordinal) || signature.Length > MaxSignatureLength))
                throw new ValidationFailedException(Messages.TailoringSignatureInvalid);
            var plan = await PlanAsync(o, r.GarmentIds, token);
            var a = plan.Amounts;

            var d = new TailoringDelivery
            {
                OrderId = o.Id, Date = day, PieceCount = a.PieceCount, ReceivedBy = Trim(r.ReceivedBy, 200) ?? o.CustomerName, ReceiverPhone = Trim(r.ReceiverPhone, 50),
                SignatureImage = signature, Notes = Trim(r.Notes, 1000), NetAmount = a.NetAmount, AdvanceRecovery = a.AdvanceRecovery, TaxableAmount = a.TaxableAmount,
                IsFinal = a.IsFinal, CostAmount = DocumentPricing.Round(plan.Pieces.Sum(p => p.Cost)), CreatedBy = By,
            };
            d.DeliveryNumber = await _numbers.AssignAsync(new DocumentNumberRequest
            {
                Key = "tailoring_delivery", DefaultPrefix = "TD-", WarehouseId = o.WarehouseId, Date = day,
                Exists = (n, tk) => _db.Set<TailoringDelivery>().AnyAsync(x => x.DeliveryNumber == n, tk),
            }, token);
            _db.Add(d);
            foreach (var p in plan.Pieces)
                _db.Add(new TailoringDeliveryPiece
                {
                    DeliveryId = d.Id, GarmentId = p.Garment.Id, LineId = p.Line.Id, NetAmount = p.Net, AdvanceRecovery = p.Recovery, CostAmount = p.Cost,
                });
            await _db.SaveChangesAsync(token);

            var settings = await PostingAccounts.SettingsAsync(_db, token);
            var revenue = (await PostingAccounts.SiteAsync(_db, o.WarehouseId, token)).Sales ?? settings.SalesRevenue();
            var note = $"تسليم {d.DeliveryNumber} - أمر تفصيل {o.OrderNumber}";

            if (!a.SettlementOnly)
            {
                var customer = await _db.Set<Customer>().AsNoTracking().FirstAsync(c => c.Id == o.CustomerId, token);
                var title = note + (a.AdvanceRecovery > 0
                    ? $" — صافي القطع {a.NetAmount:0.00} ناقص استرداد دفعة مقدمة {a.AdvanceRecovery:0.00} ({o.AdvanceInvoiceNumber})" : string.Empty);
                var invoice = await _sales.CreateAsync(new CreateInvoiceDto
                {
                    SystemGenerated = true, Kind = InvoiceKind.Sales, InvoiceType = TradeHelper.InvoiceTypeFor(customer.VatNumber), IssueDate = day,
                    PartyId = customer.Id, PartyName = customer.NameAr, PartyVatNumber = customer.VatNumber, PartyPhone = customer.Phone, PartyEmail = customer.Email,
                    PaymentMethod = r.PaymentMethod, PaymentMethodCode = Trim(r.PaymentMethodCode, 50), Status = "posted", WarehouseId = o.WarehouseId,
                    Notes = d.Notes == null ? title : $"{title} — {d.Notes}",
                    ReferenceType = ReferenceType, ReferenceId = d.Id, ReferenceNumber = d.DeliveryNumber,
                    // الإيراد والاسترداد على حساب واحد؛ القطع خدمة تفصيل (القماش صُرف لتحت التنفيذ) فلا حركة مخزون
                    RevenueAccountCode = revenue, SkipCreditLimit = true,
                    SendToZatca = await ZatcaLinkedAsync(token) ? true : null,
                    Items = plan.Items.Select(i => new InvoiceItemDto
                    {
                        ItemId = i.Line.ProductId ?? Guid.Empty, ItemName = i.Line.WearerName == null ? i.Line.GarmentTypeName : $"{i.Line.GarmentTypeName} - {i.Line.WearerName}",
                        Unit = "قطعة", Quantity = i.Quantity, UnitPrice = i.UnitPrice, Discount = i.Discount, VatRate = i.Line.VatRate,
                    }).ToList(),
                }, token);
                d.InvoiceId = invoice.Id; d.InvoiceNumber = invoice.InvoiceNumber;
                d.TaxableAmount = invoice.GrandTotal - invoice.VatTotal; d.VatAmount = invoice.VatTotal; d.TotalAmount = invoice.GrandTotal;
            }

            if (a.AdvanceRecovery > 0)
            {
                await ContractAccountsSetup.EnsureModuleMissingAsync(_db, ContractAccountsSetup.ModuleAccountSet.Tailoring, token);
                var advance = await _db.Set<AccountingSettings>().AsNoTracking().Select(s => s.TailoringAdvancesReceivedAccountCode).FirstOrDefaultAsync(token)
                    ?? throw new ValidationFailedException(Messages.TailoringAccountsMissing);
                var text = $"استرداد دفعة مقدمة - {note}" + (a.SettlementOnly ? " (تسوية بلا فاتورة)" : $" - فاتورة {d.InvoiceNumber}");
                d.RecoveryJournalEntryId = (await _posting.PostAsync(new GenericPostingRequest
                {
                    Date = day, Description = text, SourceType = ReferenceType, SourceId = d.Id, SourceNumber = d.DeliveryNumber, WarehouseId = o.WarehouseId,
                    Lines = new List<PostingLine> { new(advance, a.AdvanceRecovery, 0, text), new(revenue, 0, a.AdvanceRecovery, text) },
                }, token)).JournalEntryId;
            }

            d.CostJournalEntryId = await PostCostAsync(o, d.CostAmount, day, $"تكلفة {note}", CostSourceType, d.Id, d.DeliveryNumber, token);

            var now = SaudiTime.Now;
            foreach (var p in plan.Pieces)
            {
                TailoringFlow.Record(_db, p.Garment, null, G.Delivered, d.DeliveryNumber, By, _user.UserId);
                p.Garment.DeliveryId = d.Id; p.Garment.DeliveredAt = now; p.Garment.CurrentStageId = null;
            }
            o.AdvanceRecovered += a.AdvanceRecovery;
            _db.Add(new TailoringOrderStatusHistory
            {
                OrderId = o.Id, FromStatus = o.Status, ToStatus = o.Status, ChangedBy = By, ChangedByUserId = _user.UserId, ChangedAt = now,
                Note = $"{Messages.TailoringHistoryDelivered} {d.DeliveryNumber} • {a.PieceCount} • {d.TotalAmount:0.00}",
            });
            await _db.SaveChangesAsync(token);
            await TailoringFlow.DeriveOrderAsync(_db, o, By, _user.UserId, token);
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("TAILORING_DELIVERED", nameof(TailoringOrder), o.Id.ToString(),
                $"{o.OrderNumber} • {d.DeliveryNumber} • {a.PieceCount} pcs • {d.TotalAmount:0.##} • recovery {a.AdvanceRecovery:0.##}", token);
            return (d.Id, d.InvoiceId);
        }, ct);
        // الفاتورة أُنشئت داخل المعاملة فتخطّاها الإرسال التلقائي: تُرسل بعدها، والرفض يُحفظ على الفاتورة ولا يُفشل التسليم
        if (invoiceId is Guid inv)
            try { await _sales.SubmitToZatcaIfRequestedAsync(inv, ct); } catch (Exception) when (!ct.IsCancellationRequested) { }
        return await GetAsync(deliveryId, ct);
    }

    public Task<TailoringDeliveryDto> CancelAsync(Guid id, TailoringReasonDto r, CancellationToken ct = default)
        => RunAndGetAsync(async token =>
        {
            var d = await _db.Set<TailoringDelivery>().FirstOrDefaultAsync(x => x.Id == id, token) ?? throw new NotFoundException(Messages.TailoringDeliveryNotFound);
            if (d.Status != D.Posted) throw new ConflictException(Messages.TailoringDeliveryCancelled);
            var reason = Trim(r.Reason, 500) ?? throw new ValidationFailedException(Messages.TailoringReasonRequired);
            var o = await _db.Set<TailoringOrder>().FirstAsync(x => x.Id == d.OrderId, token);
            if (o.Status is St.Closed or St.Cancelled) throw new ConflictException(Messages.TailoringOrderClosed);
            // تعديل ما بعد التسليم مفتوح على إحدى قطعه يُنجز أو يُلغى أولاً
            if (await _db.Set<TailoringModification>().AnyAsync(m => m.AfterDelivery && m.Status == TailoringModificationStatuses.Open
                    && _db.Set<TailoringGarment>().Any(g => g.Id == m.GarmentId && g.DeliveryId == d.Id), token))
                throw new ConflictException(Messages.TailoringModificationOpen);
            var text = $"إلغاء التسليم {d.DeliveryNumber} لأمر التفصيل {o.OrderNumber}{(d.InvoiceNumber == null ? string.Empty : $" ({d.InvoiceNumber})")}: {reason}";

            if (d.InvoiceId is Guid invoiceId)
            {
                if (await _db.Set<VoucherAllocation>().AnyAsync(x => x.InvoiceId == invoiceId, token)) throw new ConflictException(Messages.TailoringDeliveryHasPayments);
                var payment = await _db.Set<InvoiceBase>().AsNoTracking().Where(i => i.Id == invoiceId).Select(i => new { i.PaymentMethod, i.PaymentMethodCode }).FirstAsync(token);
                await _returns.CreateFromInvoiceAsync(new CreateReturnInvoiceRequestDto
                {
                    OriginalInvoiceId = invoiceId, ReturnReason = text, SystemGenerated = true, ReturnDate = SaudiTime.Today,
                    RefundPaymentMethod = payment.PaymentMethod == PaymentMethod.Credit ? null : payment.PaymentMethod,
                    RefundPaymentMethodCode = payment.PaymentMethod == PaymentMethod.Credit ? null : payment.PaymentMethodCode,
                }, token);
            }
            if (d.RecoveryJournalEntryId is Guid rec) await _posting.ReverseAsync(rec, text, token);
            if (d.CostJournalEntryId is Guid cost) await _posting.ReverseAsync(cost, text, token);

            foreach (var g in await _db.Set<TailoringGarment>().Where(g => g.DeliveryId == d.Id).ToListAsync(token))
            {
                TailoringFlow.Record(_db, g, null, G.Ready, text, By, _user.UserId);
                g.DeliveryId = null; g.DeliveredAt = null;
            }
            o.AdvanceRecovered = Math.Max(0, o.AdvanceRecovered - d.AdvanceRecovery);
            d.Status = D.Cancelled; d.CancelReason = reason; d.CancelledAt = SaudiTime.Now;
            _db.Add(new TailoringOrderStatusHistory
            {
                OrderId = o.Id, FromStatus = o.Status, ToStatus = o.Status, ChangedBy = By, ChangedByUserId = _user.UserId, ChangedAt = SaudiTime.Now,
                Note = $"{Messages.TailoringHistoryDeliveryCancelled} {d.DeliveryNumber}: {reason}",
            });
            await _db.SaveChangesAsync(token);
            await TailoringFlow.DeriveOrderAsync(_db, o, By, _user.UserId, token);
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("TAILORING_DELIVERY_CANCELLED", nameof(TailoringOrder), o.Id.ToString(), $"{o.OrderNumber} • {text}", token);
            return d.Id;
        }, ct);

    public Task<TailoringOrderDeliveryDto> CloseOrderAsync(Guid orderId, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var o = await _db.Set<TailoringOrder>().FirstOrDefaultAsync(x => x.Id == orderId, token) ?? throw new NotFoundException(Messages.TailoringOrderNotFound);
            if (o.Status != St.Delivered) throw new ConflictException(Messages.TailoringOrderNotDelivered);
            var residual = DocumentPricing.Round(await PostedCostAsync(o.Id, null, token) - await TransferredAsync(o.Id, token));
            o.CloseCostAmount = residual;
            o.CloseCostJournalEntryId = await PostCostAsync(o, residual, SaudiTime.Today, $"تكلفة لاحقة للتسليم - إغلاق أمر التفصيل {o.OrderNumber}",
                CloseSourceType, o.Id, o.OrderNumber, token);
            _db.Add(new TailoringOrderStatusHistory
            {
                OrderId = o.Id, FromStatus = o.Status, ToStatus = St.Closed, ChangedBy = By, ChangedByUserId = _user.UserId, ChangedAt = SaudiTime.Now,
                Note = Messages.TailoringHistoryClosed,
            });
            o.Status = St.Closed; o.ClosedAt = SaudiTime.Now;
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("TAILORING_ORDER_CLOSED", nameof(TailoringOrder), o.Id.ToString(), $"{o.OrderNumber} • residual cost {residual:0.##}", token);
            return await OrderAsync(o.Id, token);
        }, ct);

    // ---------- الحساب ----------

    private sealed record PiecePlan(TailoringGarment Garment, TailoringOrderLine Line, decimal Net, decimal Recovery, decimal Cost);
    private sealed record ItemPlan(TailoringOrderLine Line, int Quantity, decimal UnitPrice, decimal Discount);
    private sealed record Plan(List<PiecePlan> Pieces, List<ItemPlan> Items, TailoringDeliveryAmountsDto Amounts);

    private async Task<Plan> PlanAsync(TailoringOrder o, List<Guid> garmentIds, CancellationToken ct)
    {
        var ids = garmentIds.Distinct().ToList();
        if (ids.Count == 0) throw new ValidationFailedException(Messages.TailoringNoGarments);
        if (ids.Count > MaxPieces) throw new ValidationFailedException(Messages.TailoringOrderInvalid);
        var all = await _db.Set<TailoringGarment>().Where(g => g.OrderId == o.Id && g.Status != G.Cancelled).ToListAsync(ct);
        var selected = all.Where(g => ids.Contains(g.Id)).OrderBy(g => g.Code).ToList();
        if (selected.Count != ids.Count) throw new NotFoundException(Messages.TailoringGarmentNotFound);
        var notReady = selected.FirstOrDefault(g => g.Status != G.Ready);
        if (notReady != null) throw new ConflictException(string.Format(Messages.TailoringDeliveryNotReady, notReady.Code));
        var lines = await _db.Set<TailoringOrderLine>().AsNoTracking().Where(l => l.OrderId == o.Id).ToDictionaryAsync(l => l.Id, ct);
        var prior = await (from p in _db.Set<TailoringDeliveryPiece>().AsNoTracking()
                           join d in _db.Set<TailoringDelivery>().AsNoTracking() on p.DeliveryId equals d.Id
                           where d.OrderId == o.Id && d.Status == D.Posted
                           select new { p.LineId, p.NetAmount }).ToListAsync(ct);
        var isFinal = all.Count(g => g.Status == G.Delivered) + selected.Count == all.Count;

        // الصافي: نصيب القطع من صافي سطرها، وآخر قطع السطر تأخذ باقيه
        var nets = new List<(TailoringGarment G, TailoringOrderLine L, decimal Net)>();
        foreach (var grp in selected.GroupBy(g => g.LineId))
        {
            var line = lines[grp.Key];
            var k = grp.Count();
            var before = prior.Where(p => p.LineId == line.Id).ToList();
            var lineNet = before.Count + k >= line.Quantity
                ? line.NetAmount - before.Sum(p => p.NetAmount)
                : DocumentPricing.Round(line.NetAmount * k / line.Quantity);
            nets.AddRange(Split(lineNet, grp.Select(_ => 1m).ToList()).Zip(grp, (n, g) => (g, line, n)));
        }
        var net = nets.Sum(n => n.Net);

        // الاسترداد: بنسبة صافي التسليم من صافي الأمر، وآخر تسليم يسترد الباقي؛ لا يتجاوز الصافي
        var outstanding = Math.Max(0, o.AdvanceNet - o.AdvanceRecovered);
        var recovery = isFinal ? outstanding : o.NetTotal > 0 ? DocumentPricing.Round(o.AdvanceNet * net / o.NetTotal) : 0;
        recovery = Math.Max(0, Math.Min(Math.Min(recovery, outstanding), net));
        var recoveries = Split(recovery, nets.Select(n => n.Net).ToList(), capped: true);

        // التكلفة المرحّلة: مواد القطعة وأجورها ونصيبها من مواد الأمر العامة؛ آخر تسليم يأخذ باقي ما على الأمر
        // (استعلام واحد مجمّع بالقطعة بدل استعلامين لكل قطعة: مئات القطع في تسليم واحد)
        var byGarment = await PostedCostByGarmentAsync(o.Id, ct);
        var pool = byGarment.GetValueOrDefault(Guid.Empty);
        var costs = nets.Select(n => DocumentPricing.Round(byGarment.GetValueOrDefault(n.G.Id) + (all.Count > 0 ? pool / all.Count : 0))).ToList();
        if (isFinal && costs.Count > 0)
        {
            var rest = DocumentPricing.Round(byGarment.Values.Sum() - await TransferredAsync(o.Id, ct));
            costs[^1] += rest - costs.Sum();
        }

        var pieces = nets.Select((n, i) => new PiecePlan(n.G, n.L, n.Net, recoveries[i], costs[i])).ToList();
        var items = new List<ItemPlan>();
        decimal vat = 0;
        foreach (var grp in pieces.GroupBy(p => p.Line.Id))
        {
            var line = grp.First().Line;
            var k = grp.Count();
            var taxable = grp.Sum(p => p.Net - p.Recovery);
            // الإجمالي بسعر السطر، والخصم يحمل خصم السطر ونصيب الاسترداد (سعر يرتفع سنتاً عند فرق التقريب)
            var unit = line.UnitPrice;
            if (DocumentPricing.Round(unit * k) < taxable) unit = Math.Ceiling(taxable / k * 100m) / 100m;
            items.Add(new ItemPlan(line, k, unit, DocumentPricing.Round(unit * k) - taxable));
            vat += DocumentPricing.Round(taxable * line.VatRate / 100m);
        }
        var taxableTotal = net - recovery;
        return new Plan(pieces, items, new TailoringDeliveryAmountsDto
        {
            PieceCount = pieces.Count, NetAmount = net, AdvanceRecovery = recovery, TaxableAmount = taxableTotal, VatAmount = vat,
            TotalAmount = taxableTotal + vat, IsFinal = isFinal, SettlementOnly = taxableTotal < 0.005m,
        });
    }

    /// <summary>يقسم المبلغ بنسب الأوزان مقرَّباً، والأخير يأخذ الباقي (والوزن الصفري يقسم بالتساوي).</summary>
    private static List<decimal> Split(decimal amount, List<decimal> weights, bool capped = false)
    {
        var result = new List<decimal>();
        if (weights.Count == 0) return result;
        var total = weights.Sum();
        decimal used = 0;
        for (var i = 0; i < weights.Count; i++)
        {
            if (i == weights.Count - 1) { result.Add(amount - used); break; }
            var share = DocumentPricing.Round(total > 0 ? amount * weights[i] / total : amount / weights.Count);
            if (capped && total > 0) share = Math.Min(share, weights[i]); // الأوزان مبالغ: لا يتجاوز نصيبٌ وزنَه
            result.Add(share);
            used += share;
        }
        return result;
    }

    /// <summary>
    /// التكلفة المرحّلة على تحت التنفيذ: للقطعة (<paramref name="garmentId"/>)، أو لمواد الأمر العامة (<see cref="Guid.Empty"/>)، أو للأمر كله
    /// (null). المواد بقيدها فقط (الدوري بلا قيد)، والأجور السارية.
    /// </summary>
    private async Task<decimal> PostedCostAsync(Guid orderId, Guid? garmentId, CancellationToken ct)
    {
        var m = _db.Set<TailoringMaterialIssue>().AsNoTracking().Where(x => x.OrderId == orderId && x.JournalEntryId != null);
        var l = _db.Set<TailoringLaborEntry>().AsNoTracking().Where(x => x.OrderId == orderId && x.Status == TailoringLaborStatuses.Posted && x.JournalEntryId != null);
        if (garmentId == Guid.Empty) { m = m.Where(x => x.GarmentId == null); l = l.Where(_ => false); }
        else if (garmentId is Guid gid) { m = m.Where(x => x.GarmentId == gid); l = l.Where(x => x.GarmentId == gid); }
        var materials = await m.SumAsync(x => (decimal?)(x.Kind == TailoringMaterialKinds.Issue ? x.TotalCost : -x.TotalCost), ct) ?? 0;
        return materials + (await l.SumAsync(x => (decimal?)x.Amount, ct) ?? 0);
    }

    /// <summary>التكلفة المرحّلة للأمر مجمّعة بالقطعة، ومواد الأمر العامة تحت <see cref="Guid.Empty"/> (القواعد نفسها في <see cref="PostedCostAsync"/>).</summary>
    private async Task<Dictionary<Guid, decimal>> PostedCostByGarmentAsync(Guid orderId, CancellationToken ct)
    {
        var materials = await _db.Set<TailoringMaterialIssue>().AsNoTracking().Where(x => x.OrderId == orderId && x.JournalEntryId != null)
            .GroupBy(x => x.GarmentId).Select(g => new { g.Key, Sum = g.Sum(x => x.Kind == TailoringMaterialKinds.Issue ? x.TotalCost : -x.TotalCost) }).ToListAsync(ct);
        var labor = await _db.Set<TailoringLaborEntry>().AsNoTracking()
            .Where(x => x.OrderId == orderId && x.Status == TailoringLaborStatuses.Posted && x.JournalEntryId != null)
            .GroupBy(x => x.GarmentId).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToListAsync(ct);
        var result = new Dictionary<Guid, decimal>();
        foreach (var m in materials) result[m.Key ?? Guid.Empty] = result.GetValueOrDefault(m.Key ?? Guid.Empty) + m.Sum;
        foreach (var l in labor) result[l.Key] = result.GetValueOrDefault(l.Key) + l.Sum;
        return result;
    }

    private async Task<decimal> TransferredAsync(Guid orderId, CancellationToken ct)
        => await _db.Set<TailoringDelivery>().AsNoTracking().Where(d => d.OrderId == orderId && d.Status == D.Posted).SumAsync(d => (decimal?)d.CostAmount, ct) ?? 0;

    /// <summary>مدين تكلفة الإيرادات / دائن تحت التنفيذ (والسالب عكسه). لا قيد للصفر.</summary>
    private async Task<Guid?> PostCostAsync(TailoringOrder o, decimal amount, DateTime day, string note, string source, Guid sourceId, string number, CancellationToken ct)
    {
        if (amount == 0) return null;
        await ContractAccountsSetup.EnsureModuleMissingAsync(_db, ContractAccountsSetup.ModuleAccountSet.Tailoring, ct);
        var s = await _db.Set<AccountingSettings>().AsNoTracking().FirstAsync(ct);
        var cost = s.TailoringCostOfRevenueAccountCode ?? throw new ValidationFailedException(Messages.TailoringAccountsMissing);
        var wip = s.TailoringWipAccountCode ?? throw new ValidationFailedException(Messages.TailoringAccountsMissing);
        var v = Math.Abs(amount);
        var lines = amount > 0
            ? new List<PostingLine> { new(cost, v, 0, note), new(wip, 0, v, note) }
            : new List<PostingLine> { new(wip, v, 0, note), new(cost, 0, v, note) };
        return (await _posting.PostAsync(new GenericPostingRequest
        {
            Date = day, Description = note, SourceType = source, SourceId = sourceId, SourceNumber = number, WarehouseId = o.WarehouseId, Lines = lines,
        }, ct)).JournalEntryId;
    }

    // ---------- مساعدات ----------

    private async Task<List<TailoringDeliveryDto>> MapAsync(List<TailoringDelivery> rows, bool details, CancellationToken ct)
    {
        var orderIds = rows.Select(r => r.OrderId).Distinct().ToList();
        var orders = await _db.Set<TailoringOrder>().AsNoTracking().Where(o => orderIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => new { o.OrderNumber, o.CustomerName, o.CustomerPhone }, ct);
        var ids = rows.Select(r => r.Id).ToList();
        var pieces = (await (from p in _db.Set<TailoringDeliveryPiece>().AsNoTracking()
                             join g in _db.Set<TailoringGarment>().AsNoTracking() on p.GarmentId equals g.Id
                             join l in _db.Set<TailoringOrderLine>().AsNoTracking() on p.LineId equals l.Id
                             where ids.Contains(p.DeliveryId)
                             orderby g.Code
                             select new { p, g.Code, l.GarmentTypeName, l.WearerName }).ToListAsync(ct)).ToLookup(x => x.p.DeliveryId);
        var cost = await CanSeeCostAsync(ct);
        return rows.Select(d =>
        {
            var o = orders.GetValueOrDefault(d.OrderId);
            return new TailoringDeliveryDto
            {
                Id = d.Id, OrderId = d.OrderId, OrderNumber = o?.OrderNumber ?? string.Empty, CustomerName = o?.CustomerName ?? string.Empty, CustomerPhone = o?.CustomerPhone,
                DeliveryNumber = d.DeliveryNumber, Date = d.Date, Status = d.Status, ReceivedBy = d.ReceivedBy, ReceiverPhone = d.ReceiverPhone,
                SignatureImage = details ? d.SignatureImage : null, Notes = d.Notes, InvoiceId = d.InvoiceId, InvoiceNumber = d.InvoiceNumber,
                PieceCount = d.PieceCount, NetAmount = d.NetAmount, AdvanceRecovery = d.AdvanceRecovery, TaxableAmount = d.TaxableAmount, VatAmount = d.VatAmount,
                TotalAmount = d.TotalAmount, IsFinal = d.IsFinal, SettlementOnly = d.InvoiceId == null, CostAmount = cost ? d.CostAmount : null,
                CancelReason = d.CancelReason, CancelledAt = d.CancelledAt, CreatedBy = d.CreatedBy, CreatedAt = d.CreatedAt,
                Pieces = pieces[d.Id].Select(x => new TailoringDeliveryPieceDto
                {
                    GarmentId = x.p.GarmentId, Code = x.Code, GarmentTypeName = x.GarmentTypeName, WearerName = x.WearerName, NetAmount = x.p.NetAmount,
                    AdvanceRecovery = x.p.AdvanceRecovery, CostAmount = cost ? x.p.CostAmount : null,
                }).ToList(),
            };
        }).ToList();
    }

    private async Task<bool> ZatcaLinkedAsync(CancellationToken ct)
        => await _db.Set<Tenant>().AsNoTracking().Select(t => t.ZatcaConfig.IsEnabled).FirstOrDefaultAsync(ct)
            && await _db.Set<ZatcaUnit>().AsNoTracking().AnyAsync(u => u.IsActive, ct);

    private async Task<TailoringDeliveryDto> RunAndGetAsync(Func<CancellationToken, Task<Guid>> work, CancellationToken ct)
        => await GetAsync(await _tx.RunAsync(work, ct), ct);

    private static string? Trim(string? v, int max)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        v = v.Trim();
        return v.Length > max ? v[..max] : v;
    }
}
