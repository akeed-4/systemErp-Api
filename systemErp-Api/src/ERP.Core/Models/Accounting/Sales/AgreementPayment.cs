
namespace ERP.Core.Models.Accounting;

/// <summary>
/// دفعة في جدول سداد الاتفاقية (بيع أو شراء): نسبة من إجمالي الاتفاقية تستحق في موعدها.
/// مجموع نسب دفعات الاتفاقية = 100%.
/// </summary>
public class AgreementPayment : BaseEntity
{
    public Guid AgreementId { get; set; }
    /// <summary>ترتيب الدفعة (1، 2، ...) يضبطه الخادم.</summary>
    public int Sequence { get; set; }
    /// <summary>بيان الدفعة (مقدَّم، عند التوريد، ...).</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>نسبة الدفعة من إجمالي الاتفاقية (أكبر من 0 حتى 100).</summary>
    public decimal Percentage { get; set; }
    public DateTime? DueDate { get; set; }

    public virtual Agreement Agreement { get; set; } = null!;
}
