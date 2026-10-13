namespace ERP.Core.Models.CarShowroom;

/// <summary>المعالجة المحاسبية للمصروف على السيارة: كل مصروف يُصنَّف بواحدة من ثلاث.</summary>
public enum ChargeTreatment
{
    Capitalize = 1,          // يُضاف لتكلفة السيارة (لازم لتجهيزها للبيع)
    Expense = 2,             // مصروف على المعرض (تشغيلي/بيعي/عام)
    RechargeToCustomer = 3   // يُحمَّل على العميل كسطر مستقل في فاتورته
}
