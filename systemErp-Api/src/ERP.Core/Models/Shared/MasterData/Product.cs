
namespace ERP.Core.Models.Shared;

public class Product : BaseEntity
{
    public string Sku { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal CurrentStock { get; set; }
    public decimal AverageCost { get; set; }
    public decimal LastPurchaseCost { get; set; }
    public decimal? StandardCost { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal VatRate { get; set; } = 15m;
    /// <summary>التصنيف الضريبي للصنف (خاضع / صفري / معفى / خارج النطاق): يُحدَّد عند تعريف الصنف ويتبعه في كل فاتورة.</summary>
    public VatCategory VatCategory { get; set; } = VatCategory.Standard;
    /// <summary>سبب الصفرية/الإعفاء برمز الهيئة (VATEX-SA-…)؛ إلزامي لغير الخاضع.</summary>
    public string? VatExemptionReasonCode { get; set; }
    public decimal MinStockLevel { get; set; }
    public string? Notes { get; set; }
    /// <summary>رمز تزامن (rowversion): حركتان متزامنتان على الصنف نفسه لا تبيعان الرصيد مرتين ولا تُفسدان متوسط التكلفة.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
