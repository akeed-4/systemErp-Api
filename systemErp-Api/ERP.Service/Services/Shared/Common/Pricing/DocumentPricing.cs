using System.Globalization;
using ERP.Core.Contracts.Shared;

namespace ERP.Service.Services.Shared;

/// <summary>
/// Server-side pricing shared by every selling and buying document. Line net = qty × price − line discount; an invoice
/// discount is spread over the lines in proportion to their net (before VAT, so mixed VAT rates stay correct; the last
/// line takes the rounding remainder); VAT per line = net × rate / 100, rounded to halalas.
/// </summary>
/// <remarks>
/// <see cref="InvoicePricingMethod.Inclusive"/>: the price, line discount and invoice discount include VAT. The invoice discount is
/// spread over the lines' VAT-inclusive amounts, each line's net = Round(inclusive × 100 / (100 + rate)) and its VAT is computed on
/// that net exactly as above, so a line's VAT always equals Round(net × rate) (ZATCA) and may leave a halala difference against the
/// entered inclusive amount. Every returned figure (gross, discounts, net, VAT) is VAT-exclusive in both methods.
/// </remarks>
public static class DocumentPricing
{
    public static PricedDocument Price(IReadOnlyList<PricedLineInput> lines, decimal invoiceDiscount = 0,
        InvoicePricingMethod pricingMethod = InvoicePricingMethod.Exclusive)
    {
        if (lines.Count == 0)
        {
            throw new ValidationFailedException(Messages.AtLeastOneLineRequiredDocument);
        }

        var errors = new Dictionary<string, string[]>();
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            if (l.Quantity <= 0 || l.UnitPrice < 0 || l.Discount < 0 || l.Discount > l.Quantity * l.UnitPrice
                || l.VatRate is < 0 or > 100 || l.ExciseTaxRate is < 0 or > 100
                || l.ExciseRetailPrice < 0 || l.ExciseStandardPrice < 0
                || (l.ExciseTaxRate > 0 && l.ExciseRetailPrice <= 0 && l.ExciseStandardPrice <= 0))
            {
                errors[$"lines[{i}]"] = [T(
                    "الكمية أكبر من صفر، السعر ≥ 0، الخصم بين صفر وقيمة السطر، ونسبة الضريبة بين 0 و100",
                    "quantity > 0, price ≥ 0, 0 ≤ discount ≤ line amount, VAT and excise rates 0–100, and a reference excise price when excise applies")];
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(Messages.SomeDocumentLinesInvalid, errors.Values.SelectMany(v => v));
        }

        if (pricingMethod == InvoicePricingMethod.Inclusive) return PriceInclusive(lines, invoiceDiscount);
        if (pricingMethod != InvoicePricingMethod.Exclusive) throw new ValidationFailedException(Messages.InvoicePricingMethodInvalid);

        var nets = lines.Select(l => Round((l.Quantity * l.UnitPrice) - l.Discount)).ToList();
        var netBeforeInvoiceDiscount = nets.Sum();
        if (invoiceDiscount < 0 || invoiceDiscount > netBeforeInvoiceDiscount)
        {
            throw new ValidationFailedException(Messages.InvoiceDiscountRange);
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
            var exciseReferencePrice = Math.Max(l.ExciseRetailPrice, l.ExciseStandardPrice);
            var exciseBase = l.ExciseTaxRate > 0 ? Round(l.Quantity * exciseReferencePrice) : 0m;
            var excise = Round(exciseBase * l.ExciseTaxRate / 100m);
            // الانتقائية ضمن قيمة التوريد الخاضعة لضريبة القيمة المضافة.
            var vat = l.VatOverride.HasValue ? Round(l.VatOverride.Value) : Round((net + excise) * l.VatRate / 100m);
            priced.Add(new PricedLine(Round(l.Quantity * l.UnitPrice), l.Discount, allocation, net,
                l.ExciseTaxRate, l.ExciseRetailPrice, l.ExciseStandardPrice, exciseBase, excise, l.VatRate, vat, net + excise + vat));
        }

        var itemsDiscount = lines.Sum(l => l.Discount);
        var netTotal = priced.Sum(p => p.Net);
        var exciseTotal = priced.Sum(p => p.ExciseTaxAmount);
        var vatTotal = priced.Sum(p => p.VatAmount);
        return new PricedDocument(priced, priced.Sum(p => p.Gross), itemsDiscount, invoiceDiscount, itemsDiscount + invoiceDiscount,
            netTotal, exciseTotal, vatTotal, netTotal + exciseTotal + vatTotal);
    }

