namespace ERP.Core.Models.Shared;

public enum PaymentMethod
{
    Cash = 1,
    Credit = 2,
    BankCard = 3,
    BankTransfer = 4,
    /// <summary>سداد جزء من الفاتورة بعربون مقبوض سابقاً: يُقفل حساب عربونات العملاء (216) بدل الصندوق/البنك.</summary>
    CustomerDeposit = 5
}
