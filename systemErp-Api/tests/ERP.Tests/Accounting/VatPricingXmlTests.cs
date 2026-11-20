using System.Globalization;
using System.Xml.Linq;
using ERP.Core.Contracts.Shared;
using ERP.Core.Models.Shared;
using ERP.Service.Services.Accounting;
using ERP.Service.Services.Shared;

namespace ERP.Tests;

/// <summary>
/// التسعير الشامل وغير الشامل للضريبة من مصدر واحد (<see cref="DocumentPricing"/> و<see cref="VatPricing"/>)، وXML الهيئة الفعلي المبني منه
/// (<see cref="ZatcaInvoiceXml"/>): السعر في PriceAmount صافٍ دائماً، والخصم (AllowanceCharge) يطابق صافي السطر، وإجماليات XML
/// تطابق إجماليات الفاتورة في النظام بما فيها فروق التقريب. بلا قاعدة بيانات ولا اتصال.
/// </summary>
public class VatPricingXmlTests
{
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";

    private static PricedLineInput L(decimal qty, decimal price, decimal discount = 0, decimal rate = 15) => new(qty, price, discount, rate);

    // ---------------- التسعير ----------------

    [Fact]
    public void Inclusive_price_is_split_into_net_and_vat_with_vat_computed_on_the_net()
    {
        var p = DocumentPricing.Price(new[] { L(1, 115) }, 0, InvoicePricingMethod.Inclusive);
        Assert.Equal((100m, 15m, 115m), (p.NetTotal, p.VatTotal, p.GrandTotal));

        // 100 شامل: الصافي 86.96 والضريبة 13.04 = Round(86.96 × 15%)
        p = DocumentPricing.Price(new[] { L(1, 100) }, 0, InvoicePricingMethod.Inclusive);
        Assert.Equal((86.96m, 13.04m, 100m), (p.NetTotal, p.VatTotal, p.GrandTotal));

        // كمية بكسور: 3 × 33.33 = 99.99 شامل
        p = DocumentPricing.Price(new[] { L(3, 33.33m) }, 0, InvoicePricingMethod.Inclusive);
        Assert.Equal((86.95m, 13.04m, 99.99m), (p.NetTotal, p.VatTotal, p.GrandTotal));
        Assert.Equal(86.95m, p.Subtotal); // الإجمالي قبل الخصم صافٍ أيضاً
    }

    [Fact]
    public void Inclusive_line_and_invoice_discounts_are_spread_like_exclusive_and_reported_as_net_equivalents()
    {
        // سطر 2 × 57.50 بخصم 11.50 (= 103.50)، وسطر 46، وخصم فاتورة 14.95 — كلها شاملة
        var p = DocumentPricing.Price(new[] { L(2, 57.5m, 11.5m), L(1, 46) }, 14.95m, InvoicePricingMethod.Inclusive);

        Assert.Equal(new[] { 81.00m, 36.00m }, p.Lines.Select(l => l.Net));
        Assert.Equal(new[] { 12.15m, 5.40m }, p.Lines.Select(l => l.VatAmount));
        Assert.Equal(new[] { 100.00m, 40.00m }, p.Lines.Select(l => l.Gross));
        Assert.Equal(new[] { 10.00m, 0m }, p.Lines.Select(l => l.Discount));
        Assert.Equal(new[] { 9.00m, 4.00m }, p.Lines.Select(l => l.AllocatedInvoiceDiscount));
        Assert.Equal((140m, 10m, 13m, 23m), (p.Subtotal, p.ItemsDiscountTotal, p.InvoiceDiscount, p.DiscountTotal));
        Assert.Equal((117m, 17.55m, 134.55m), (p.NetTotal, p.VatTotal, p.GrandTotal));
        Assert.Equal(149.50m - 14.95m, p.GrandTotal); // يطابق ما أدخله المستخدم
        // كل سطر: الإجمالي قبل الخصم − الخصمان = الصافي
        Assert.All(p.Lines, l => Assert.Equal(l.Net, l.Gross - l.Discount - l.AllocatedInvoiceDiscount));
    }

