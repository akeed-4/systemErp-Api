using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.Accounting.Contracts;
using Erp.Modules.EInvoicing.Contracts;
using Erp.Modules.Inventory.Contracts;
using Erp.Modules.Payments.Contracts;
using Erp.Modules.Sales.Contracts;
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Persistence;
using Erp.Modules.Settings.Contracts;
using Erp.SharedKernel.Errors;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Sales.Application;

/// <summary>
/// General sales invoices. Posting (one transaction): stock issue at moving-average cost → journal entry
/// (Dr cash/card/bank per payment row, or Dr the customer on credit; Cr revenue per cost center; Cr output VAT)
/// → COGS entry (Dr COGS / Cr inventory) → ZATCA chain + QR.
/// </summary>
internal sealed class SalesInvoiceService(
    SalesDbContext db,
    IUnitOfWork unitOfWork,
    SalesInputs inputs,
    INumberSequenceService numbers,
    IInventoryService inventory,
    IWarehouseDirectory warehouses,
    IPaymentMethodDirectory paymentMethods,
    IAccountingPostingService posting,
    IAccountBalanceQueries balances,
    IEInvoicingService einvoicing,
    IVoucherService vouchers,
    TimeProvider clock) : ISalesInvoiceService
{
    public const string Module = "sales";
    public const string DocumentType = "sales_invoice";

    public async Task<SalesInvoiceResult> CreateAsync(CreateSalesInvoiceCommand command, CancellationToken cancellationToken)
    {
        var invoice = await unitOfWork.ExecuteAsync(
            async ct =>
            {
                var party = await inputs.ResolvePartyAsync(command.CustomerId, command.BuyerName, ct);
                var type = command.InvoiceType ?? (party.VatNumber is null ? SalesInvoiceType.Simplified : SalesInvoiceType.TaxInvoice);
                if (type == SalesInvoiceType.TaxInvoice && party.VatNumber is null)
                {
                    throw ErpException.Validation("A tax invoice needs a customer with a VAT number.", "الفاتورة الضريبية تتطلب عميلاً له رقم ضريبي.");
                }

                var created = new SalesInvoice(command.Date ?? Today(), command.Settlement);
                created.SetParty(party.CustomerId, party.Name, party.VatNumber, party.CrNumber, party.Phone, party.Email, party.Address);
                var warehouseId = command.WarehouseId ?? (await warehouses.GetDefaultAsync(ct)).Id;
                created.SetContext(type, warehouseId, command.CostCenterId, command.RevenueAccountId, command.QuotationId, command.SalesOrderId, command.Source, command.Notes);
                created.SetLines(await inputs.ResolveLinesAsync(command.Lines, ct), command.InvoiceDiscount);

                await ApplySettlementAsync(created, party, command.Payments ?? [], ct);
                db.Invoices.Add(created);

                if (command.Post)
                {
                    await PostAsync(created, ct);
                }

                return created;
            },
            cancellationToken);

        return ToResult(invoice);
    }

    public async Task<SalesInvoiceResult> PostDraftAsync(Guid id, CancellationToken ct)
    {
        var invoice = await unitOfWork.ExecuteAsync(
            async innerCt =>
            {
                var draft = await LoadAsync(id, innerCt);
                await PostAsync(draft, innerCt);
                return draft;
            },
            ct);
        return ToResult(invoice);
    }

    public async Task CancelAsync(Guid id, string? reason, CancellationToken ct)
    {
        await unitOfWork.ExecuteAsync(
            async innerCt =>
            {
                var invoice = await LoadAsync(id, innerCt);
                var wasPosted = invoice.Status == SalesInvoiceStatus.Posted;
                if (wasPosted && await vouchers.GetAllocatedAmountAsync(Module, DocumentType, invoice.Id, innerCt) > 0)
                {
                    throw ErpException.Conflict("invoice_has_receipts", "Receipts are allocated to this invoice; cancel them first.", "توجد سندات قبض مخصصة لهذه الفاتورة؛ يرجى إلغاؤها أولاً.");
                }

                invoice.Cancel(clock.GetUtcNow(), reason ?? "إلغاء الفاتورة");
                if (!wasPosted)
                {
                    return;
                }

                var today = Today();
                var source = Source(invoice);
                await posting.ReverseAsync(source, "invoice", today, reason ?? "إلغاء الفاتورة", innerCt);
                if (invoice.TotalCost > 0)
                {
                    await posting.ReverseAsync(source, "cogs", today, reason ?? "إلغاء الفاتورة", innerCt);
                }

                await inventory.ReverseAsync(StockDocument(invoice), innerCt);
            },
            ct);
    }

    public async Task<decimal> PaidAmountAsync(SalesInvoice invoice, CancellationToken ct) =>
        invoice.PaidAtIssue + await vouchers.GetAllocatedAmountAsync(Module, DocumentType, invoice.Id, ct);

    private async Task ApplySettlementAsync(SalesInvoice invoice, PartySnapshot party, IReadOnlyList<SalesPaymentInput> payments, CancellationToken ct)
    {
        if (invoice.Settlement == SettlementType.Credit)
        {
            if (party.AccountId is not { } receivable)
            {
                throw ErpException.Validation("A credit invoice needs a registered customer.", "الفاتورة الآجلة تتطلب عميلاً مسجلاً في دليل العملاء.");
            }

            invoice.SetReceivable(receivable);
            if (party.CreditLimit > 0)
            {
                var balance = await balances.GetBalanceAsync(party.AccountId.Value, null, ct);
                if (balance + invoice.GrandTotal > party.CreditLimit)
                {
                    throw ErpException.Conflict(
                        "credit_limit_exceeded",
                        $"The customer's credit limit ({party.CreditLimit:0.00}) would be exceeded (balance {balance:0.00}).",
                        $"تتجاوز الفاتورة الحد الائتماني للعميل ({party.CreditLimit:0.00})؛ الرصيد الحالي {balance:0.00}.");
                }
            }

            return;
        }

        if (payments.Count == 0)
        {
            throw ErpException.Validation("A cash invoice needs at least one payment.", "الفاتورة النقدية تتطلب تحديد طريقة الدفع.");
        }

        foreach (var payment in payments)
        {
            var treasury = await paymentMethods.ResolveTreasuryAccountAsync(payment.PaymentMethodId, payment.BankAccountId, ct);
            invoice.AddPayment(payment.PaymentMethodId, treasury, payment.BankAccountId, payment.Amount, payment.Reference);
        }

        if (invoice.PaidAtIssue != invoice.GrandTotal)
        {
            throw ErpException.Validation(
                $"Payments ({invoice.PaidAtIssue:0.00}) must equal the invoice total ({invoice.GrandTotal:0.00}).",
                $"مجموع الدفعات ({invoice.PaidAtIssue:0.00}) يجب أن يساوي إجمالي الفاتورة ({invoice.GrandTotal:0.00}).");
        }
    }

    private async Task PostAsync(SalesInvoice invoice, CancellationToken ct)
    {
        invoice.EnsureDraft();
        var number = await numbers.NextAsync("inv", invoice.IssueDate, null, ct);
        var issuedAt = clock.GetUtcNow();

        var stockLines = invoice.Lines.Where(l => l.ProductId is not null).ToList();
        if (stockLines.Count > 0)
        {
            var issued = await inventory.IssueAsync(
                StockDocument(invoice, number),
                StockMovementType.OutSales,
                stockLines.Select(l => new StockLine(l.ProductId!.Value, invoice.WarehouseId, l.Quantity, UnitPrice: l.NetAmount / l.Quantity)).ToList(),
                ct);
            invoice.ApplyCosts(stockLines.Zip(issued).ToDictionary(p => p.First.LineNo, p => p.Second.UnitCost));
        }

        var source = new SourceRef(Module, DocumentType, invoice.Id, number);
        var entry = await posting.PostAsync(new PostingRequest(source, "invoice", invoice.IssueDate, $"فاتورة مبيعات {number} - {invoice.PartyName}", RevenueLines(invoice)), ct);

        if (invoice.TotalCost > 0)
        {
            await posting.PostAsync(
                new PostingRequest(source, "cogs", invoice.IssueDate, $"تكلفة مبيعات الفاتورة {number}",
                [
                    new PostingLine(AccountRef.For(PostingPurpose.CostOfGoodsSold), invoice.TotalCost, 0, invoice.CostCenterId),
                    new PostingLine(AccountRef.For(PostingPurpose.Inventory), 0, invoice.TotalCost),
                ]),
                ct);
        }

        invoice.MarkPosted(number, issuedAt, entry.JournalEntryId);
        invoice.AttachEInvoice(await einvoicing.RegisterAsync(
            new EInvoiceRequest(Module, DocumentType, invoice.Id, number,
                invoice.InvoiceType == SalesInvoiceType.TaxInvoice ? EInvoiceKind.StandardTaxInvoice : EInvoiceKind.SimplifiedTaxInvoice,
                issuedAt, invoice.GrandTotal, invoice.VatTotal, invoice.PartyName, invoice.PartyVatNumber),
            ct));
    }

    private static List<PostingLine> RevenueLines(SalesInvoice invoice)
    {
        var party = invoice.CustomerId is { } customerId ? new PartyRef(PartyType.Customer, customerId) : null;
        var lines = new List<PostingLine>();

        if (invoice.Settlement == SettlementType.Credit)
        {
            lines.Add(new PostingLine(AccountRef.ById(invoice.ReceivableAccountId!.Value), invoice.GrandTotal, 0, invoice.CostCenterId, party));
        }
        else
        {
            lines.AddRange(invoice.Payments.Select(p => new PostingLine(AccountRef.ById(p.TreasuryAccountId), p.Amount, 0, invoice.CostCenterId, party, p.Reference)));
        }

        var revenue = invoice.RevenueAccountId is { } revenueAccount ? AccountRef.ById(revenueAccount) : AccountRef.For(PostingPurpose.SalesRevenue);
        lines.AddRange(invoice.Lines
            .GroupBy(l => l.CostCenterId ?? invoice.CostCenterId)
            .Select(g => new PostingLine(revenue, 0, g.Sum(l => l.NetAmount), g.Key)));

        if (invoice.VatTotal > 0)
        {
            lines.Add(new PostingLine(AccountRef.For(PostingPurpose.OutputVat), 0, invoice.VatTotal));
        }

        return lines.Where(l => l.Debit + l.Credit > 0).ToList();
    }

    private async Task<SalesInvoice> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Invoices.Include(i => i.Lines).Include(i => i.Payments).SingleOrDefaultAsync(i => i.Id == id, ct)
        ?? throw ErpException.NotFound("Sales invoice", "فاتورة المبيعات");

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    private static SourceRef Source(SalesInvoice invoice) => new(Module, DocumentType, invoice.Id, invoice.InvoiceNumber ?? string.Empty);

    private static StockDocument StockDocument(SalesInvoice invoice, string? number = null) =>
        new(Module, DocumentType, invoice.Id, number ?? invoice.InvoiceNumber ?? string.Empty, invoice.IssueDate);

    private static SalesInvoiceResult ToResult(SalesInvoice i) =>
        new(i.Id, i.InvoiceNumber, i.NetTotal, i.VatTotal, i.GrandTotal, i.JournalEntryId, i.QrCode);
}
