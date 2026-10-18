namespace ERP.Core.Models.Shared;

public sealed class ContractStageDefinition
{
    public string Id { get; set; } = string.Empty;
    public string WorkflowType { get; set; } = string.Empty;
    public int Number { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string DescAr { get; set; } = string.Empty;
    public string DescEn { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string BadgeColor { get; set; } = string.Empty;
    public string HeaderColor { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}