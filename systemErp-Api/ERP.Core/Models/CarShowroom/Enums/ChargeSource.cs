namespace ERP.Core.Models.CarShowroom;

public enum ChargeSource
{
    Manual = 1,
    Tamm = 2,
    LandedCostVoucher = 3,
    /// <summary>رصيد تكلفة سابق لسجل المصاريف (حقول التكلفة اليدوية القديمة أو تكلفة أُدخلت عند إنشاء المركبة).</summary>
    OpeningBalance = 4,
    /// <summary>رسوم مُدخلة في حقول أمر الشراء (جمارك، أرضيات ميناء): يديرها الأمر نفسه.</summary>
    OrderFields = 5,
    /// <summary>مبالغ مُدخلة في حقول عقد البيع (تسجيل، تأمين، ملحقات): يديرها العقد نفسه.</summary>
    ContractFields = 6
}
