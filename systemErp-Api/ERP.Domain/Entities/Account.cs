using ERP.Domain.Common;
using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public class Account : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public AccountCategory Type { get; set; }
    public string? ParentCode { get; set; }
    public int Level { get; set; }
    public decimal Balance { get; set; }
    public bool IsDebitNature { get; set; }
    public bool IsSystem { get; set; }
    public string? Currency { get; set; }
    public LinkedEntityType? LinkedEntityType { get; set; }
    public Guid? LinkedEntityId { get; set; }
    public string? Notes { get; set; }

    public virtual ICollection<Account> Children { get; set; } = new List<Account>();
}
