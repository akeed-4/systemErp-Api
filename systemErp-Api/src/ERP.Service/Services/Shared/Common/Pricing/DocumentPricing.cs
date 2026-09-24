using ERP.Core.Contracts.Shared;

namespace ERP.Service.Services.Shared;

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
            throw new ValidationFailedException("يجب إدخال سطر واحد على الأقل.");
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
            throw new ValidationFailedException("بعض أسطر المستند غير صحيحة.", errors.Values.SelectMany(v => v));
        }

        var nets = lines.Select(l => Round((l.Quantity * l.UnitPrice) - l.Discount)).ToList();
        var netBeforeInvoiceDiscount = nets.Sum();
        if (invoiceDiscount < 0 || invoiceDiscount > netBeforeInvoiceDiscount)
        {
            throw new ValidationFailedException("خصم الفاتورة يجب أن يكون بين صفر وإجمالي الأسطر.");
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
            var vat = l.VatOverride.HasValue ? Round(l.VatOverride.Value) : Round(net * l.VatRate / 100m);
            priced.Add(new PricedLine(Round(l.Quantity * l.UnitPrice), l.Discount, allocation, net, l.VatRate, vat, net + vat));
        }

        var itemsDiscount = lines.Sum(l => l.Discount);
        var netTotal = priced.Sum(p => p.Net);
        var vatTotal = priced.Sum(p => p.VatAmount);
        return new PricedDocument(priced, priced.Sum(p => p.Gross), itemsDiscount, invoiceDiscount, itemsDiscount + invoiceDiscount, netTotal, vatTotal, netTotal + vatTotal);
    }

    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
