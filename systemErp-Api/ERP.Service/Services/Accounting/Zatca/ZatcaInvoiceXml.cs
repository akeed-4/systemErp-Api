using System.Globalization;
using System.Text;
using System.Xml;

namespace ERP.Service.Services.Accounting;

/// <summary>طرف الفاتورة (البائع أو المشتري) بعنوانه الوطني.</summary>
public sealed record ZatcaParty(
    string Name, string? VatNumber, string? CrNumber, string? Street, string? BuildingNumber, string? District,
    string? City, string? PostalCode, string CountryCode, string? IdType = null, string? IdNumber = null);

/// <summary>سطر فاتورة: الصافي بعد كل الخصومات وقبل الضريبة، والخصم هو الفرق بين (الكمية × السعر) والصافي.</summary>
public sealed record ZatcaLine(
    string Name, decimal Quantity, decimal UnitPrice, decimal Allowance, decimal Net, decimal ExciseTaxAmount, decimal VatRate, decimal VatAmount,
    VatCategory Category, string? ExemptionCode, string? ExemptionReason);

/// <summary>الفاتورة كما تُبنى منها وثيقة UBL: مستقلة عن قاعدة البيانات لتُبنى منها أيضاً فواتير فحص الامتثال.</summary>
public sealed class ZatcaDocument
{
    public string Number { get; init; } = string.Empty;
    public Guid Uuid { get; init; }
    public DateTime IssueDate { get; init; }
    /// <summary>HH:mm:ss بتوقيت المملكة.</summary>
    public string IssueTime { get; init; } = "00:00:00";
    /// <summary>388 فاتورة، 381 إشعار دائن، 383 إشعار مدين.</summary>
    public string TypeCode { get; init; } = ZatcaInvoiceXml.Invoice;
    /// <summary>مبسطة (B2C) تُبلَّغ خلال 24 ساعة، أو ضريبية (B2B) تُعتمد قبل تسليمها للمشتري.</summary>
    public bool IsSimplified { get; init; }
    public string Currency { get; init; } = "SAR";
    /// <summary>سعر تحويل عملة الفاتورة إلى الريال (الضريبة تُذكر بالريال دائماً).</summary>
    public decimal ExchangeRate { get; init; } = 1;
    public ZatcaParty Seller { get; init; } = null!;
    public ZatcaParty Buyer { get; init; } = null!;
    public IReadOnlyList<ZatcaLine> Lines { get; init; } = Array.Empty<ZatcaLine>();
    /// <summary>عدّاد الفواتير (ICV) وتجزئة الفاتورة السابقة (PIH): سلسلة لا تنقطع لكل وحدة إصدار.</summary>
    public long Counter { get; init; }
    public string PreviousHash { get; init; } = ZatcaInvoiceXml.FirstPreviousHash;
    /// <summary>رقم الفاتورة الأصلية وسبب الإشعار (إلزاميان للإشعار الدائن/المدين).</summary>
    public string? BillingReference { get; init; }
    public string? NoteReason { get; init; }
    public string PaymentMeansCode { get; init; } = "10";
}

/// <summary>
/// يبني وثيقة UBL 2.1 وفق مواصفة هيئة الزكاة. الوثيقة تُكتب بلا أي مسافات تنسيق: تجزئة الفاتورة تُحسب بعد حذف
/// عناصر التوقيع والـ QR منها، فأي فراغ يبقى مكانها يغيّر التجزئة؛ بلا فراغات تتطابق تجزئة النسختين دائماً.
/// </summary>
public static class ZatcaInvoiceXml
{
    public const string Invoice = "388";
    public const string CreditNote = "381";
    public const string DebitNote = "383";
    /// <summary>تجزئة «السابقة» لأول فاتورة في السلسلة: Base64 لتجزئة SHA-256 للنص "0".</summary>
    public const string FirstPreviousHash = "NWZlY2ViNjZmZmM4NmYzOGQ5NTI3ODZjNmQ2OTZjNzljMmRiYzIzOWRkNGU5MWI0NjcyOWQ3M2EyN2ZiNTdlOQ==";

    private const string Ns = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    private const string Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private const string Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private const string Ext = "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2";