    [Fact]
    public void Exclusive_pricing_is_unchanged_whether_the_method_is_passed_or_not()
    {
        var lines = new[] { L(2, 50.33m, 0.66m), L(3, 33.33m), L(1, 10, rate: 0) };
        var before = DocumentPricing.Price(lines, 5);
        var now = DocumentPricing.Price(lines, 5, InvoicePricingMethod.Exclusive);
        Assert.Equal(
            (before.Subtotal, before.ItemsDiscountTotal, before.InvoiceDiscount, before.NetTotal, before.VatTotal, before.GrandTotal),
            (now.Subtotal, now.ItemsDiscountTotal, now.InvoiceDiscount, now.NetTotal, now.VatTotal, now.GrandTotal));
        Assert.Equal(before.Lines.Select(l => (l.Net, l.VatAmount, l.Total)), now.Lines.Select(l => (l.Net, l.VatAmount, l.Total)));
        Assert.Equal((100m, 15m), (DocumentPricing.Price(new[] { L(1, 100) }).NetTotal, DocumentPricing.Price(new[] { L(1, 100) }).VatTotal));
    }

    [Fact]
    public void Inclusive_zero_rated_line_is_its_own_net_and_unsupported_lines_are_rejected()
    {
        var p = DocumentPricing.Price(new[] { L(1, 100, rate: 0) }, 0, InvoicePricingMethod.Inclusive);
        Assert.Equal((100m, 0m, 100m), (p.NetTotal, p.VatTotal, p.GrandTotal));

        Assert.Throws<ValidationFailedException>(() => DocumentPricing.Price(
            new[] { new PricedLineInput(1, 100, 0, 15, ExciseTaxRate: 50, ExciseRetailPrice: 100) }, 0, InvoicePricingMethod.Inclusive));
        Assert.Throws<ValidationFailedException>(() => DocumentPricing.Price(
            new[] { new PricedLineInput(1, 100, 0, 0, VatOverride: 3) }, 0, InvoicePricingMethod.Inclusive));
        Assert.Throws<ValidationFailedException>(() => DocumentPricing.Price(new[] { L(1, 100) }, 100.01m, InvoicePricingMethod.Inclusive));
        Assert.Throws<ValidationFailedException>(() => DocumentPricing.Price(new[] { L(1, 100) }, 0, (InvoicePricingMethod)99));
    }

    /// <summary>
    /// فرق التقريب في الشامل: الضريبة دائماً Round(الصافي × النسبة) (قاعدة الهيئة)، فقد يختلف الإجمالي عن المبلغ المُدخل بهللة واحدة
    /// على الأكثر لكل سطر، ولا يزيد. يُفحص على نطاق واسع من المبالغ والنسب.
    /// </summary>
    [Fact]
    public void Inclusive_rounding_difference_is_at_most_one_halala_and_vat_always_matches_the_net()
    {
        var worst = 0m;
        foreach (var rate in new[] { 15m, 5m })
            for (var cents = 1; cents <= 20000; cents += 7)
            {
                var entered = cents / 100m;
                var line = DocumentPricing.Price(new[] { L(1, entered, rate: rate) }, 0, InvoicePricingMethod.Inclusive).Lines[0];
                Assert.Equal(DocumentPricing.Round(line.Net * rate / 100m), line.VatAmount);
                worst = Math.Max(worst, Math.Abs(line.Total - entered));
            }
        Assert.True(worst <= 0.01m, $"أقصى فرق {worst}");
    }

    // ---------------- سعر الوحدة والخصم في XML ----------------

    [Fact]
    public void Zatca_line_price_for_exclusive_is_the_stored_price_and_the_old_allowance()
    {
        Assert.Equal((50.33m, 0.66m), VatPricing.ZatcaLinePrice(InvoicePricingMethod.Exclusive, 2, 50.33m, 15, 100.00m));
        Assert.Equal((115m, 0m), VatPricing.ZatcaLinePrice(InvoicePricingMethod.Exclusive, 1, 115, 15, 115m));
    }

