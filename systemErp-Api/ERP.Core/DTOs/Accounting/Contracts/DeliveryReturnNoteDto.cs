namespace ERP.Core.DTOs.Accounting;

public partial class DeliveryReturnNoteDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string ReturnNumber { get; set; } = string.Empty;
    public string Type { get; set; } = "sales_delivery_return";
    public Guid DeliveryNoteId { get; set; }
    public string DeliveryNumber { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public string Status { get; set; } = "draft";
    public Guid? ContractId { get; set; }
    public string? ContractNumber { get; set; }
    public string PartyName { get; set; } = string.Empty;
    public string? ReturnReason { get; set; }
    public string? Notes { get; set; }
    public decimal Subtotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public List<DeliveryReturnItemDto> Items { get; set; } = new();
}
