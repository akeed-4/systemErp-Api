using Erp.Modules.EInvoicing.Contracts;
using Erp.Modules.Sales.Contracts;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Errors;
using Erp.SharedKernel.Pricing;

namespace Erp.Modules.Sales.Domain;

internal enum SalesInvoiceStatus
{
    Draft,
    Posted,
    Cancelled,
}

internal sealed class SalesInvoice : SalesDocument
{
    private readonly List<SalesInvoiceLine> _lines = [];
    private readonly List<SalesInvoicePayment> _payments = [];

    private SalesInvoice()
    {
    }

    public SalesInvoice(DateOnly issueDate, SettlementType settlement)
    {
        IssueDate = issueDate;
        Settlement = settlement;
        Status = SalesInvoiceStatus.Draft;
    }

    public string? InvoiceNumber { get; private set; }

    public DateOnly IssueDate { get; private set; }

    public DateTimeOffset? IssuedAt { get; private set; }

    public SalesInvoiceType InvoiceType { get; private set; }

    public SettlementType Settlement { get; private set; }

    public string CurrencyCode { get; private set; } = "SAR";

    public decimal ExchangeRate { get; private set; } = 1;

    public Guid WarehouseId { get; private set; }

    public Guid? CostCenterId { get; private set; }

    public Guid? RevenueAccountId { get; private set; }

    /// <summary>The customer's own GL sub-account (credit invoices), snapshotted when the invoice is created.</summary>
    public Guid? ReceivableAccountId { get; private set; }

    public decimal TotalCost { get; private set; }

    public decimal GrossProfit { get; private set; }

    public SalesInvoiceStatus Status { get; private set; }

    public Guid? QuotationId { get; private set; }

    public Guid? SalesOrderId { get; private set; }

    public string? SourceModule { get; private set; }

    public string? SourceDocumentType { get; private set; }

    public Guid? SourceDocumentId { get; private set; }

    public string? SourceDocumentNumber { get; private set; }

    public Guid? JournalEntryId { get; private set; }

    public Guid? EInvoiceDocumentId { get; private set; }

    public Guid? Uuid { get; private set; }

    public string? QrCode { get; private set; }

