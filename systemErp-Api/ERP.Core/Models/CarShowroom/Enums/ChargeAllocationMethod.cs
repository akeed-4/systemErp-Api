namespace ERP.Core.Models.CarShowroom;

/// <summary>طريقة توزيع مصروف واحد على عدة سيارات.</summary>
public enum ChargeAllocationMethod
{
    Equal = 1,      // بالتساوي
    ByValue = 2,    // حسب القيمة (ثمن الشراء)
    ByVolume = 3,   // حسب الحجم
    ByWeight = 4,   // حسب الوزن
    Manual = 5      // يدوي
}
