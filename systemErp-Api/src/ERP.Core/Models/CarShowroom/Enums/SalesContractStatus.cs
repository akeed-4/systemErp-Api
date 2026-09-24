namespace ERP.Core.Models.CarShowroom;

/// <summary>حالة عقد بيع السيارة - 5 مراحل معتمدة + الإلغاء (تطابق واجهة Angular).</summary>
public enum SalesContractStatus
{
    Draft = 1,        // 1. طلبات ومسودات العقود
    Approved = 2,      // 2. تدقيق واعتماد العقود
    Allocated = 3,      // 3. تخصيص الشاسيه وتجهيز PDI
    Delivered = 4,      // 4. تسليم المركبة ومحضر الاستلام
    Invoiced = 5,        // 5. الفوترة والترحيل المالي
    Cancelled = 6
}