    /// <summary>
    /// التسعير الشامل: المبالغ المدخلة شاملة الضريبة. خصم الفاتورة يوزَّع على مبالغ الأسطر الشاملة (والكسر على آخر سطر)، ثم
    /// يُستخرج صافي كل سطر (<see cref="VatPricing.NetOf"/>) وتُحسب ضريبته عليه بالمعادلة نفسها في الطريقة غير الشاملة.
    /// الخصمان يُعادان بمقابلهما الصافي: خصم السطر = صافي قبل الخصم − صافي بعد خصم السطر، وحصة خصم الفاتورة = الباقي حتى صافي السطر.
    /// السطر بضريبة انتقائية أو بمبلغ ضريبة ثابت (هامش الربح) لا يُسعَّر شاملاً.
    /// </summary>
    private static PricedDocument PriceInclusive(IReadOnlyList<PricedLineInput> lines, decimal invoiceDiscount)
    {
        if (lines.Any(l => l.ExciseTaxRate > 0 || l.VatOverride.HasValue))
        {
            throw new ValidationFailedException(Messages.VatInclusivePricingUnsupportedLine);
        }

        var inclusiveAfterLineDiscount = lines.Select(l => Round((l.Quantity * l.UnitPrice) - l.Discount)).ToList();
        var inclusiveTotal = inclusiveAfterLineDiscount.Sum();
        if (invoiceDiscount < 0 || invoiceDiscount > inclusiveTotal)
        {
            throw new ValidationFailedException(Messages.InvoiceDiscountRange);
        }

        var priced = new List<PricedLine>(lines.Count);
        var allocatedSoFar = 0m;
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            var allocation = i == lines.Count - 1
                ? invoiceDiscount - allocatedSoFar
                : inclusiveTotal == 0 ? 0 : Round(invoiceDiscount * inclusiveAfterLineDiscount[i] / inclusiveTotal);
            allocatedSoFar += allocation;

            var grossNet = VatPricing.NetOf(Round(l.Quantity * l.UnitPrice), l.VatRate);
            var afterLineDiscountNet = VatPricing.NetOf(inclusiveAfterLineDiscount[i], l.VatRate);
            var net = VatPricing.NetOf(inclusiveAfterLineDiscount[i] - allocation, l.VatRate);
            var vat = Round(net * l.VatRate / 100m);
            priced.Add(new PricedLine(grossNet, grossNet - afterLineDiscountNet, afterLineDiscountNet - net, net,
                0m, 0m, 0m, 0m, 0m, l.VatRate, vat, net + vat));
        }

        var itemsDiscount = priced.Sum(p => p.Discount);
        var invoiceDiscountNet = priced.Sum(p => p.AllocatedInvoiceDiscount);
        var netTotal = priced.Sum(p => p.Net);
        var vatTotal = priced.Sum(p => p.VatAmount);
        return new PricedDocument(priced, priced.Sum(p => p.Gross), itemsDiscount, invoiceDiscountNet, itemsDiscount + invoiceDiscountNet,
            netTotal, 0m, vatTotal, netTotal + vatTotal);
    }

    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>يختار النص حسب لغة الطلب الحالية (Accept-Language عبر RequestLocalizationMiddleware)؛ الإنجليزية فقط صراحةً، وإلا فالعربية.</summary>
    private static string T(string ar, string en)
        => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase) ? en : ar;
}
