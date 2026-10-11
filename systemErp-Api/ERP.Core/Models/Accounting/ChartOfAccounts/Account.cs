
namespace ERP.Core.Models.Accounting;

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
    /// <summary>رمز تزامن (rowversion): ترحيلان متزامنان على الحساب نفسه لا يُضيِّع أحدهما أثر الآخر على الرصيد.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public virtual ICollection<Account> Children { get; set; } = new List<Account>();
}
