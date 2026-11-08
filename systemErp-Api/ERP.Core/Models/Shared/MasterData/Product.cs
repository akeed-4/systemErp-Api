
namespace ERP.Core.Models.Shared;

public class Product : BaseEntity
{
    public string Sku { get; set; } = string.Empty;
    public string? Barcode { get; set; }
    /// <summary>مفتاح مفهرس للبحث الدقيق في الباركود؛ يحافظ على قيمة الباركود الأصلية كاملة.</summary>
    public string? BarcodeLookupKey { get; private set; }
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    /// <summary>طبيعة الصنف (<see cref="ProductItemTypes"/>): بضاعة، خدمي، مجمّع، مركّب، منتج صناعي.</summary>
    public string ItemType { get; set; } = ProductItemTypes.Stock;
    public string Category { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal CurrentStock { get; set; }
    public decimal AverageCost { get; set; }
    public decimal LastPurchaseCost { get; set; }
    public decimal? StandardCost { get; set; }
    /// <summary>وزن الوحدة بالكيلوجرام: أساس توزيع التكاليف الإضافية للمشتريات بالوزن.</summary>
    public decimal? WeightKg { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal VatRate { get; set; } = 15m;
    /// <summary>نسبة الضريبة الانتقائية المعتمدة للصنف؛ الصفر يعني أن الصنف غير مفعّل عليه الانتقائية.</summary>
    public decimal ExciseTaxRate { get; set; }
    /// <summary>سعر التجزئة المحدد للسلعة قبل الانتقائية وضريبة القيمة المضافة.</summary>
    public decimal ExciseRetailPrice { get; set; }
    /// <summary>السعر القياسي المعتمد للسلعة قبل الانتقائية وضريبة القيمة المضافة.</summary>
    public decimal ExciseStandardPrice { get; set; }
    /// <summary>التصنيف الضريبي للصنف (خاضع / صفري / معفى / خارج النطاق): يُحدَّد عند تعريف الصنف ويتبعه في كل فاتورة.</summary>
    public VatCategory VatCategory { get; set; } = VatCategory.Standard;
    /// <summary>سبب الصفرية/الإعفاء برمز الهيئة (VATEX-SA-…)؛ إلزامي لغير الخاضع.</summary>
    public string? VatExemptionReasonCode { get; set; }
    /// <summary>تتبّع التشغيلات (Lot/Batch): كل وارد برقم تشغيلة، وكل صادر يُخصَّص من تشغيلات المستودع (FEFO افتراضياً).</summary>
    public bool TrackBatches { get; set; }
    /// <summary>تاريخ صلاحية إلزامي لكل تشغيلة واردة، ويُمنع بيع المنتهي (يتطلب تتبّع التشغيلات).</summary>
    public bool TrackExpiry { get; set; }
    /// <summary>مدة الصلاحية بالأيام من تاريخ الإنتاج: تقترح تاريخ الانتهاء عند الاستلام (اختياري).</summary>
    public int? ShelfLifeDays { get; set; }
    /// <summary>التنبيه قبل انتهاء الصلاحية بعدد أيام (تقرير قرب الانتهاء).</summary>
    public int ExpiryAlertDays { get; set; } = 30;
    public decimal MinStockLevel { get; set; }
    public string? Notes { get; set; }
    /// <summary>رمز تزامن (rowversion): حركتان متزامنتان على الصنف نفسه لا تبيعان الرصيد مرتين ولا تُفسدان متوسط التكلفة.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
