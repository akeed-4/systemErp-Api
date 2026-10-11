using System.Net;

namespace ERP.Service.Services.Shared;

/// <summary>رسالة دعوة الموظف لبوابة الخدمة الذاتية (عربي وإنجليزي في رسالة واحدة).</summary>
public static class InviteEmail
{
    public const string Subject = "دعوة لبوابة الموظف | Employee portal invitation";

    public static string Body(string userName, string companyName, string link, int days)
    {
        var name = WebUtility.HtmlEncode(userName);
        var company = WebUtility.HtmlEncode(companyName);
        var href = WebUtility.HtmlEncode(link);
        return $@"<!DOCTYPE html>
<html lang=""ar"" dir=""rtl"">
<body style=""margin:0;padding:24px;background:#f1f5f9;font-family:Tahoma,Arial,sans-serif;color:#0f172a"">
  <div style=""max-width:480px;margin:0 auto;background:#ffffff;border:1px solid #e2e8f0;border-radius:16px;padding:28px"">
    <h2 style=""margin:0 0 12px;font-size:18px"">بوابة الموظف</h2>
    <p style=""margin:0 0 16px;font-size:14px;line-height:1.8"">مرحباً {name}، دعتك {company} لبوابة الموظف لتقديم طلبات الإجازة ومتابعتها. اضغط الزر لتعيين كلمة المرور. الرابط صالح لمدة {days} أيام.</p>
    <div style=""text-align:center;margin:0 0 16px""><a href=""{href}"" style=""display:inline-block;background:#3730a3;color:#ffffff;text-decoration:none;border-radius:12px;padding:12px 28px;font-size:14px;font-weight:bold"">تفعيل الحساب</a></div>
    <p dir=""ltr"" style=""margin:0 0 20px;font-size:11px;color:#64748b;word-break:break-all;text-align:left"">{href}</p>
    <hr style=""border:none;border-top:1px solid #e2e8f0;margin:0 0 20px"">
    <div dir=""ltr"" style=""text-align:left"">
      <h2 style=""margin:0 0 12px;font-size:16px"">Employee portal</h2>
      <p style=""margin:0;font-size:13px;line-height:1.6"">Hello {name}, {company} invited you to the employee portal to submit and track leave requests. Use the button above to set your password. The link is valid for {days} days.</p>
    </div>
  </div>
</body>
</html>";
    }
}