    [Fact]
    public void Zatca_line_price_for_inclusive_is_net_and_quantity_times_price_minus_allowance_equals_the_line_net()
    {
        Assert.Equal((86.96m, 0m), VatPricing.ZatcaLinePrice(InvoicePricingMethod.Inclusive, 1, 100, 15, 86.96m));
        // سطر 2 × 57.50 شامل صافيه بعد الخصمين 81: السعر الصافي 50 والخصم 19 (10 سطر + 9 فاتورة)
        Assert.Equal((50m, 19m), VatPricing.ZatcaLinePrice(InvoicePricingMethod.Inclusive, 2, 57.5m, 15, 81m));

        foreach (var qty in new[] { 1m, 2m, 3m, 7m, 12.5m, 0.333m })
            foreach (var price in new[] { 0.07m, 1.01m, 33.33m, 57.5m, 99.99m, 1234.56m })
                foreach (var rate in new[] { 15m, 5m, 0m })
                    foreach (var discountShare in new[] { 0m, 0.1m, 0.37m })
                    {
                        var inclusive = DocumentPricing.Round(qty * price);
                        var discount = DocumentPricing.Round(inclusive * discountShare);
                        var line = DocumentPricing.Price(new[] { L(qty, price, discount, rate) }, 0, InvoicePricingMethod.Inclusive).Lines[0];
                        var (unit, allowance) = VatPricing.ZatcaLinePrice(InvoicePricingMethod.Inclusive, qty, price, rate, line.Net);
                        Assert.True(allowance >= 0, $"{qty}×{price}@{rate}");
                        Assert.Equal(line.Net, DocumentPricing.Round(qty * unit) - allowance);
                        // صافٍ لا يتجاوز الشامل (المبالغ الصغيرة جداً قد يساوي تقريبُ صافيها المبلغ نفسه)
                        if (rate > 0) Assert.True(unit <= price, $"السعر الشامل {price} أُرسل صافياً {unit}");
                        if (rate > 0 && inclusive >= 1) Assert.True(unit < price, $"السعر الشامل {price} أُرسل على أنه صافٍ");
                        Assert.Equal(line.Discount + line.AllocatedInvoiceDiscount, allowance);
                    }
    }

    // ---------------- XML الفعلي ----------------

    /// <summary>أسطر الهيئة من تسعير النظام كما يبنيها ZatcaService: السعر المحفوظ (المُدخل) وصافي السطر وضريبته المحفوظان.</summary>
    internal static ZatcaLine[] ZatcaLines(IReadOnlyList<PricedLineInput> inputs, PricedDocument priced, InvoicePricingMethod method)
        => inputs.Select((l, i) =>
        {
            var pl = priced.Lines[i];
            var (unit, allowance) = VatPricing.ZatcaLinePrice(method, l.Quantity, l.UnitPrice, l.VatRate, pl.Net);
            return l.VatRate > 0
                ? new ZatcaLine($"صنف {i + 1}", l.Quantity, unit, allowance, pl.Net, 0, l.VatRate, pl.VatAmount, VatCategory.Standard, null, null)
                : new ZatcaLine($"صنف {i + 1}", l.Quantity, unit, allowance, pl.Net, 0, 0, 0, VatCategory.ZeroRated, "VATEX-SA-35", "الأدوية والمعدات الطبية المؤهلة");
        }).ToArray();

