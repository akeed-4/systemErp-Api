namespace ERP.Core.Models.CarShowroom;

public enum ChargeStatus
{
    Estimated = 1,  // تقديري (لا يدخل تكلفة السيارة)
    Actual = 2,     // فعلي
    Cancelled = 3,
    /// <summary>تقديري (أو مستحق) حلّ محله سطر فعلي من سند تكاليف إضافية؛ يبقى سجلاً للقيمة الأولى.</summary>
    Replaced = 4
}
