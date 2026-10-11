namespace ERP.Core.Models.Shared;

public enum UserRole
{
    Owner = 1,
    Admin = 2,
    GeneralManager = 3,
    ChiefAccountant = 4,
    SalesRep = 5,
    /// <summary>موظف يدخل بوابة الخدمة الذاتية فقط (طلبات إجازته)؛ لا يفتح شاشات النظام.</summary>
    Employee = 6
}
