namespace ERP.Core.Models.CarShowroom;

/// <summary>مرحلة المصروف في عمر السيارة. مصاريف مرحلة الاحتفاظ المُرسمَلة تُجمع في تكلفة التجهيز، وغيرها في التكاليف الإضافية.</summary>
public enum ExpenseStage
{
    Purchase = 1,   // شراء/استيراد
    Holding = 2,    // احتفاظ وتجهيز
    Sale = 3,       // بيع
    General = 4     // عام (غير مرتبط بسيارة بعينها)
}
