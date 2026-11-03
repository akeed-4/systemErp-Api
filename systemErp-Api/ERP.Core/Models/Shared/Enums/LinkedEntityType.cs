namespace ERP.Core.Models.Shared;

public enum LinkedEntityType
{
    General = 0,
    Customer = 1,
    Supplier = 2,
    Bank = 3,
    /// <summary>حساب مخزون الموقع (المستودع) تحت المخزون 114: يُنشأ للموقع الرئيسي عند تأسيس المنشأة؛ المواقع المضافة تختار حسابها.</summary>
    Warehouse = 4
}
