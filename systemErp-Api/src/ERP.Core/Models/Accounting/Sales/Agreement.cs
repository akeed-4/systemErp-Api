
namespace ERP.Core.Models.Accounting;

/// <summary>اتفاقية إطارية (شراء أو بيع) تُستخدم كمرجع أسعار/كميات لأوامر (<see cref="CommercialOrder"/>) لاحقة.</summary>
public class Agreement : BaseEntity
{
    public string AgreementNumber { get; set; } = string.Empty;
    public string Type { get; set; } = "purchase"; // purchase | sales
    /// <summary>العميل/المورد المرتبط (اختياري) - يحدّد حساب الذمم عند الفوترة.</summary>
    public Guid? PartyId { get; set; }
    /// <summary>customer | supplier</summary>
    public string? PartyType { get; set; }
    public string PartyName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string Status { get; set; } = "active"; // active | expired | cancelled
    public string? Notes { get; set; }

    public virtual ICollection<AgreementItem> Items { get; set; } = new List<AgreementItem>();
}
