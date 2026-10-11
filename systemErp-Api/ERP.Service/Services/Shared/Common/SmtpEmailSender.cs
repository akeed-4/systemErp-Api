using System.Net;
using System.Net.Mail;
using System.Text;
using ERP.Core.Contracts.Shared;
using Microsoft.Extensions.Options;

namespace ERP.Service.Services.Shared;

/// <summary>إرسال البريد عبر SMTP (STARTTLS على المنفذ 587 مع Gmail).</summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly IOptionsMonitor<EmailOptions> _options;
    public SmtpEmailSender(IOptionsMonitor<EmailOptions> options) => _options = options;

    public bool IsConfigured => _options.CurrentValue.IsConfigured;

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        var o = _options.CurrentValue;
        if (!o.IsConfigured) throw new InvalidOperationException("Email is not configured.");

        using var message = new MailMessage
        {
            From = new MailAddress(o.FromAddress, string.IsNullOrWhiteSpace(o.FromName) ? o.FromAddress : o.FromName, Encoding.UTF8),
            Subject = subject, SubjectEncoding = Encoding.UTF8,
            Body = htmlBody, BodyEncoding = Encoding.UTF8, IsBodyHtml = true,
        };
        message.To.Add(new MailAddress(to));

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
}
