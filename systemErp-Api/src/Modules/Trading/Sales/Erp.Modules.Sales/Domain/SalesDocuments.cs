using Erp.Modules.Sales.Contracts;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Errors;
using Erp.SharedKernel.Pricing;

namespace Erp.Modules.Sales.Domain;

/// <summary>Buyer snapshot and totals shared by every sales document. The snapshot is the legal record; CustomerId is the link.</summary>
internal abstract class SalesDocument : TenantEntity
{
    public Guid? CustomerId { get; protected set; }

    public string PartyName { get; protected set; } = string.Empty;

    public string? PartyVatNumber { get; protected set; }

    public string? PartyCrNumber { get; protected set; }

    public string? PartyPhone { get; protected set; }

    public string? PartyEmail { get; protected set; }

    public string? PartyAddress { get; protected set; }

    /// <summary>Gross of all lines (quantity × price) before discounts.</summary>
    public decimal GrossTotal { get; protected set; }

    public decimal ItemsDiscountTotal { get; protected set; }

    public decimal InvoiceDiscount { get; protected set; }

    public decimal DiscountTotal { get; protected set; }

    /// <summary>Taxable amount (the frontend's "subtotal").</summary>
    public decimal NetTotal { get; protected set; }

    public decimal VatTotal { get; protected set; }

    public decimal GrandTotal { get; protected set; }

    public string? Notes { get; protected set; }

    public byte[] RowVersion { get; private set; } = [];

    public void SetParty(Guid? customerId, string name, string? vatNumber, string? crNumber, string? phone, string? email, string? address)
    {
        CustomerId = customerId;
        PartyName = name.Trim();
        PartyVatNumber = vatNumber;
        PartyCrNumber = crNumber;
        PartyPhone = phone;
        PartyEmail = email;
        PartyAddress = address;
    }

    protected void ApplyTotals(PricedDocument priced)
    {
        GrossTotal = priced.Subtotal;
        ItemsDiscountTotal = priced.ItemsDiscountTotal;
        InvoiceDiscount = priced.InvoiceDiscount;
        DiscountTotal = priced.DiscountTotal;
        NetTotal = priced.NetTotal;
        VatTotal = priced.VatTotal;
        GrandTotal = priced.GrandTotal;
    }
}

/// <summary>A priced document line (product line, or a service/description-only line when ProductId is null).</summary>
internal abstract class SalesLine : TenantEntity
{
    public int LineNo { get; protected set; }

    public Guid? ProductId { get; protected set; }

    public string Description { get; protected set; } = string.Empty;

    public string? Unit { get; protected set; }

    public decimal Quantity { get; protected set; }

    public decimal UnitPrice { get; protected set; }

    public decimal Discount { get; protected set; }

    public decimal AllocatedInvoiceDiscount { get; protected set; }

    public decimal VatRate { get; protected set; }

    public decimal NetAmount { get; protected set; }

    public decimal VatAmount { get; protected set; }

    public decimal TotalWithVat { get; protected set; }

    public Guid? CostCenterId { get; protected set; }

    protected void Fill(int lineNo, Guid? productId, string description, string? unit, decimal quantity, decimal unitPrice, Guid? costCenterId, PricedLine priced)
    {
        LineNo = lineNo;
        ProductId = productId;
        Description = description;
        Unit = unit;
        Quantity = quantity;
        UnitPrice = unitPrice;
        Discount = priced.Discount;
        AllocatedInvoiceDiscount = priced.AllocatedInvoiceDiscount;
        VatRate = priced.VatRate;
        NetAmount = priced.Net;
        VatAmount = priced.VatAmount;
        TotalWithVat = priced.Total;
        CostCenterId = costCenterId;
    }
}

internal sealed record LineData(Guid? ProductId, string Description, string? Unit, decimal Quantity, decimal UnitPrice, decimal Discount, decimal VatRate, Guid? CostCenterId);

internal enum QuotationStatus
{
    Draft,
    Sent,
    Accepted,
    Rejected,
    Expired,
    ConvertedToInvoice,
    ConvertedToOrder,
}

internal sealed class SalesQuotation : SalesDocument
{
    private readonly List<SalesQuotationLine> _lines = [];

    private SalesQuotation()
    {
    }

    public SalesQuotation(string number, DateOnly date, DateOnly validUntil)
    {
        QuotationNumber = number;
        Date = date;
        ValidUntil = validUntil;
        Status = QuotationStatus.Sent;
    }

    public string QuotationNumber { get; private set; } = string.Empty;

    public DateOnly Date { get; private set; }

    public DateOnly ValidUntil { get; private set; }

    public string? PaymentTerms { get; private set; }

    public string? TermsAndConditions { get; private set; }

    public QuotationStatus Status { get; private set; }

    public Guid? ConvertedInvoiceId { get; private set; }

    public Guid? ConvertedOrderId { get; private set; }

    public IReadOnlyList<SalesQuotationLine> Lines => _lines;

    public void SetLines(IReadOnlyList<LineData> lines, decimal invoiceDiscount)
    {
        EnsureOpen();
        var priced = DocumentPricing.Price(lines.Select(l => new PricedLineInput(l.Quantity, l.UnitPrice, l.Discount, l.VatRate)).ToList(), invoiceDiscount);
        _lines.Clear();
        for (var i = 0; i < lines.Count; i++)
        {
            _lines.Add(new SalesQuotationLine(Id, i + 1, lines[i], priced.Lines[i]));
        }

        ApplyTotals(priced);
    }