    internal static ZatcaDocument Document(string type, IReadOnlyList<ZatcaLine> lines, long counter = 1, bool simplified = false,
        string? previousHash = null, DateTime? issuedAt = null) => new()
    {
        Number = $"T-{counter:0000}", Uuid = Guid.NewGuid(),
        IssueDate = (issuedAt ?? new DateTime(2026, 11, 20, 10, 0, 0)).Date, IssueTime = (issuedAt ?? new DateTime(2026, 11, 20, 10, 0, 0)).ToString("HH:mm:ss", CultureInfo.InvariantCulture),
        TypeCode = type, IsSimplified = simplified, Counter = counter, PreviousHash = previousHash ?? ZatcaInvoiceXml.FirstPreviousHash,
        Seller = new ZatcaParty("شركة اختبار الفوترة", "399999999900003", "1010010000", "طريق الملك فهد", "1234", "العليا", "الرياض", "12345", "SA"),
        Buyer = new ZatcaParty("شركة المشتري", "399999999800003", "1010020000", "شارع التحلية", "4321", "السليمانية", "الرياض", "54321", "SA"),
        BillingReference = type == ZatcaInvoiceXml.Invoice ? null : "T-0001", NoteReason = type == ZatcaInvoiceXml.Invoice ? null : "إرجاع بضاعة",
        Lines = lines,
    };

    private static decimal Dec(XElement? e) => decimal.Parse(e!.Value, CultureInfo.InvariantCulture);

    /// <summary>
    /// XML الهيئة يطابق تسعير النظام: لكل سطر الصافي والضريبة وسعر وحدة صافٍ (لا السعر الشامل) وخصم يحقق
    /// Round(الكمية × السعر) − الخصم = الصافي، ولإجماليات الفاتورة الصافي والضريبة والمستحق، وتفصيل الضريبة لكل تصنيف.
    /// </summary>
    internal static void AssertXmlMatches(string xml, IReadOnlyList<PricedLineInput> inputs, PricedDocument priced, InvoicePricingMethod method)
    {
        var doc = XDocument.Parse(xml);
        var lines = doc.Root!.Elements(Cac + "InvoiceLine").ToList();
        Assert.Equal(priced.Lines.Count, lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var x = lines[i]; var pl = priced.Lines[i]; var input = inputs[i];
            var qty = Dec(x.Element(Cbc + "InvoicedQuantity"));
            var price = Dec(x.Element(Cac + "Price")!.Element(Cbc + "PriceAmount"));
            var allowance = x.Elements(Cac + "AllowanceCharge").Where(a => a.Element(Cbc + "ChargeIndicator")!.Value == "false")
                .Sum(a => Dec(a.Element(Cbc + "Amount")));
            Assert.Equal(pl.Net, Dec(x.Element(Cbc + "LineExtensionAmount")));
            Assert.Equal(pl.VatAmount, Dec(x.Element(Cac + "TaxTotal")!.Element(Cbc + "TaxAmount")));
            Assert.Equal(pl.Total, Dec(x.Element(Cac + "TaxTotal")!.Element(Cbc + "RoundingAmount")));
            Assert.Equal(pl.Net, DocumentPricing.Round(qty * price) - allowance);
            Assert.Equal(pl.Discount + pl.AllocatedInvoiceDiscount, allowance);
            if (method == InvoicePricingMethod.Inclusive && input.VatRate > 0)
                Assert.True(price < input.UnitPrice, $"PriceAmount {price} هو السعر الشامل {input.UnitPrice}");
            else Assert.Equal(input.UnitPrice, price);
        }

        var totals = doc.Root.Element(Cac + "LegalMonetaryTotal")!;
        Assert.Equal(priced.NetTotal, Dec(totals.Element(Cbc + "LineExtensionAmount")));
        Assert.Equal(priced.NetTotal, Dec(totals.Element(Cbc + "TaxExclusiveAmount")));
        Assert.Equal(priced.GrandTotal, Dec(totals.Element(Cbc + "TaxInclusiveAmount")));
        Assert.Equal(priced.GrandTotal, Dec(totals.Element(Cbc + "PayableAmount")));
        Assert.Equal(0m, Dec(totals.Element(Cbc + "AllowanceTotalAmount"))); // خصم الفاتورة موزَّع على الأسطر لا على المستوى العام

        var taxTotals = doc.Root.Elements(Cac + "TaxTotal").ToList();
        Assert.All(taxTotals, t => Assert.Equal(priced.VatTotal, Dec(t.Element(Cbc + "TaxAmount"))));
        var subtotals = taxTotals.SelectMany(t => t.Elements(Cac + "TaxSubtotal")).ToList();
        Assert.Equal(priced.NetTotal, subtotals.Sum(s => Dec(s.Element(Cbc + "TaxableAmount"))));
        Assert.Equal(priced.VatTotal, subtotals.Sum(s => Dec(s.Element(Cbc + "TaxAmount"))));
    }

