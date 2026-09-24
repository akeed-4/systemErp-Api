
namespace ERP.Core.Models.Accounting;

public class AgreementItem : BaseEntity
{
    public Guid AgreementId { get; set; }
    public Guid ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    public virtual Agreement Agreement { get; set; } = null!;
}
