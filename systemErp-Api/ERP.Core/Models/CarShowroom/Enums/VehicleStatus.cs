namespace ERP.Core.Models.CarShowroom;

public enum VehicleStatus
{
    Available = 1,
    Reserved = 2,
    Sold = 3,
    /// <summary>مشطوبة بجرد معتمد (مفقودة فعلياً). تخرج من المخزون ولا تُباع، وتُستعاد بجرد لاحق يعثر عليها.</summary>
    WrittenOff = 4,
    /// <summary>في الطريق: أُنشئت من أمر شراء بشاسيه مؤقت ولم تُستلم بعد. لا تُعرض للبيع ولا تدخل الجرد.</summary>
    InTransit = 5,
    /// <summary>شراء أُلغي بعد أن صُرف عليها: تبقى سجلاً لمصاريفها ولا تدخل المخزون.</summary>
    Cancelled = 6,
    /// <summary>مسودة: بطاقة أنشأتها مسودة فاتورة شراء. لا تظهر في المخزون ولا البيع، وتُدار من فاتورتها حتى الترحيل.</summary>
    Draft = 7
}
