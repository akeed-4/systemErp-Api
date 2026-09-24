namespace ERP.Core.Models.Shared;

public enum StockMovementType
{
    InPurchase = 1,              // وارد مشتريات
    OutSales = 2,                 // منصرف مبيعات
    AdjustmentIn = 3,            // تسوية جردية - إضافة
    AdjustmentOut = 4             // تسوية جردية - حذف
}