    public EInvoiceStatus EInvoiceStatus { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    public IReadOnlyList<SalesInvoiceLine> Lines => _lines;

    public IReadOnlyList<SalesInvoicePayment> Payments => _payments;

    public decimal PaidAtIssue => _payments.Sum(p => p.Amount);

    public decimal ReturnedTotal { get; private set; }

    public void SetContext(SalesInvoiceType type, Guid warehouseId, Guid? costCenterId, Guid? revenueAccountId, Guid? quotationId, Guid? orderId, SalesSourceRef? source, string? notes)
    {
        EnsureDraft();
        InvoiceType = type;
        WarehouseId = warehouseId;
        CostCenterId = costCenterId;
        RevenueAccountId = revenueAccountId;
        QuotationId = quotationId;
        SalesOrderId = orderId;
        SourceModule = source?.Module;
        SourceDocumentType = source?.DocumentType;
        SourceDocumentId = source?.DocumentId;
        SourceDocumentNumber = source?.DocumentNumber;
        Notes = notes;
    }

    public void SetLines(IReadOnlyList<LineData> lines, decimal invoiceDiscount)
    {
        EnsureDraft();
        var priced = DocumentPricing.Price(lines.Select(l => new PricedLineInput(l.Quantity, l.UnitPrice, l.Discount, l.VatRate)).ToList(), invoiceDiscount);
        _lines.Clear();
        for (var i = 0; i < lines.Count; i++)
        {
            _lines.Add(new SalesInvoiceLine(Id, i + 1, lines[i], priced.Lines[i]));
        }

        ApplyTotals(priced);
    }

    public void SetReceivable(Guid accountId)
    {
        EnsureDraft();
        ReceivableAccountId = accountId;
    }

    public void AddPayment(Guid paymentMethodId, Guid treasuryAccountId, Guid? bankAccountId, decimal amount, string? reference)
    {
        EnsureDraft();
        _payments.Add(new SalesInvoicePayment(Id, _payments.Count + 1, paymentMethodId, treasuryAccountId, bankAccountId, DocumentPricing.Round(amount), reference));
    }

    public void ApplyCosts(IReadOnlyDictionary<int, decimal> unitCostByLine)
    {
        foreach (var line in _lines)
        {
            line.SetUnitCost(unitCostByLine.GetValueOrDefault(line.LineNo));
        }

        TotalCost = _lines.Sum(l => l.TotalCost);
        GrossProfit = NetTotal - TotalCost;
    }

    public void MarkPosted(string number, DateTimeOffset issuedAt, Guid journalEntryId)
    {
        EnsureDraft();
        InvoiceNumber = number;
        IssuedAt = issuedAt;
        JournalEntryId = journalEntryId;
        Status = SalesInvoiceStatus.Posted;
    }

    public void AttachEInvoice(EInvoiceResult result)
    {
        EInvoiceDocumentId = result.DocumentId;
        Uuid = result.Uuid;
        QrCode = result.QrCode;
        EInvoiceStatus = result.Status;
    }

    public void RegisterReturn(decimal amount) => ReturnedTotal += amount;

    public void Cancel(DateTimeOffset at, string reason)
    {
        if (Status == SalesInvoiceStatus.Cancelled)
        {
            throw ErpException.Conflict("invoice_cancelled", "The invoice is already cancelled.", "الفاتورة ملغاة مسبقاً.");
        }

        if (EInvoiceStatus is EInvoiceStatus.Reported or EInvoiceStatus.Cleared)
        {
            throw ErpException.Conflict("invoice_reported", "A reported/cleared invoice cannot be cancelled; issue a credit note.", "لا يمكن إلغاء فاتورة مُبلّغة للهيئة؛ يرجى إصدار إشعار دائن.");
        }

        if (ReturnedTotal > 0)
        {
            throw ErpException.Conflict("invoice_has_returns", "The invoice has credit notes and cannot be cancelled.", "الفاتورة عليها مرتجعات ولا يمكن إلغاؤها.");
        }

        Status = SalesInvoiceStatus.Cancelled;
        CancelledAt = at;
        CancellationReason = reason;
    }

    public void EnsureDraft()
    {
        if (Status != SalesInvoiceStatus.Draft)
        {
            throw ErpException.Conflict("invoice_not_draft", "Posted invoices cannot be changed.", "لا يمكن تعديل فاتورة مرحّلة.");
        }
    }
}

internal sealed class SalesInvoiceLine : SalesLine
{
    private SalesInvoiceLine()
    {
    }

    public SalesInvoiceLine(Guid invoiceId, int lineNo, LineData data, PricedLine priced)
    {
        InvoiceId = invoiceId;
        Fill(lineNo, data.ProductId, data.Description, data.Unit, data.Quantity, data.UnitPrice, data.CostCenterId, priced);
    }

    public Guid InvoiceId { get; private set; }

    public decimal UnitCost { get; private set; }

    public decimal TotalCost { get; private set; }

    public decimal ReturnedQuantity { get; private set; }

    public decimal ReturnableQuantity => Quantity - ReturnedQuantity;

    public void SetUnitCost(decimal unitCost)
    {
        UnitCost = unitCost;
        TotalCost = DocumentPricing.Round(unitCost * Quantity);
    }

    public void RegisterReturn(decimal quantity)
    {
        if (quantity > ReturnableQuantity)
        {
            throw ErpException.Validation(
                $"Line {LineNo}: only {ReturnableQuantity:0.###} can still be returned.",
                $"السطر {LineNo}: الكمية المتاحة للإرجاع {ReturnableQuantity:0.###} فقط.");
        }

        ReturnedQuantity += quantity;
    }
}

internal sealed class SalesInvoicePayment : TenantEntity
{
    private SalesInvoicePayment()
    {
    }

