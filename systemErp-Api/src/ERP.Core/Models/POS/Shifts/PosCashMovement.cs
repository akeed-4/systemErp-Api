namespace ERP.Core.Models.POS;

/// <summary>حركة نقدية على درج الكاشير خارج البيع (إيداع/صرف) — تدخل في النقد المتوقع وتُرحَّل بقيد.</summary>
public class PosCashMovement : BaseEntity
{
    public Guid ShiftId { get; set; }
    public PosCashMovementType Type { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    /// <summary>الحساب المقابل للصندوق (صرف: مصروف نثري افتراضياً؛ إيداع: البنك افتراضياً).</summary>
    public string CounterAccountCode { get; set; } = string.Empty;
    public Guid? JournalEntryId { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
}
