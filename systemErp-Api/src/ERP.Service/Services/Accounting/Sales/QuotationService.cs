using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class QuotationService : CrudService<Quotation, QuotationDto, CreateQuotationDto, UpdateQuotationDto>, IQuotationService
{
    private readonly INumberSequenceService _numbers;
    private readonly IInvoiceService _invoices;
    private readonly ICommercialOrderService _orders;

    public QuotationService(ErpDbContext db, INumberSequenceService numbers, IInvoiceService invoices, ICommercialOrderService orders) : base(db)
    {
        _numbers = numbers; _invoices = invoices; _orders = orders;
    }

    protected override string Label => "عرض السعر";
    protected override bool Transactional => true;

    protected override IQueryable<Quotation> ApplySearch(IQueryable<Quotation> q, string t)
        => q.Where(x => x.QuotationNumber.Contains(t) || x.PartyName.Contains(t));

    protected override IQueryable<Quotation> ApplyFilters(IQueryable<Quotation> q, PaginationParams p)
        => string.IsNullOrWhiteSpace(p.Status) ? q : q.Where(x => x.Status == p.Status);

    protected override Task ValidateAsync(CreateQuotationDto d, Quotation? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        TradeHelper.RequireParty(d.PartyName, errors);
        if (d.Type is not ("sales_quotation" or "purchase_quotation")) errors.Add("النوع: sales_quotation | purchase_quotation.");
        if (!TradeHelper.QuotationStatuses.Contains(d.Status)) errors.Add("حالة غير صالحة.");
        if (d.ValidUntil < d.Date) errors.Add("تاريخ الصلاحية قبل تاريخ العرض.");
        if (d.Items.Count == 0) errors.Add("يجب إدخال صنف واحد على الأقل.");
        if (d.Status.StartsWith("converted")) errors.Add("حالة التحويل تُضبط بعملية التحويل فقط.");
        if (existing != null && existing.Status.StartsWith("converted")) errors.Add("لا يمكن تعديل عرض تم تحويله.");
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
        return Task.CompletedTask;
    }

    protected override async Task OnCreatingAsync(Quotation e, CreateQuotationDto d, CancellationToken ct)
    {
        e.QuotationNumber = await _numbers.NextAsync(e.Type == "sales_quotation" ? "sales_quotation" : "purchase_quotation",
            e.Type == "sales_quotation" ? "QT-" : "PQT-", ct);
        Recalculate(e);
    }

    protected override Task OnUpdatingAsync(Quotation e, UpdateQuotationDto d, CancellationToken ct)
    {
        e.QuotationNumber = Db.Entry(e).OriginalValues.GetValue<string>(nameof(Quotation.QuotationNumber));
        e.ConvertedInvoiceId = Db.Entry(e).OriginalValues.GetValue<Guid?>(nameof(Quotation.ConvertedInvoiceId));
        Recalculate(e);
        return Task.CompletedTask;
    }

    protected override Task OnDeletingAsync(Quotation e, CancellationToken ct)
        => e.Status.StartsWith("converted") ? throw new ConflictException("لا يمكن حذف عرض تم تحويله.") : Task.CompletedTask;

    private static void Recalculate(Quotation q)
    {
        var priced = DocumentPricing.Price(q.Items.Select(i => new PricedLineInput(i.Quantity, i.UnitPrice, i.Discount, i.VatRate)).ToList());
        var items = q.Items.ToList();
        for (var i = 0; i < items.Count; i++)
        {
            items[i].TotalBeforeVat = priced.Lines[i].Net; items[i].VatAmount = priced.Lines[i].VatAmount; items[i].TotalAfterVat = priced.Lines[i].Total;
        }
        q.Subtotal = priced.NetTotal; q.DiscountTotal = priced.DiscountTotal; q.VatTotal = priced.VatTotal; q.GrandTotal = priced.GrandTotal;
    }

    /// <summary>ينشئ فاتورة مسودة (لا أثر مالي) يراجعها المستخدم ويرحّلها بتحديد طريقة الدفع.</summary>
    public async Task<InvoiceDto> ConvertToInvoiceAsync(Guid id, CancellationToken ct = default)
        => await new TransactionRunner(Db).RunAsync(async token =>
        {
            var q = await Db.Set<Quotation>().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException("عرض السعر غير موجود");
            if (q.Status.StartsWith("converted")) throw new ConflictException("تم تحويل هذا العرض مسبقاً.");
            if (q.Status == "rejected") throw new ConflictException("لا يمكن تحويل عرض مرفوض.");

            var sales = q.Type == "sales_quotation";
            var invoice = await _invoices.CreateAsync(new CreateInvoiceDto
            {
                Kind = sales ? InvoiceKind.Sales : InvoiceKind.Purchase,
                InvoiceType = sales ? TradeHelper.InvoiceTypeFor(q.PartyVatNumber) : InvoiceType.TaxInvoice,
                PartyId = q.PartyId, PartyName = q.PartyName, PartyPhone = q.PartyPhone, PartyEmail = q.PartyEmail, PartyVatNumber = q.PartyVatNumber,
                PaymentMethod = q.PartyId.HasValue ? PaymentMethod.Credit : PaymentMethod.Cash, Status = "draft",
                Notes = $"فاتورة محوّلة من عرض سعر {q.QuotationNumber}",
                ReferenceType = "quotation", ReferenceId = q.Id, ReferenceNumber = q.QuotationNumber,
                Items = q.Items.Select(i => new InvoiceItemDto
                {
                    ItemId = i.ItemId, ItemName = i.ItemName, Sku = i.Sku, Unit = i.Unit, Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice, Discount = i.Discount, VatRate = i.VatRate,
                }).ToList(),
            }, token);

            q.ConvertedInvoiceId = invoice.Id;
            q.Status = "converted_to_invoice";
            await Db.SaveChangesAsync(token);
            return invoice;
        }, ct);

    public async Task<CommercialOrderDto> ConvertToOrderAsync(Guid id, CancellationToken ct = default)
        => await new TransactionRunner(Db).RunAsync(async token =>
        {
            var q = await Db.Set<Quotation>().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, token)
                ?? throw new NotFoundException("عرض السعر غير موجود");
            if (q.Status.StartsWith("converted")) throw new ConflictException("تم تحويل هذا العرض مسبقاً.");
            if (q.Status == "rejected") throw new ConflictException("لا يمكن تحويل عرض مرفوض.");

            var order = await _orders.CreateAsync(new CreateCommercialOrderDto
            {
                Type = q.Type == "sales_quotation" ? "sales_order" : "purchase_order",
                PartyId = q.PartyId, PartyName = q.PartyName, PartyPhone = q.PartyPhone, PartyVatNumber = q.PartyVatNumber,
                OrderDate = DateTime.UtcNow, ExpectedDeliveryDate = DateTime.UtcNow.AddDays(7), PaymentTerms = q.PaymentTerms,
                Status = "draft", Notes = $"أمر محوّل من عرض سعر {q.QuotationNumber}",
                Items = q.Items.Select(i => new CommercialOrderItemDto
                {
                    ItemId = i.ItemId, ItemName = i.ItemName, Sku = i.Sku, Unit = i.Unit, Quantity = i.Quantity,
                    UnitPrice = i.UnitPrice, Discount = i.Discount, VatRate = i.VatRate,
                }).ToList(),
            }, token);
            q.Status = "converted_to_order";
            await Db.SaveChangesAsync(token);
            return order;
        }, ct);
}
