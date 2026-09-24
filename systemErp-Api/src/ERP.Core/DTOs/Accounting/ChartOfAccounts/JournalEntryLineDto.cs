namespace ERP.Core.DTOs.Accounting;

public partial class JournalEntryLineDto
{
    public Guid Id { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string? Notes { get; set; }
    public Guid? CostCenterId { get; set; }
}