    public void SetTerms(DateOnly validUntil, string? paymentTerms, string? terms, string? notes)
    {
        ValidUntil = validUntil;
        PaymentTerms = paymentTerms;
        TermsAndConditions = terms;
        Notes = notes;
    }

    public void ChangeStatus(QuotationStatus status)
    {
        EnsureOpen();
        Status = status;
    }

    public void MarkConvertedToInvoice(Guid invoiceId)
    {
        EnsureOpen();
        Status = QuotationStatus.ConvertedToInvoice;
        ConvertedInvoiceId = invoiceId;
    }

    public void MarkConvertedToOrder(Guid orderId)
    {
        EnsureOpen();
        Status = QuotationStatus.ConvertedToOrder;
        ConvertedOrderId = orderId;
    }

    private void EnsureOpen()
    {
        if (Status is QuotationStatus.ConvertedToInvoice or QuotationStatus.ConvertedToOrder)
        {
            throw ErpException.Conflict("quotation_converted", "The quotation was already converted.", "تم تحويل عرض السعر مسبقاً.");
        }
    }
}

internal sealed class SalesQuotationLine : SalesLine
{
    private SalesQuotationLine()
    {
    }

    public SalesQuotationLine(Guid quotationId, int lineNo, LineData data, PricedLine priced)
    {
        QuotationId = quotationId;
        Fill(lineNo, data.ProductId, data.Description, data.Unit, data.Quantity, data.UnitPrice, data.CostCenterId, priced);
    }

    public Guid QuotationId { get; private set; }
}

internal enum SalesOrderStatus
{
    Draft,
    Confirmed,
    PartiallyFulfilled,
    Completed,
    Cancelled,
}

internal sealed class SalesOrder : SalesDocument
{
    private readonly List<SalesOrderLine> _lines = [];

    private SalesOrder()
    {
    }

    public SalesOrder(string number, DateOnly orderDate)
    {
        OrderNumber = number;
        OrderDate = orderDate;
        Status = SalesOrderStatus.Draft;
    }

    public string OrderNumber { get; private set; } = string.Empty;

    public DateOnly OrderDate { get; private set; }

    public DateOnly? ExpectedDeliveryDate { get; private set; }

    public string? PaymentTerms { get; private set; }

    /// <summary>Framework agreement this order draws on (Contracts module; opaque id, §11.2).</summary>
    public Guid? AgreementId { get; private set; }

    public Guid? QuotationId { get; private set; }

    public SalesOrderStatus Status { get; private set; }

    public Guid? ConvertedInvoiceId { get; private set; }

    public IReadOnlyList<SalesOrderLine> Lines => _lines;

    public void SetDetails(DateOnly? expectedDeliveryDate, string? paymentTerms, Guid? agreementId, Guid? quotationId, string? notes)
    {
        ExpectedDeliveryDate = expectedDeliveryDate;
        PaymentTerms = paymentTerms;
        AgreementId = agreementId;
        QuotationId = quotationId;
        Notes = notes;
    }

    public void SetLines(IReadOnlyList<LineData> lines, decimal invoiceDiscount)
    {
        if (Status != SalesOrderStatus.Draft)
        {
            throw ErpException.Conflict("order_not_draft", "Only draft orders can be changed.", "لا يمكن تعديل إلا أوامر البيع المسودة.");
        }

        var priced = DocumentPricing.Price(lines.Select(l => new PricedLineInput(l.Quantity, l.UnitPrice, l.Discount, l.VatRate)).ToList(), invoiceDiscount);
        _lines.Clear();
        for (var i = 0; i < lines.Count; i++)
        {
            _lines.Add(new SalesOrderLine(Id, i + 1, lines[i], priced.Lines[i]));
        }

        ApplyTotals(priced);
    }

    public void Confirm()
    {
        if (Status != SalesOrderStatus.Draft)
        {
            throw ErpException.Conflict("order_not_draft", "Only draft orders can be confirmed.", "لا يمكن اعتماد إلا أوامر البيع المسودة.");
        }

        Status = SalesOrderStatus.Confirmed;
    }

    public void Cancel()
    {
        if (Status is SalesOrderStatus.Completed or SalesOrderStatus.Cancelled)
        {
            throw ErpException.Conflict("order_closed", "The order is already closed.", "أمر البيع مغلق مسبقاً.");
        }

        Status = SalesOrderStatus.Cancelled;
    }

    public void MarkInvoiced(Guid invoiceId)
    {
        if (Status is not (SalesOrderStatus.Confirmed or SalesOrderStatus.PartiallyFulfilled))
        {
            throw ErpException.Conflict("order_not_confirmed", "Only confirmed orders can be invoiced.", "لا يمكن فوترة إلا أوامر البيع المعتمدة.");
        }

        Status = SalesOrderStatus.Completed;
        ConvertedInvoiceId = invoiceId;
    }
}

internal sealed class SalesOrderLine : SalesLine
{
    private SalesOrderLine()
    {
    }

    public SalesOrderLine(Guid orderId, int lineNo, LineData data, PricedLine priced)
    {
        OrderId = orderId;
        Fill(lineNo, data.ProductId, data.Description, data.Unit, data.Quantity, data.UnitPrice, data.CostCenterId, priced);
    }

    public Guid OrderId { get; private set; }
}
