
namespace ERP.Core.Models.Accounting;

public class ContractClause : BaseEntity
{
    public Guid ContractId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsMandatory { get; set; }

    public virtual CommercialContract Contract { get; set; } = null!;
}
