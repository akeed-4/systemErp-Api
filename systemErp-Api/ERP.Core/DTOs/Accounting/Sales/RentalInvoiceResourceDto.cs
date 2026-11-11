namespace ERP.Core.DTOs.Accounting;

public class RentalInvoiceResourceDto
{
    public Guid Id { get; set; }
    public string ResourceName { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string? ProjectNameEn { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public Guid CostCenterId { get; set; }
}
