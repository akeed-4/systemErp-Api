using System.Globalization;
using ERP.Core.Contracts.Shared;
using ERP.Service.Data;
using ERP.Service.Services.Shared.Text;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ERP.Service.Services.Shared;

/// <summary>
/// PDF المستندات من بيانات السرفر (QuestPDF) لا من الواجهة، فما يصل العميل هو المحفوظ فعلاً. يطابق أقسام قالب الطباعة: المنشأة
/// وأرقامها، نوع الفاتورة ورمز QR الهيئة، العميل، البنود، الإجماليات والتفقيط. الخط من خطوط النظام (Arial/Tahoma تحملان العربية
/// على Windows)؛ على خادم بلا أحدها يُثبَّت خط عربي أو يوضع ملفه بجوار التطبيق (مسار اكتشاف QuestPDF).
/// </summary>
public class InvoicePdfRenderer : IInvoicePdfRenderer
{
    private static readonly string[] Fonts = { "Arial", "Tahoma", "Segoe UI" };
    private static readonly string Accent = "#1e1b4b";

    static InvoicePdfRenderer()
    {
        // رخصة المجتمع: مجانية لمن يقل دخله السنوي عن مليون دولار (راجِع شروط QuestPDF قبل الإنتاج)
        QuestPDF.Settings.License = LicenseType.Community;
        // منذ 2026.9 لا تُستعمل خطوط النظام إلا صراحةً، والخط الغائب يوقف التوليد بدل استبداله
        QuestPDF.Settings.UseEnvironmentFonts = true;
    }

    private readonly ErpDbContext _db;
    public InvoicePdfRenderer(ErpDbContext db) => _db = db;

    public async Task<RenderedDocument> RenderSalesInvoiceAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var inv = await _db.Set<SalesInvoice>().AsNoTracking().Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
                  ?? throw new NotFoundException(Messages.InvoiceNotFound);
        var company = await _db.Set<Tenant>().AsNoTracking().FirstAsync(ct);
        var qr = string.IsNullOrEmpty(inv.ZatcaQrCode) ? null : QrPng(inv.ZatcaQrCode);