    public SalesInvoicePayment(Guid invoiceId, int lineNo, Guid paymentMethodId, Guid treasuryAccountId, Guid? bankAccountId, decimal amount, string? reference)
    {
        InvoiceId = invoiceId;
        LineNo = lineNo;
        PaymentMethodId = paymentMethodId;
        TreasuryAccountId = treasuryAccountId;
        BankAccountId = bankAccountId;
        Amount = amount;
        Reference = reference;
    }

    public Guid InvoiceId { get; private set; }

    public int LineNo { get; private set; }

    public Guid PaymentMethodId { get; private set; }

    public Guid TreasuryAccountId { get; private set; }

    public Guid? BankAccountId { get; private set; }

    public decimal Amount { get; private set; }

    public string? Reference { get; private set; }
}

internal enum SalesNoteType
{
    CreditNote,
    DebitNote,
}

/// <summary>sales.SalesNotes: credit notes (returns / price reductions) and debit notes against a posted invoice.</summary>
internal sealed class SalesNote : TenantEntity
{
    private readonly List<SalesNoteLine> _lines = [];

    private SalesNote()
    {
    }

    public SalesNote(string number, SalesNoteType type, SalesInvoice original, DateOnly date, string reason)
    {
        NoteNumber = number;
        NoteType = type;
        OriginalInvoiceId = original.Id;
        OriginalInvoiceNumber = original.InvoiceNumber!;
        CustomerId = original.CustomerId;
        PartyName = original.PartyName;
        Date = date;
        Reason = reason;
    }

    public string NoteNumber { get; private set; } = string.Empty;

    public SalesNoteType NoteType { get; private set; }

    public Guid OriginalInvoiceId { get; private set; }

    public string OriginalInvoiceNumber { get; private set; } = string.Empty;

    public Guid? CustomerId { get; private set; }

    public string PartyName { get; private set; } = string.Empty;

    public DateOnly Date { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public Guid? RefundPaymentMethodId { get; private set; }

    public decimal NetTotal { get; private set; }

    public decimal VatTotal { get; private set; }

    public decimal GrandTotal { get; private set; }

    public decimal TotalCost { get; private set; }

    public Guid? JournalEntryId { get; private set; }

    public Guid? EInvoiceDocumentId { get; private set; }

    public string? QrCode { get; private set; }

    public IReadOnlyList<SalesNoteLine> Lines => _lines;

    public void AddLine(SalesNoteLine line)
    {
        _lines.Add(line);
        NetTotal = _lines.Sum(l => l.NetAmount);
        VatTotal = _lines.Sum(l => l.VatAmount);
        GrandTotal = NetTotal + VatTotal;
        TotalCost = _lines.Sum(l => l.TotalCost);
    }

    public void SetRefund(Guid? paymentMethodId) => RefundPaymentMethodId = paymentMethodId;

    public void MarkPosted(Guid journalEntryId, EInvoiceResult einvoice)
    {
        JournalEntryId = journalEntryId;
        EInvoiceDocumentId = einvoice.DocumentId;
        QrCode = einvoice.QrCode;
    }
}

internal sealed class SalesNoteLine : TenantEntity
{
    private SalesNoteLine()
    {
    }

    public SalesNoteLine(Guid noteId, int lineNo, Guid? originalLineId, Guid? productId, string description, decimal quantity, decimal unitPrice, decimal netAmount, decimal vatRate, decimal vatAmount, decimal unitCost)
    {
        NoteId = noteId;
        LineNo = lineNo;
        OriginalLineId = originalLineId;
        ProductId = productId;
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
        NetAmount = netAmount;
        VatRate = vatRate;
        VatAmount = vatAmount;
        TotalWithVat = netAmount + vatAmount;
        UnitCost = unitCost;
        TotalCost = DocumentPricing.Round(unitCost * quantity);
    }

    public Guid NoteId { get; private set; }

    public int LineNo { get; private set; }

    public Guid? OriginalLineId { get; private set; }

    public Guid? ProductId { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public decimal Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal NetAmount { get; private set; }

    public decimal VatRate { get; private set; }

    public decimal VatAmount { get; private set; }

    public decimal TotalWithVat { get; private set; }

    public decimal UnitCost { get; private set; }

    public decimal TotalCost { get; private set; }
}
