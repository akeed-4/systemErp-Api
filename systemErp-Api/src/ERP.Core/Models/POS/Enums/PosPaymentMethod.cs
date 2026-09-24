namespace ERP.Core.Models.POS;

/// <summary>طريقة الدفع في نقطة البيع (cash | mada | card | apple_pay | split | credit).</summary>
public enum PosPaymentMethod
{
    Cash = 1,
    Mada = 2,
    Card = 3,
    ApplePay = 4,
    Split = 5,
    Credit = 6
}