        var bytes = Document.Create(doc => doc.Page(page =>
        {
            Setup(page);
            page.Header().Element(h => Header(h, company, inv, qr));
            page.Content().PaddingTop(10).Column(col =>
            {
                col.Spacing(10);
                col.Item().Element(c => Meta(c, inv));
                col.Item().Element(c => Party(c, inv));
                col.Item().Element(c => Lines(c, inv));
                col.Item().Element(c => Totals(c, inv));
            });
            page.Footer().AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Darken1));
                t.Span($"{company.NameAr} — UUID: {inv.Uuid} — ");
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        })).GeneratePdf();
        return new RenderedDocument(bytes, $"{SafeFileName(inv.InvoiceNumber)}.pdf");
    }

    public RenderedDocument RenderTestDocument(string companyName)
    {
        var bytes = Document.Create(doc => doc.Page(page =>
        {
            Setup(page);
            page.Content().PaddingVertical(40).Column(col =>
            {
                col.Spacing(12);
                col.Item().AlignCenter().Text(companyName).FontSize(18).Bold();
                col.Item().AlignCenter().Text("رسالة اختبار من نظام نمو المحاسبي").FontSize(14);
                col.Item().AlignCenter().Text("Test message from the accounting system").FontSize(12).FontColor(Colors.Grey.Darken1);
                col.Item().AlignCenter().Text(SaudiTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).FontSize(10).FontColor(Colors.Grey.Darken1);
            });
        })).GeneratePdf();
        return new RenderedDocument(bytes, "test.pdf");
    }

    // ---------- الأقسام ----------

    private static void Header(IContainer c, Tenant company, SalesInvoice inv, byte[]? qr) => c.BorderBottom(1.5f).BorderColor(Accent).PaddingBottom(8).Row(row =>
    {
        row.RelativeItem().Column(col =>
        {
            col.Item().Text(company.NameAr).FontSize(16).Bold();
            if (!string.IsNullOrWhiteSpace(company.NameEn) && company.NameEn != company.NameAr) col.Item().Text(company.NameEn).FontSize(10).FontColor(Colors.Grey.Darken1);
            col.Item().Text(string.Join(" - ", new[] { company.Address, company.City }.Where(s => !string.IsNullOrWhiteSpace(s)))).FontSize(9);
            col.Item().Text($"الرقم الضريبي VAT No: {company.VatNumber}").FontSize(9);
            if (!string.IsNullOrWhiteSpace(company.CrNumber)) col.Item().Text($"السجل التجاري CR No: {company.CrNumber}").FontSize(9);
            if (!string.IsNullOrWhiteSpace(company.Phone)) col.Item().Text($"الهاتف Phone: {company.Phone}").FontSize(9);
        });
        row.ConstantItem(150).AlignCenter().Column(col =>
        {
            var (ar, en) = Title(inv.InvoiceType);
            col.Item().AlignCenter().Text(ar).FontSize(15).Bold().FontColor(Accent);
            col.Item().AlignCenter().Text(en).FontSize(9).FontColor(Colors.Grey.Darken1);
        });
        if (qr != null) row.ConstantItem(90).AlignLeft().Image(qr);
    });

    private static void Meta(IContainer c, SalesInvoice inv) => c.Background(Colors.Grey.Lighten4).Padding(6).Row(row =>
    {
        void Cell(string label, string value) => row.RelativeItem().Column(col =>
        {
            col.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Darken1);
            col.Item().Text(value).Bold();
        });
        Cell("رقم الفاتورة Invoice No", inv.InvoiceNumber);
        Cell("تاريخ الإصدار Issue Date", inv.IssueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Cell("وقت الإصدار Time", inv.IssueTime);
        Cell("طريقة الدفع Payment", PaymentLabel(inv.PaymentMethod));
        Cell("العملة Currency", inv.CurrencyCode);
    });

    private static void Party(IContainer c, SalesInvoice inv) => c.Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(6).Column(col =>
    {
        col.Item().Text("بيانات العميل Customer").Bold().FontColor(Accent);
        col.Item().Text($"الاسم: {inv.PartyName}");
        if (!string.IsNullOrWhiteSpace(inv.PartyVatNumber)) col.Item().Text($"الرقم الضريبي: {inv.PartyVatNumber}");
        if (!string.IsNullOrWhiteSpace(inv.PartyCrNumber)) col.Item().Text($"السجل التجاري: {inv.PartyCrNumber}");
        var address = string.Join("، ", new[] { inv.PartyStreet, inv.PartyBuildingNo, inv.PartyDistrict, inv.PartyCity, inv.PartyPostalCode }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        if (address.Length > 0) col.Item().Text($"العنوان: {address}");
        else if (!string.IsNullOrWhiteSpace(inv.PartyAddress)) col.Item().Text($"العنوان: {inv.PartyAddress}");
    });

    private static void Lines(IContainer c, SalesInvoice inv) => c.Table(table =>
    {
        table.ColumnsDefinition(cols =>
        {
            cols.ConstantColumn(22);
            cols.RelativeColumn(4);
            cols.RelativeColumn(1.2f);
            cols.RelativeColumn(1.5f);
            cols.RelativeColumn(1.2f);
            cols.RelativeColumn(1.6f);
            cols.RelativeColumn(1);
            cols.RelativeColumn(1.4f);
            cols.RelativeColumn(1.8f);
        });
        table.Header(h =>
        {
            foreach (var title in new[] { "#", "الصنف Item", "الكمية Qty", "السعر Price", "الخصم Disc.", "الخاضع Taxable", "الضريبة %", "الضريبة VAT", "الإجمالي Total" })
                h.Cell().Background(Accent).Padding(4).Text(title).FontSize(8).Bold().FontColor(Colors.White);
        });
        var i = 0;
        foreach (var line in inv.Items.OrderBy(l => l.CreatedAt))
        {
            var bg = ++i % 2 == 0 ? Colors.Grey.Lighten4 : Colors.White;
            IContainer Cell() => table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4);
            Cell().Text(i.ToString(CultureInfo.InvariantCulture));
            Cell().Column(col =>
            {
                col.Item().Text(line.ItemName).Bold();
                if (!string.IsNullOrWhiteSpace(line.Sku)) col.Item().Text(line.Sku).FontSize(7).FontColor(Colors.Grey.Darken1);
            });
            Cell().Text($"{Num(line.Quantity, "0.###")} {line.Unit}".Trim());
            Cell().Text(Num(line.UnitPrice));
            Cell().Text(Num(line.Discount));
            Cell().Text(Num(line.TotalBeforeVat + line.ExciseTaxAmount));
            Cell().Text($"{Num(line.VatRate, "0.##")}%");
            Cell().Text(Num(line.VatAmount));
            Cell().Text(Num(line.TotalAfterVat)).Bold();
        }
    });

    private static void Totals(IContainer c, SalesInvoice inv) => c.Row(row =>
    {
        row.RelativeItem().PaddingLeft(10).Column(col =>
        {
            col.Item().Text("المبلغ كتابةً Amount in words").FontSize(8).FontColor(Colors.Grey.Darken1);
            col.Item().Text(inv.CurrencyCode == "SAR" ? ArabicAmountInWords.Riyals(inv.GrandTotal) : $"{Num(inv.GrandTotal)} {inv.CurrencyCode}").Bold();
            if (!string.IsNullOrWhiteSpace(inv.Notes)) col.Item().PaddingTop(6).Text($"ملاحظات: {inv.Notes}").FontSize(9);
        });
        row.ConstantItem(230).Column(col =>
        {
            void Line(string label, decimal value, bool strong = false)
            {
                col.Item().Background(strong ? Accent : Colors.White).Padding(4).Row(r =>
                {
                    var text = r.RelativeItem().Text(label);
                    var amount = r.AutoItem().Text(strong ? $"{Num(value)} {inv.CurrencyCode}" : Num(value));
                    if (strong) { text.Bold().FontColor(Colors.White); amount.Bold().FontColor(Colors.White); }
                });
            }
            Line("الإجمالي قبل الخصم Subtotal", inv.Subtotal + inv.DiscountTotal);
            if (inv.DiscountTotal != 0) Line("الخصم Discount", -inv.DiscountTotal);
            Line("الخاضع للضريبة Taxable", inv.Subtotal + inv.ExciseTaxTotal);
            if (inv.ExciseTaxTotal != 0) Line("الضريبة الانتقائية Excise", inv.ExciseTaxTotal);
            Line("ضريبة القيمة المضافة VAT", inv.VatTotal);
            Line("الإجمالي المستحق Grand Total", inv.GrandTotal, strong: true);
        });
    });

    // ---------- أدوات ----------

    private static void Setup(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.Margin(28);
        page.ContentFromRightToLeft();
        page.DefaultTextStyle(t => t.FontFamily(Fonts).FontSize(9.5f));
    }

    private static (string Ar, string En) Title(InvoiceType type) => type switch
    {
        InvoiceType.Simplified => ("فاتورة ضريبية مبسطة", "Simplified Tax Invoice"),
        InvoiceType.DebitNote => ("إشعار مدين", "Debit Note"),
        InvoiceType.CreditNote => ("إشعار دائن", "Credit Note"),
        _ => ("فاتورة ضريبية", "Tax Invoice"),
    };

    private static string PaymentLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "نقدي",
        PaymentMethod.Credit => "آجل",
        PaymentMethod.BankCard => "بطاقة",
        PaymentMethod.BankTransfer => "تحويل بنكي",
        PaymentMethod.CustomerDeposit => "دفعة مقدمة",
        _ => method.ToString(),
    };

    private static string Num(decimal value, string format = "#,##0.00") => value.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>رمز QR بتصحيح M (كما في الواجهة) ودقة تكفي لقراءته بعد الطباعة.</summary>
    private static byte[] QrPng(string data)
    {
        using var generator = new QRCodeGenerator();
        using var code = generator.CreateQrCode(data, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(code).GetGraphic(8);
    }

    internal static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return clean.Length == 0 ? "invoice" : clean;
    }
}
