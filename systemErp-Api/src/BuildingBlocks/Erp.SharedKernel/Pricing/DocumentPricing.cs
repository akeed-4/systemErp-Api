using Erp.SharedKernel.Errors;

namespace Erp.SharedKernel.Pricing;

/// <param name="Discount">Line discount as an amount (SAR), as the frontend sends it.</param>
/// <param name="VatRate">Percentage, e.g. 15 (never 0.15).</param>
public sealed record PricedLineInput(decimal Quantity, decimal UnitPrice, decimal Discount, decimal VatRate);

public sealed record PricedLine(
    decimal Gross,
    decimal Discount,
    decimal AllocatedInvoiceDiscount,
    decimal Net,
    decimal VatRate,
    decimal VatAmount,
    decimal Total);

/// <param name="Subtotal">Gross of all lines (quantity × price) before any discount.</param>
/// <param name="NetTotal">Taxable amount after line and invoice discounts (the frontend's invoice "subtotal").</param>
public sealed record PricedDocument(
    IReadOnlyList<PricedLine> Lines,
    decimal Subtotal,
    decimal ItemsDiscountTotal,
    decimal InvoiceDiscount,
    decimal DiscountTotal,
    decimal NetTotal,
    decimal VatTotal,
    decimal GrandTotal);

/// <summary>
/// Server-side pricing shared by every selling and buying document. Line net = qty × price − line discount; an invoice
/// discount is spread over the lines in proportion to their net (before VAT, so mixed VAT rates stay correct; the last
/// line takes the rounding remainder); VAT per line = net × rate / 100, rounded to halalas.
/// </summary>
public static class DocumentPricing
{
    public static PricedDocument Price(IReadOnlyList<PricedLineInput> lines, decimal invoiceDiscount = 0)
    {
        if (lines.Count == 0)
        {
            throw ErpException.Validation("At least one line is required.", "يجب إدخال سطر واحد على الأقل.");
        }

        var errors = new Dictionary<string, string[]>();
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            if (l.Quantity <= 0 || l.UnitPrice < 0 || l.Discount < 0 || l.Discount > l.Quantity * l.UnitPrice || l.VatRate is < 0 or > 100)
            {
                errors[$"lines[{i}]"] = ["quantity > 0, price ≥ 0, 0 ≤ discount ≤ line amount, VAT rate 0–100"];
            }
        }

        if (errors.Count > 0)
        {
            throw ErpException.Validation("Some lines are not valid.", "بعض أسطر المستند غير صحيحة.", errors);
        }

        var nets = lines.Select(l => Round((l.Quantity * l.UnitPrice) - l.Discount)).ToList();
        var netBeforeInvoiceDiscount = nets.Sum();
        if (invoiceDiscount < 0 || invoiceDiscount > netBeforeInvoiceDiscount)
        {
            throw ErpException.Validation("The invoice discount must be between zero and the lines total.", "خصم الفاتورة يجب أن يكون بين صفر وإجمالي الأسطر.");
        }

        var priced = new List<PricedLine>(lines.Count);
        var allocatedSoFar = 0m;
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var allocation = i == lines.Count - 1
                ? invoiceDiscount - allocatedSoFar
                : netBeforeInvoiceDiscount == 0 ? 0 : Round(invoiceDiscount * nets[i] / netBeforeInvoiceDiscount);
            allocatedSoFar += allocation;

            var net = nets[i] - allocation;
            var vat = Round(net * l.VatRate / 100m);
            priced.Add(new PricedLine(Round(l.Quantity * l.UnitPrice), l.Discount, allocation, net, l.VatRate, vat, net + vat));
        }

        var itemsDiscount = lines.Sum(l => l.Discount);
        var netTotal = priced.Sum(p => p.Net);
        var vatTotal = priced.Sum(p => p.VatAmount);
        return new PricedDocument(priced, priced.Sum(p => p.Gross), itemsDiscount, invoiceDiscount, itemsDiscount + invoiceDiscount, netTotal, vatTotal, netTotal + vatTotal);
    }

    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
