namespace ERP.Core.Models.Shared;

public enum StockMovementType
{
    InPurchase = 1,              // وارد مشتريات
    OutSales = 2,                 // منصرف مبيعات
    AdjustmentIn = 3,            // تسوية جردية - إضافة
    AdjustmentOut = 4,            // تسوية جردية - حذف
    TransferIn = 5,               // تحويل وارد من مستودع آخر (لا يغيّر رصيد الصنف الإجمالي ولا تكلفته)
    TransferOut = 6,              // تحويل صادر إلى مستودع آخر
    Revaluation = 7               // إعادة تقييم: تغيّر قيمة الصنف بلا كمية (إعادة احتساب، تكلفة معيارية، تكاليف إضافية، إعادة بناء بعد تعديل حركة)
}
