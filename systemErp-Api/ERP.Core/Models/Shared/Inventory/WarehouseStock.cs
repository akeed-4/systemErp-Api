namespace ERP.Core.Models.Shared;

/// <summary>
/// رصيد صنف في موقع. مجموع أرصدة الصنف في المواقع = Product.CurrentStock،
/// والتكلفة موحّدة على مستوى الصنف (لا تكلفة لكل موقع).
/// </summary>
public class WarehouseStock : BaseEntity
{
    public Guid ItemId { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>رمز تزامن (rowversion): حركتان متزامنتان على الرصيد نفسه لا تصرفانه مرتين.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
