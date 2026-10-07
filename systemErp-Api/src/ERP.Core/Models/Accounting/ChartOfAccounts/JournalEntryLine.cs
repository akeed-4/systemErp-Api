
namespace ERP.Core.Models.Accounting;

public class JournalEntryLine : BaseEntity
{
    public Guid JournalEntryId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? Notes { get; set; }
    public Guid? CostCenterId { get; set; }
    /// <summary>التسوية البنكية التي طابقت هذه الحركة مع كشف البنك (لحسابات النقدية والبنوك).</summary>
    public Guid? BankReconciliationId { get; set; }

    public virtual JournalEntry JournalEntry { get; set; } = null!;
}