    public static IEnumerable<object[]> Cases()
    {
        // [الطريقة، نوع المستند، الأسطر، خصم الفاتورة]
        yield return new object[] { InvoicePricingMethod.Inclusive, ZatcaInvoiceXml.Invoice, new[] { L(1, 100) }, 0m };
        yield return new object[] { InvoicePricingMethod.Inclusive, ZatcaInvoiceXml.Invoice, new[] { L(2, 57.5m, 11.5m), L(1, 46) }, 14.95m };
        yield return new object[] { InvoicePricingMethod.Inclusive, ZatcaInvoiceXml.Invoice, new[] { L(3, 33.33m, 0.99m), L(7, 1.01m), L(1, 10, rate: 0) }, 3.33m };
        yield return new object[] { InvoicePricingMethod.Inclusive, ZatcaInvoiceXml.CreditNote, new[] { L(1, 57.5m, 5.75m), L(1, 46) }, 7.47m };
        yield return new object[] { InvoicePricingMethod.Exclusive, ZatcaInvoiceXml.Invoice, new[] { L(1, 100) }, 0m };
        yield return new object[] { InvoicePricingMethod.Exclusive, ZatcaInvoiceXml.Invoice, new[] { L(2, 50.33m, 0.66m), L(3, 33.33m), L(1, 10, rate: 0) }, 5m };
        yield return new object[] { InvoicePricingMethod.Exclusive, ZatcaInvoiceXml.CreditNote, new[] { L(1, 50, 5) }, 2.5m };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Zatca_xml_totals_and_lines_match_the_invoice_in_the_system(InvoicePricingMethod method, string type, PricedLineInput[] inputs, decimal invoiceDiscount)
    {
        var priced = DocumentPricing.Price(inputs, invoiceDiscount, method);
        var xml = ZatcaInvoiceXml.Build(Document(type, ZatcaLines(inputs, priced, method)));
        AssertXmlMatches(xml, inputs, priced, method);
    }

    [Fact]
    public void Same_inclusive_amounts_entered_as_exclusive_nets_give_the_same_xml_totals()
    {
        // فاتورة شاملة، ونظيرتها غير الشاملة بأسعار الصافي نفسها: الإجماليات والضريبة في XML واحدة
        var inclusive = DocumentPricing.Price(new[] { L(2, 115) }, 0, InvoicePricingMethod.Inclusive);
        var exclusive = DocumentPricing.Price(new[] { L(2, 100) });
        Assert.Equal((inclusive.NetTotal, inclusive.VatTotal, inclusive.GrandTotal), (exclusive.NetTotal, exclusive.VatTotal, exclusive.GrandTotal));

        var a = XDocument.Parse(ZatcaInvoiceXml.Build(Document(ZatcaInvoiceXml.Invoice, ZatcaLines(new[] { L(2, 115) }, inclusive, InvoicePricingMethod.Inclusive))));
        var b = XDocument.Parse(ZatcaInvoiceXml.Build(Document(ZatcaInvoiceXml.Invoice, ZatcaLines(new[] { L(2, 100) }, exclusive, InvoicePricingMethod.Exclusive))));
        string Line(XDocument d, string name) => d.Root!.Element(Cac + "InvoiceLine")!.Descendants(Cbc + name).First().Value;
        Assert.Equal(Line(b, "PriceAmount"), Line(a, "PriceAmount"));
        Assert.Equal(Line(b, "LineExtensionAmount"), Line(a, "LineExtensionAmount"));
        Assert.Equal(b.Root!.Element(Cac + "LegalMonetaryTotal")!.ToString(), a.Root!.Element(Cac + "LegalMonetaryTotal")!.ToString());
    }
}
