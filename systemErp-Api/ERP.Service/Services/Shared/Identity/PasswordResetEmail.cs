using System.Net;

namespace ERP.Service.Services.Shared;

/// <summary>رسالة رمز استعادة كلمة المرور (عربي وإنجليزي في رسالة واحدة).</summary>
public static class PasswordResetEmail
{
    public const string Subject = "رمز التحقق لاستعادة كلمة المرور | Password reset code";

    public static string Body(string userName, string code, int minutes)
    {
        var name = WebUtility.HtmlEncode(userName);
        return $@"<!DOCTYPE html>
<html lang=""ar"" dir=""rtl"">
<body style=""margin:0;padding:24px;background:#f1f5f9;font-family:Tahoma,Arial,sans-serif;color:#0f172a"">
  <div style=""max-width:480px;margin:0 auto;background:#ffffff;border:1px solid #e2e8f0;border-radius:16px;padding:28px"">
    <h2 style=""margin:0 0 12px;font-size:18px"">استعادة كلمة المرور</h2>
    <p style=""margin:0 0 16px;font-size:14px;line-height:1.8"">مرحباً {name}، استخدم رمز التحقق التالي لإعادة تعيين كلمة المرور. الرمز صالح لمدة {minutes} دقائق.</p>
    <div dir=""ltr"" style=""text-align:center;font-size:32px;font-weight:bold;letter-spacing:8px;background:#eef2ff;color:#3730a3;border-radius:12px;padding:16px;margin:0 0 16px"">{code}</div>
    <p style=""margin:0 0 20px;font-size:12px;color:#64748b;line-height:1.8"">إن لم تطلب استعادة كلمة المرور فتجاهل هذه الرسالة، ولا تشارك الرمز مع أحد.</p>
    <hr style=""border:none;border-top:1px solid #e2e8f0;margin:0 0 20px"">
    <div dir=""ltr"" style=""text-align:left"">
      <h2 style=""margin:0 0 12px;font-size:16px"">Password reset</h2>
      <p style=""margin:0 0 8px;font-size:13px;line-height:1.6"">Hello {name}, use the code above to reset your password. It is valid for {minutes} minutes.</p>
      <p style=""margin:0;font-size:12px;color:#64748b;line-height:1.6"">If you did not request a password reset, ignore this email and never share the code.</p>
    </div>
  </div>
</body>
</html>";
    }
}
