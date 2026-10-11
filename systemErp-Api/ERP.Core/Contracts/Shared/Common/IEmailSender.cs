namespace ERP.Core.Contracts.Shared;

/// <summary>إرسال بريد المنصة (رموز التحقق والإشعارات).</summary>
public interface IEmailSender
{
    /// <summary>هل ضُبط خادم البريد وبيانات الدخول؟ بدونها لا يُرسل شيء.</summary>
    bool IsConfigured { get; }
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
}
