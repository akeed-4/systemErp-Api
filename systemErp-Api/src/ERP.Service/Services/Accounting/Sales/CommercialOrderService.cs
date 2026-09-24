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

    protected override string Label => "الأمر";
    protected override bool Transactional => true;

    protected override IQueryable<CommercialOrder> ApplySearch(IQueryable<CommercialOrder> q, string t)
        => q.Where(x => x.OrderNumber.Contains(t) || x.PartyName.Contains(t));

    protected override IQueryable<CommercialOrder> ApplyFilters(IQueryable<CommercialOrder> q, PaginationParams p)
        => string.IsNullOrWhiteSpace(p.Status) ? q : q.Where(x => x.Status == p.Status);

    protected override Task ValidateAsync(CreateCommercialOrderDto d, CommercialOrder? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        TradeHelper.RequireParty(d.PartyName, errors);
        if (d.Type is not ("sales_order" or "purchase_order")) errors.Add("النوع: sales_order | purchase_order.");
        if (!TradeHelper.OrderStatuses.Contains(d.Status)) errors.Add("حالة غير صالحة.");
        if (d.ExpectedDeliveryDate < d.OrderDate) errors.Add("تاريخ التسليم المتوقع قبل تاريخ الأمر.");
        if (d.Items.Count == 0) errors.Add("يجب إدخال صنف واحد على الأقل.");
        if (existing?.ConvertedInvoiceId != null) errors.Add("لا يمكن تعديل أمر تم تحويله لفاتورة.");
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
        return Task.CompletedTask;
    }

    protected override async Task OnCreatingAsync(CommercialOrder e, CreateCommercialOrderDto d, CancellationToken ct)
    {
        e.OrderNumber = await _numbers.NextAsync(e.Type, e.Type == "sales_order" ? "SO-" : "PO-", ct);
        Recalculate(e);
    }

    protected override Task OnUpdatingAsync(CommercialOrder e, UpdateCommercialOrderDto d, CancellationToken ct)
    {
        e.OrderNumber = Db.Entry(e).OriginalValues.GetValue<string>(nameof(CommercialOrder.OrderNumber));
        e.ConvertedInvoiceId = Db.Entry(e).OriginalValues.GetValue<Guid?>(nameof(CommercialOrder.ConvertedInvoiceId));
        Recalculate(e);
        return Task.CompletedTask;
    }

    protected override Task OnDeletingAsync(CommercialOrder e, CancellationToken ct)
        => e.ConvertedInvoiceId != null ? throw new ConflictException("لا يمكن حذف أمر تم تحويله لفاتورة.") : Task.CompletedTask;

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
                ?? throw new NotFoundException("الأمر غير موجود");
            if (o.ConvertedInvoiceId != null) throw new ConflictException("تم تحويل هذا الأمر مسبقاً.");
            if (o.Status is "cancelled" or "draft") throw new ConflictException("أكّد الأمر أولاً؛ الأمر الملغى لا يُحوَّل.");

            var sales = o.Type == "sales_order";
            var invoice = await _invoices.CreateAsync(new CreateInvoiceDto
            {
                Kind = sales ? InvoiceKind.Sales : InvoiceKind.Purchase,
                InvoiceType = sales ? TradeHelper.InvoiceTypeFor(o.PartyVatNumber) : InvoiceType.TaxInvoice,
                PartyId = o.PartyId, PartyName = o.PartyName, PartyPhone = o.PartyPhone, PartyVatNumber = o.PartyVatNumber,
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
