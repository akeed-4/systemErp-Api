namespace ERP.Core.Models.POS;

public enum CardTerminalPaymentStatus
{
    /// <summary>أُنشئ وأُرسل (أو سيُرسل) للجهاز ولم تصل نتيجته بعد.</summary>
    Pending = 1,
    /// <summary>الجهاز أعاد موافقة (برقم مرجع العملية RRN).</summary>
    Approved = 2,
    /// <summary>الجهاز أعاد رفضاً: لم يُخصم شيء.</summary>
    Declined = 3,
    /// <summary>انقطع الاتصال أو انتهت المهلة قبل وصول نتيجة نهائية: قد يكون خُصم أو لا، يُستعلم من الجهاز أو يُحسم يدوياً.</summary>
    Unknown = 4,
    /// <summary>لم يصل الطلب للجهاز أصلاً (تعذّر الاتصال بخدمة الربط): لم يُخصم شيء.</summary>
    NotSent = 5,
    /// <summary>موافقة لم تُستخدم في بيع وأُلغيت/استُردّت على الجهاز (حسم إداري).</summary>
    Reversed = 6
}
