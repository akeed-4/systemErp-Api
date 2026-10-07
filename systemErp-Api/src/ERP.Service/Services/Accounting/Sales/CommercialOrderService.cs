using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class CommercialOrderService : CrudService<CommercialOrder, CommercialOrderDto, CreateCommercialOrderDto, UpdateCommercialOrderDto>, ICommercialOrderService
{
    private readonly INumberSequenceService _numbers;
    private readonly IInvoiceService _invoices;

    public CommercialOrderService(ErpDbContext db, INumberSequenceService numbers, IInvoiceService invoices) : base(db)
    {
        _numbers = numbers; _invoices = invoices;
    }

    protected override string Label => Messages.LabelOrder;
    protected override bool Transactional => true;

    protected override IQueryable<CommercialOrder> ApplySearch(IQueryable<CommercialOrder> q, string t)
        => q.Where(x => x.OrderNumber.Contains(t) || x.PartyName.Contains(t));

    protected override IQueryable<CommercialOrder> ApplyFilters(IQueryable<CommercialOrder> q, PaginationParams p)
        => string.IsNullOrWhiteSpace(p.Status) ? q : q.Where(x => x.Status == p.Status);

    /// <summary>الاتفاقيات التي تغيّر استهلاكها بهذا الطلب (القديمة والجديدة) ليُعاد احتسابها بعد الحفظ.</summary>
    private readonly HashSet<Guid> _touchedAgreements = new();

    protected override async Task ValidateAsync(CreateCommercialOrderDto d, CommercialOrder? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        TradeHelper.RequireParty(d.PartyName, errors);
        if (d.Type is not ("sales_order" or "purchase_order")) errors.Add(Messages.OrderTypeValues);
        if (!TradeHelper.OrderStatuses.Contains(d.Status)) errors.Add(Messages.InvalidStatus);
        if (d.ExpectedDeliveryDate < d.OrderDate) errors.Add(Messages.ExpectedDeliveryBeforeOrderDate);
        if (d.Items.Count == 0) errors.Add(Messages.AtLeastOneItemRequired);
        if (existing?.ConvertedInvoiceId != null) errors.Add(Messages.CannotEditInvoicedOrder);
        if (errors.Count == 0 && d.AgreementId.HasValue) await ValidateAgainstAgreementAsync(d, existing?.Id, errors, ct);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
    }

    /// <summary>بنود الأمر ضمن بنود الاتفاقية وحدودها، ولا تتجاوز الرصيد المتبقي عند الاعتماد.</summary>
    private async Task ValidateAgainstAgreementAsync(CreateCommercialOrderDto d, Guid? orderId, List<string> errors, CancellationToken ct)
    {
        var agreement = await Db.Set<Agreement>().AsNoTracking().Include(a => a.Items).FirstOrDefaultAsync(a => a.Id == d.AgreementId, ct);
        if (agreement == null) { errors.Add(Messages.AgreementNotFound); return; }
        if (agreement.Status != "active") errors.Add(Messages.AgreementNotActive);
        if ((d.Type == "sales_order") != (agreement.Type == "sales")) errors.Add(Messages.OrderTypeMismatchAgreement);
        var used = AgreementUsage.IsConsuming(d.Status)
            ? await AgreementUsage.UsedByItemAsync(Db, agreement.Id, orderId, ct)
            : new Dictionary<Guid, decimal>();
        foreach (var line in d.Items.GroupBy(i => i.ItemId))
        {
            var term = agreement.Items.FirstOrDefault(i => i.ItemId == line.Key && i.Status == "active");
            var name = line.First().ItemName;
            if (term == null) { errors.Add(string.Format(Messages.ItemNotInAgreement, name)); continue; }
            var qty = line.Sum(i => i.Quantity);
            if (qty < term.MinQuantity || (term.MaxQuantity > 0 && qty > term.MaxQuantity))
                errors.Add(string.Format(Messages.QtyOutsideAgreementLimits, name, term.MinQuantity, (term.MaxQuantity > 0 ? term.MaxQuantity.ToString("0.####") : Messages.NoLimit)));
            var remaining = term.Quantity - used.GetValueOrDefault(line.Key);
            if (AgreementUsage.IsConsuming(d.Status) && qty > remaining)
                errors.Add(string.Format(Messages.QtyExceedsAgreementRemaining, name, remaining));
        }
    }

    protected override async Task OnCreatingAsync(CommercialOrder e, CreateCommercialOrderDto d, CancellationToken ct)
    {
        e.OrderNumber = await _numbers.NextAsync(e.Type, e.Type == "sales_order" ? "SO-" : "PO-", ct);
        await StampAgreementAsync(e, ct);
        Recalculate(e);
    }

    protected override async Task OnUpdatingAsync(CommercialOrder e, UpdateCommercialOrderDto d, CancellationToken ct)
    {
        e.OrderNumber = Db.Entry(e).OriginalValues.GetValue<string>(nameof(CommercialOrder.OrderNumber));
        e.ConvertedInvoiceId = Db.Entry(e).OriginalValues.GetValue<Guid?>(nameof(CommercialOrder.ConvertedInvoiceId));
        if (Db.Entry(e).OriginalValues.GetValue<Guid?>(nameof(CommercialOrder.AgreementId)) is { } previous) _touchedAgreements.Add(previous);
        await StampAgreementAsync(e, ct);
        Recalculate(e);
    }

    private async Task StampAgreementAsync(CommercialOrder e, CancellationToken ct)
    {
        e.AgreementNumber = e.AgreementId.HasValue
            ? await Db.Set<Agreement>().Where(a => a.Id == e.AgreementId).Select(a => a.AgreementNumber).FirstOrDefaultAsync(ct)
            : null;
        if (e.AgreementId is { } id) _touchedAgreements.Add(id);
    }

    protected override Task OnDeletingAsync(CommercialOrder e, CancellationToken ct)
    {
        if (e.ConvertedInvoiceId != null) throw new ConflictException(Messages.CannotDeleteInvoicedOrder);
        if (e.AgreementId is { } id) _touchedAgreements.Add(id);
        return Task.CompletedTask;
    }

    protected override Task OnCreatedAsync(CommercialOrder e, CancellationToken ct) => RefreshTouchedAgreementsAsync(ct);
    protected override Task OnUpdatedAsync(CommercialOrder e, CancellationToken ct) => RefreshTouchedAgreementsAsync(ct);
    protected override Task OnDeletedAsync(CommercialOrder e, CancellationToken ct) => RefreshTouchedAgreementsAsync(ct);

    private async Task RefreshTouchedAgreementsAsync(CancellationToken ct)
    {
        foreach (var id in _touchedAgreements) await AgreementUsage.RefreshAsync(Db, id, ct);
        _touchedAgreements.Clear();
    }

    private static void Recalculate(CommercialOrder o)
    {
        var priced = DocumentPricing.Price(o.Items.Select(i => new PricedLineInput(i.Quantity, i.UnitPrice, i.Discount, i.VatRate)).ToList());
        var items = o.Items.ToList();
        for (var i = 0; i < items.Count; i++)
        {
            items[i].TotalBeforeVat = priced.Lines[i].Net; items[i].VatAmount = priced.Lines[i].VatAmount; items[i].TotalAfterVat = priced.Lines[i].Total;
        }
        o.Subtotal = priced.NetTotal; o.VatTotal = priced.VatTotal; o.GrandTotal = priced.GrandTotal;
    }

    public async Task<InvoiceDto> ConvertToInvoiceAsync(Guid id, CancellationToken ct = default)
        => await new TransactionRunner(Db).RunAsync(async token =>
        {
            var o = await Db.Set<CommercialOrder>().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException(Messages.OrderNotFound);
            if (o.ConvertedInvoiceId != null) throw new ConflictException(Messages.OrderAlreadyConverted);
            if (o.Status is "cancelled" or "draft") throw new ConflictException(Messages.ConfirmOrderFirst);

            var sales = o.Type == "sales_order";
            var invoice = await _invoices.CreateAsync(new CreateInvoiceDto
            {
                Kind = sales ? InvoiceKind.Sales : InvoiceKind.Purchase,
                InvoiceType = sales ? TradeHelper.InvoiceTypeFor(o.PartyVatNumber) : InvoiceType.TaxInvoice,
                PartyId = o.PartyId, PartyName = o.PartyName, PartyPhone = o.PartyPhone, PartyVatNumber = o.PartyVatNumber,
                WarehouseId = o.WarehouseId,
                PaymentMethod = o.PartyId.HasValue ? PaymentMethod.Credit : PaymentMethod.Cash, Status = "draft",
                Notes = $"فاتورة محوّلة من أمر {o.OrderNumber}",
                ReferenceType = o.Type, ReferenceId = o.Id, ReferenceNumber = o.OrderNumber,
                Items = o.Items.Select(i => new InvoiceItemDto
                {
                    ItemId = i.ItemId, ItemName = i.ItemName, Sku = i.Sku, Unit = i.Unit, Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice, Discount = i.Discount, VatRate = i.VatRate,
                }).ToList(),
            }, token);
            o.ConvertedInvoiceId = invoice.Id;
            o.Status = "completed";
            await Db.SaveChangesAsync(token);
            return invoice;
        }, ct);
}
