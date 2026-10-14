namespace ERP.Core.Models.Shared;

/// <summary>
/// توقيت المملكة (UTC+3 بلا توقيت صيفي) هو توقيت الأعمال: تواريخ المستندات والقيود والفترات المحاسبية تُحسب به،
/// فقيد الساعة 1 فجراً في أول الشهر يقع في الشهر الجديد لا في السابق كما لو حُسب بالتوقيت العالمي.
/// الطوابع التقنية (الإنشاء، الدخول، انتهاء الجلسات والاشتراكات) تبقى بالتوقيت العالمي.
/// </summary>
public static class SaudiTime
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(3);

    /// <summary>الوقت الحالي بتوقيت المملكة.</summary>
    public static DateTime Now => DateTime.SpecifyKind(DateTime.UtcNow + Offset, DateTimeKind.Unspecified);

    /// <summary>تاريخ اليوم بتوقيت المملكة: الافتراضي لكل تاريخ مستند أو قيد لم يُحدَّد.</summary>
    public static DateTime Today => Now.Date;

    public static DateTime ToUtc(DateTime saudiLocal) => DateTime.SpecifyKind(saudiLocal - Offset, DateTimeKind.Utc);

    /// <summary>طابع عالمي (مثل وقت إغلاق وردية) إلى وقت المملكة: تاريخ قيده يتبع اليوم المحلي لا العالمي.</summary>
    public static DateTime FromUtc(DateTime utc) => DateTime.SpecifyKind(utc + Offset, DateTimeKind.Unspecified);
}
