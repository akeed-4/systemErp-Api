using ERP.Domain.Common;

namespace ERP.Domain.Entities;

public class JournalEntryLine : BaseEntity
{
    public Guid JournalEntryId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? Notes { get; set; }
    public Guid? CostCenterId { get; set; }

    public virtual JournalEntry JournalEntry { get; set; } = null!;
}
