namespace ERP.Core.Models.POS;

public enum PosCashMovementType
{
    PaidIn = 1,   // إيداع نقدي في الدرج (فكّة، تغذية من البنك)
    PaidOut = 2   // صرف نقدي من الدرج (مصروف نثري، سحب للبنك)
}
