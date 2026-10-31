using System.Text.RegularExpressions;

namespace ERP.Service.Services.Shared;

/// <summary>
/// نص رسالة الفاتورة بمتغيرات مسمّاة ({customer_name}): قائمة المتغيرات المتاحة، والتحقق من قيود Meta، والتحويل إلى متغيرات موضعية ({{1}}..).
/// كل ظهور لمتغير يأخذ رقماً جديداً بترتيبه في النص، فتُرسل القيم بالترتيب نفسه.
/// </summary>
public static class WhatsAppInvoiceText
{
    public const string CustomerName = "customer_name";
    public const string InvoiceNumber = "invoice_number";
    public const string Amount = "amount";
    public const string InvoiceDate = "invoice_date";
    public const string CompanyName = "company_name";

    public static readonly IReadOnlyList<string> Keys = new[] { CustomerName, InvoiceNumber, Amount, InvoiceDate, CompanyName };

    /// <summary>متغيرات قالب الفاتورة الافتراضي (erp_sales_invoice_document) بترتيبها.</summary>
    public static readonly string[] DefaultKeys = { CustomerName, InvoiceNumber, Amount };

    public const int MaxBody = 1024;
    public const int MaxFooter = 60;
    public const int MaxVariables = 10;

    private static readonly Regex Token = new(@"\{\s*([A-Za-z_]+)\s*\}");
    private static readonly Regex ExtraNewlines = new(@"\n{3,}");

    /// <summary>نص القالب الافتراضي بمتغيرات مسمّاة: نقطة بداية للمستخدم.</summary>
    public static readonly IReadOnlyDictionary<string, string> DefaultBodies = new Dictionary<string, string>
    {
        ["ar"] = "مرحباً {customer_name}، مرفق لكم الفاتورة رقم {invoice_number} بمبلغ {amount}. شكراً لتعاملكم معنا.",
        ["en"] = "Hello {customer_name}, please find attached invoice {invoice_number} for {amount}. Thank you for your business.",
    };

    /// <summary>أمثلة القيم: يطلبها Meta مع القالب لمراجعته، وتُرسل بها رسالة الاختبار.</summary>
    public static IReadOnlyDictionary<string, string> Samples(string language, string companyName) => language == "en"
        ? new Dictionary<string, string>
        {
            [CustomerName] = "Example Co.", [InvoiceNumber] = "INV-1001", [Amount] = "1,150.00 SAR", [InvoiceDate] = "2026-01-15",
            [CompanyName] = companyName.Length > 0 ? companyName : "Your company",
        }
        : new Dictionary<string, string>
        {
            [CustomerName] = "شركة المثال", [InvoiceNumber] = "INV-1001", [Amount] = "1,150.00 SAR", [InvoiceDate] = "2026-01-15",
            [CompanyName] = companyName.Length > 0 ? companyName : "منشأتك",
        };

    public sealed record Compiled(string Body, string MetaBody, string[] Keys, string? Footer);

    /// <summary>يتحقق من النص والتذييل بقيود Meta ويحوّل المتغيرات؛ الخطأ برسالة تشرح ما يُصلَح.</summary>
    public static Compiled Compile(string? body, string? footer)
    {
        var text = ExtraNewlines.Replace((body ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n'), "\n\n").Trim();
        if (text.Length == 0) throw new ValidationFailedException(Messages.WhatsAppTemplateBodyRequired);

        var keys = new List<string>();
        string? unknown = null;
        var meta = Token.Replace(text, m =>
        {
            var key = m.Groups[1].Value.ToLowerInvariant();
            if (!Keys.Contains(key)) { unknown ??= m.Value; return m.Value; }
            keys.Add(key);
            return $"{{{{{keys.Count}}}}}";
        });
        if (unknown != null) throw new ValidationFailedException(string.Format(Messages.WhatsAppTemplateUnknownVariable, unknown));
        // أقواس متبقية خارج متغير صحيح تربك Meta في قراءة المتغيرات
        if (Token.Replace(text, string.Empty).IndexOfAny(new[] { '{', '}' }) >= 0)
            throw new ValidationFailedException(string.Format(Messages.WhatsAppTemplateUnknownVariable, "{ }"));
        if (keys.Count > MaxVariables) throw new ValidationFailedException(string.Format(Messages.WhatsAppTemplateTooManyVariables, MaxVariables));
        // شرط Meta: لا يبدأ النص بمتغير ولا ينتهي به
        if (meta.StartsWith("{{") || meta.EndsWith("}}")) throw new ValidationFailedException(Messages.WhatsAppTemplateVariableAtEdge);
        if (meta.Length > MaxBody) throw new ValidationFailedException(string.Format(Messages.WhatsAppTemplateBodyTooLong, MaxBody));

        var foot = Regex.Replace(footer ?? string.Empty, @"\s+", " ").Trim();
        if (foot.Length > MaxFooter || foot.IndexOfAny(new[] { '{', '}' }) >= 0)
            throw new ValidationFailedException(string.Format(Messages.WhatsAppTemplateFooterInvalid, MaxFooter));

        return new Compiled(text, meta, keys.ToArray(), foot.Length == 0 ? null : foot);
    }

    public static string[] SplitKeys(string keys) => keys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>قيم المتغيرات بترتيب القالب؛ المتغير بلا قيمة يُرسل «-» (Meta يرفض القيمة الفارغة).</summary>
    public static string[] Values(IEnumerable<string> keys, IReadOnlyDictionary<string, string> values)
        => keys.Select(k => values.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v : "-").ToArray();
}
