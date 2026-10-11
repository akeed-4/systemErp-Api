namespace ERP.Core.Models.Shared;

/// <summary>
/// رصيد صنف في مستودع. مجموع أرصدة الصنف في المستودعات = Product.CurrentStock،
/// والتكلفة موحّدة على مستوى الصنف (لا تكلفة لكل مستودع).
/// </summary>
public class WarehouseStock : BaseEntity
{
    public Guid ItemId { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>رمز تزامن (rowversion): حركتان متزامنتان على الرصيد نفسه لا تصرفانه مرتين.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
