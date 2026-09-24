namespace ERP.Core.DTOs.Accounting;

public partial class ContractClauseDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsMandatory { get; set; }
}
