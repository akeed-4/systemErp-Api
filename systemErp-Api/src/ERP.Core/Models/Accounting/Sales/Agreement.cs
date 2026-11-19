
namespace ERP.Core.Models.Accounting;

/// <summary>اتفاقية إطارية (شراء أو بيع) تُستخدم كمرجع أسعار/كميات لأوامر (<see cref="CommercialOrder"/>) لاحقة.</summary>
public class Agreement : BaseEntity, IPartyDocument
{
    public string AgreementNumber { get; set; } = string.Empty;
    public string Type { get; set; } = "purchase"; // purchase | sales
    /// <summary>العميل/المورد المرتبط (اختياري) - يحدّد حساب الذمم عند الفوترة.</summary>
    public Guid? PartyId { get; set; }
    /// <summary>customer | supplier</summary>
    public string? PartyType { get; set; }
    /// <summary>نسخة PartyId المقيّدة بمفتاح أجنبي إلى Customer (تُعبّأ تلقائياً عند الحفظ).</summary>
    public Guid? CustomerId { get; set; }
    /// <summary>نسخة PartyId المقيّدة بمفتاح أجنبي إلى Supplier (تُعبّأ تلقائياً عند الحفظ).</summary>
    public Guid? SupplierId { get; set; }
    public bool IsSupplierParty => PartySide.IsSupplier(PartyType, Type == "purchase");
    public string PartyName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string Status { get; set; } = "active"; // active | expired | cancelled
    public string? Notes { get; set; }
    public string? ReferenceNo { get; set; }
    public string? PartyNameEn { get; set; }
    public string Currency { get; set; } = "SAR";

    public virtual ICollection<AgreementItem> Items { get; set; } = new List<AgreementItem>();
}
