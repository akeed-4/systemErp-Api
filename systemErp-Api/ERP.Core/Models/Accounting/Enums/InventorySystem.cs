namespace ERP.Core.Models.Accounting;

/// <summary>طريقة الجرد المحاسبية: تحدد أثر المشتريات والمبيعات على الحسابات فقط، والكميات ومتوسط التكلفة تُتتبَّع في الطريقتين.</summary>
public enum InventorySystem
{
    /// <summary>مستمر: المشتريات على المخزون، وكل بيع يُثبت تكلفته فوراً.</summary>
    Perpetual = 1,
    /// <summary>دوري: المشتريات على حساب المشتريات، والتكلفة تُستنتج من جرد نهاية الفترة.</summary>
    Periodic = 2
}
