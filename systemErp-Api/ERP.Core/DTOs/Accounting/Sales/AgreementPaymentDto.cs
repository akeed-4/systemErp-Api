namespace ERP.Core.DTOs.Accounting;

/// <summary>دفعة في جدول سداد الاتفاقية: نسبة من إجمالي الاتفاقية وموعد استحقاقها.</summary>
public partial class AgreementPaymentDto
{
    public Guid Id { get; set; }
    public int Sequence { get; set; }
    /// <summary>بيان الدفعة؛ فارغ = «دفعة n».</summary>
    public string? Description { get; set; }
    public decimal Percentage { get; set; }
    public DateTime? DueDate { get; set; }
}