    /// <summary>
    /// <paramref name="signature"/> فارغة = الوثيقة قبل التوقيع (التي تُحسب منها التجزئة)؛ وإلا تُضاف عناصر التوقيع
    /// الثلاثة: UBLExtensions، ومرجع QR، وcac:Signature.
    /// </summary>
    public static string Build(ZatcaDocument d, ZatcaSignatureParts? signature = null)
    {
        var text = new StringBuilder();
        using (var w = XmlWriter.Create(text, new XmlWriterSettings { OmitXmlDeclaration = true, Indent = false, NewLineHandling = NewLineHandling.None }))
        {
            w.WriteStartElement("Invoice", Ns);
            w.WriteAttributeString("xmlns", "cac", null, Cac);
            w.WriteAttributeString("xmlns", "cbc", null, Cbc);
            w.WriteAttributeString("xmlns", "ext", null, Ext);

            if (signature != null) w.WriteRaw(signature.UblExtensions);
            Text(w, "ProfileID", "reporting:1.0");
            Text(w, "ID", d.Number);
            Text(w, "UUID", d.Uuid.ToString());
            Text(w, "IssueDate", d.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Text(w, "IssueTime", d.IssueTime);
            w.WriteStartElement("cbc", "InvoiceTypeCode", Cbc);
            w.WriteAttributeString("name", d.IsSimplified ? "0200000" : "0100000");
            w.WriteString(d.TypeCode);
            w.WriteEndElement();
            Text(w, "DocumentCurrencyCode", d.Currency);
            Text(w, "TaxCurrencyCode", "SAR");

            if (d.TypeCode != Invoice)
            {
                w.WriteStartElement("cac", "BillingReference", Cac);
                w.WriteStartElement("cac", "InvoiceDocumentReference", Cac);
                Text(w, "ID", string.IsNullOrWhiteSpace(d.BillingReference) ? d.Number : d.BillingReference);
                w.WriteEndElement();
                w.WriteEndElement();
            }

            w.WriteStartElement("cac", "AdditionalDocumentReference", Cac);
            Text(w, "ID", "ICV");
            Text(w, "UUID", d.Counter.ToString(CultureInfo.InvariantCulture));
            w.WriteEndElement();
            Attachment(w, "PIH", d.PreviousHash);
            if (signature != null)
            {
                Attachment(w, "QR", signature.Qr);
                w.WriteStartElement("cac", "Signature", Cac);
                Text(w, "ID", "urn:oasis:names:specification:ubl:signature:Invoice");
                Text(w, "SignatureMethod", "urn:oasis:names:specification:ubl:dsig:enveloped:xades");
                w.WriteEndElement();
            }

            w.WriteStartElement("cac", "AccountingSupplierParty", Cac);
            Party(w, d.Seller, full: true);
            w.WriteEndElement();
            w.WriteStartElement("cac", "AccountingCustomerParty", Cac);
            Party(w, d.Buyer, full: !d.IsSimplified);
            w.WriteEndElement();

            // تاريخ التوريد إلزامي في الفاتورة الضريبية
            if (!d.IsSimplified)
            {
                w.WriteStartElement("cac", "Delivery", Cac);
                Text(w, "ActualDeliveryDate", d.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                w.WriteEndElement();
            }

            w.WriteStartElement("cac", "PaymentMeans", Cac);
            Text(w, "PaymentMeansCode", d.PaymentMeansCode);
            if (d.TypeCode != Invoice) Text(w, "InstructionNote", string.IsNullOrWhiteSpace(d.NoteReason) ? "تصحيح فاتورة" : d.NoteReason.Trim());
            w.WriteEndElement();

            var net = d.Lines.Sum(l => l.Net);
            var vat = d.Lines.Sum(l => l.VatAmount);

            // إجمالي الضريبة بالريال (عملة الضريبة)، ثم تفصيلها بعملة الفاتورة لكل تصنيف ونسبة
            w.WriteStartElement("cac", "TaxTotal", Cac);
            Amount(w, "TaxAmount", Round(vat * d.ExchangeRate), "SAR");
            w.WriteEndElement();
            w.WriteStartElement("cac", "TaxTotal", Cac);
            Amount(w, "TaxAmount", vat, d.Currency);
            foreach (var g in d.Lines.GroupBy(l => (l.Category, l.VatRate, l.ExemptionCode)))
            {
                w.WriteStartElement("cac", "TaxSubtotal", Cac);
                Amount(w, "TaxableAmount", g.Sum(l => l.Net), d.Currency);
                Amount(w, "TaxAmount", g.Sum(l => l.VatAmount), d.Currency);
                var first = g.First();
                TaxCategory(w, "TaxCategory", first, withReason: true);
                w.WriteEndElement();
            }
            w.WriteEndElement();

            w.WriteStartElement("cac", "LegalMonetaryTotal", Cac);
            Amount(w, "LineExtensionAmount", net, d.Currency);
            Amount(w, "TaxExclusiveAmount", net, d.Currency);
            Amount(w, "TaxInclusiveAmount", net + vat, d.Currency);
            Amount(w, "AllowanceTotalAmount", 0, d.Currency);
            Amount(w, "PrepaidAmount", 0, d.Currency);
            Amount(w, "PayableAmount", net + vat, d.Currency);
            w.WriteEndElement();

            for (var i = 0; i < d.Lines.Count; i++)
            {
                var l = d.Lines[i];
                w.WriteStartElement("cac", "InvoiceLine", Cac);
                Text(w, "ID", (i + 1).ToString(CultureInfo.InvariantCulture));
                w.WriteStartElement("cbc", "InvoicedQuantity", Cbc);
                w.WriteAttributeString("unitCode", "PCE");
                w.WriteString(Number(l.Quantity, 6));
                w.WriteEndElement();
                Amount(w, "LineExtensionAmount", l.Net, d.Currency);
                if (l.Allowance > 0)
                {
                    w.WriteStartElement("cac", "AllowanceCharge", Cac);
                    Text(w, "ChargeIndicator", "false");
                    Text(w, "AllowanceChargeReason", "discount");
                    Amount(w, "Amount", l.Allowance, d.Currency);
                    w.WriteEndElement();
                }
                if (l.ExciseTaxAmount > 0)
                {
                    w.WriteStartElement("cac", "AllowanceCharge", Cac);
                    Text(w, "ChargeIndicator", "true");
                    Text(w, "AllowanceChargeReason", "Excise tax");
                    Amount(w, "Amount", l.ExciseTaxAmount, d.Currency);
                    TaxCategory(w, "TaxCategory", l, withReason: true);
                    w.WriteEndElement();
                }
                w.WriteStartElement("cac", "TaxTotal", Cac);
                Amount(w, "TaxAmount", l.VatAmount, d.Currency);
                Amount(w, "RoundingAmount", l.Net + l.VatAmount, d.Currency);
                w.WriteEndElement();
                w.WriteStartElement("cac", "Item", Cac);
                Text(w, "Name", l.Name);
                TaxCategory(w, "ClassifiedTaxCategory", l, withReason: false);
                w.WriteEndElement();
                w.WriteStartElement("cac", "Price", Cac);
                w.WriteStartElement("cbc", "PriceAmount", Cbc);
                w.WriteAttributeString("currencyID", d.Currency);
                w.WriteString(Number(l.UnitPrice, 6, minDecimals: 2));
                w.WriteEndElement();
                w.WriteEndElement();
                w.WriteEndElement();
            }

            w.WriteEndElement();
        }
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + text;
    }

    /// <summary>
    /// رمز التصنيف الضريبي لدى الهيئة: S أساسي، Z صفري، E معفى، O خارج النطاق. هامش الربح بلا رمز في مواصفات الهيئة
    /// (فاتورته لا تُرسل)، فلا يُخترع له رمز.
    /// </summary>
    public static string CategoryCode(VatCategory category) => category switch
    {
        VatCategory.Standard => "S",
        VatCategory.ZeroRated => "Z",
        VatCategory.Exempt => "E",
        VatCategory.OutOfScope => "O",
        _ => throw new ConflictException(Messages.MarginSchemeNotSubmittedToZatca),
    };

    private static void Party(XmlWriter w, ZatcaParty p, bool full)
    {
        w.WriteStartElement("cac", "Party", Cac);
        // السجل التجاري أولاً، وإلا الهوية الأخرى بنوعها (NAT/IQA/PAS...) كما تحددها الهيئة
        var idScheme = !string.IsNullOrWhiteSpace(p.CrNumber) ? "CRN" : p.IdType?.Trim().ToUpperInvariant();
        var idValue = !string.IsNullOrWhiteSpace(p.CrNumber) ? p.CrNumber : p.IdNumber;
        if (!string.IsNullOrWhiteSpace(idScheme) && !string.IsNullOrWhiteSpace(idValue))
        {
            w.WriteStartElement("cac", "PartyIdentification", Cac);
            w.WriteStartElement("cbc", "ID", Cbc);
            w.WriteAttributeString("schemeID", idScheme);
            w.WriteString(idValue.Trim());
            w.WriteEndElement();
            w.WriteEndElement();
        }
        if (full)
        {
            w.WriteStartElement("cac", "PostalAddress", Cac);
            Optional(w, "StreetName", p.Street);
            Optional(w, "BuildingNumber", p.BuildingNumber);
            Optional(w, "CitySubdivisionName", p.District);
            Optional(w, "CityName", p.City);
            Optional(w, "PostalZone", p.PostalCode);
            w.WriteStartElement("cac", "Country", Cac);
            Text(w, "IdentificationCode", p.CountryCode);
            w.WriteEndElement();
            w.WriteEndElement();
        }
        if (!string.IsNullOrWhiteSpace(p.VatNumber))
        {
            w.WriteStartElement("cac", "PartyTaxScheme", Cac);
            Text(w, "CompanyID", p.VatNumber.Trim());
            TaxScheme(w);
            w.WriteEndElement();
        }
        w.WriteStartElement("cac", "PartyLegalEntity", Cac);
        Text(w, "RegistrationName", p.Name);
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void TaxCategory(XmlWriter w, string element, ZatcaLine l, bool withReason)
    {
        w.WriteStartElement("cac", element, Cac);
        Text(w, "ID", CategoryCode(l.Category));
        Text(w, "Percent", Number(l.VatRate, 2, minDecimals: 2));
        if (withReason && l.Category != VatCategory.Standard)
        {
            Optional(w, "TaxExemptionReasonCode", l.ExemptionCode);
            Optional(w, "TaxExemptionReason", l.ExemptionReason);
        }
        TaxScheme(w);
        w.WriteEndElement();
    }

    private static void TaxScheme(XmlWriter w)
    {
        w.WriteStartElement("cac", "TaxScheme", Cac);
        Text(w, "ID", "VAT");
        w.WriteEndElement();
    }

    private static void Attachment(XmlWriter w, string id, string content)
    {
        w.WriteStartElement("cac", "AdditionalDocumentReference", Cac);
        Text(w, "ID", id);
        w.WriteStartElement("cac", "Attachment", Cac);
        w.WriteStartElement("cbc", "EmbeddedDocumentBinaryObject", Cbc);
        w.WriteAttributeString("mimeCode", "text/plain");
        w.WriteString(content);
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void Text(XmlWriter w, string name, string value) => w.WriteElementString("cbc", name, Cbc, value);

    private static void Optional(XmlWriter w, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) Text(w, name, value.Trim());
    }

    private static void Amount(XmlWriter w, string name, decimal value, string currency)
    {
        w.WriteStartElement("cbc", name, Cbc);
        w.WriteAttributeString("currencyID", currency);
        w.WriteString(Round(value).ToString("0.00", CultureInfo.InvariantCulture));
        w.WriteEndElement();
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string Number(decimal value, int maxDecimals, int minDecimals = 0)
        => Math.Round(value, maxDecimals, MidpointRounding.AwayFromZero)
            .ToString("0." + new string('0', minDecimals) + new string('#', maxDecimals - minDecimals), CultureInfo.InvariantCulture);
}

/// <summary>ما يضيفه التوقيع للوثيقة: كتلة UBLExtensions (XML خام) ورمز QR.</summary>
public sealed record ZatcaSignatureParts(string UblExtensions, string Qr);
