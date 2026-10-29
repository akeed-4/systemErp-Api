using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using System.Text.RegularExpressions;
using ERP.Core.Contracts.Shared;
using Microsoft.Extensions.Options;

namespace ERP.Service.Services.Shared;

/// <summary>
/// إرسال البريد عبر SMTP (STARTTLS على المنفذ 587 مع Gmail).
/// الرسالة نصية وHTML معاً (multipart/alternative): رسالة HTML وحدها من أكثر ما يرفع تصنيفها كرسالة مزعجة.
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly IOptionsMonitor<EmailOptions> _options;
    public SmtpEmailSender(IOptionsMonitor<EmailOptions> options) => _options = options;

    public bool IsConfigured => _options.CurrentValue.IsConfigured;

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        var o = _options.CurrentValue;
        if (!o.IsConfigured) throw new InvalidOperationException("Email is not configured.");

        var from = new MailAddress(o.FromAddress, string.IsNullOrWhiteSpace(o.FromName) ? o.FromAddress : o.FromName, Encoding.UTF8);
        using var message = new MailMessage
        {
            From = from,
            Subject = subject, SubjectEncoding = Encoding.UTF8,
            Body = PlainText(htmlBody), BodyEncoding = Encoding.UTF8, IsBodyHtml = false,
            HeadersEncoding = Encoding.UTF8,
        };
        message.To.Add(new MailAddress(to));
        message.ReplyToList.Add(from);
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(htmlBody, Encoding.UTF8, MediaTypeNames.Text.Html));

        using var client = new SmtpClient(o.Host, o.Port)
        {
            EnableSsl = o.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(o.UserName, o.Password),
            Timeout = o.TimeoutSeconds * 1000,
        };
        await client.SendMailAsync(message, ct);
    }

    /// <summary>النسخة النصية من HTML الرسالة: الروابط تبقى ظاهرة بعناوينها، وكل كتلة في سطر.</summary>
    internal static string PlainText(string html)
    {
        var text = Regex.Replace(html, @"<(style|script|head)[^>]*>.*?</\1>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<a\s[^>]*href=""([^""]*)""[^>]*>(.*?)</a>", "$2: $1", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<br\s*/?>|</(p|div|h\d|li|tr)>|<hr[^>]*>", "\n", RegexOptions.IgnoreCase);
        text = WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]+>", string.Empty));
        var lines = text.Split('\n').Select(l => Regex.Replace(l, @"\s+", " ").Trim()).Where(l => l.Length > 0);
        return string.Join("\r\n\r\n", lines);
    }
}
