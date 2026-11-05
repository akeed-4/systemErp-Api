namespace ERP.Core.Models.Shared;

public enum PaymentMethod
{
    Cash = 1,
    Credit = 2,
    BankCard = 3,
    BankTransfer = 4,
    /// <summary>سداد جزء من الفاتورة بعربون مقبوض سابقاً: يُقفل حساب عربونات العملاء (216) بدل الصندوق/البنك.</summary>
    CustomerDeposit = 5,
    /// <summary>
    /// استقطاع عقد من فاتورة مستخلص (ضمان محتجز، غرامة، خصم آخر): جزء غير آجل يُرحَّل على الحساب المحدد في رمز الطريقة
    /// (PaymentMethodCode = رمز الحساب) بدل ذمة الطرف، فيبقى على الفاتورة الصافي المستحق وحده. للفواتير التي ينشئها النظام فقط.
    /// </summary>
    ContractSettlement = 6
}
