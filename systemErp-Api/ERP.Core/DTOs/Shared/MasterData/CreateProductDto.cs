namespace ERP.Core.DTOs.Shared;

public partial class CreateProductDto
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
    /// <summary>وزن الوحدة بالكيلوجرام: أساس توزيع التكاليف الإضافية للمشتريات بالوزن.</summary>
    public decimal? WeightKg { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal VatRate { get; set; } = 15m;
    public decimal ExciseTaxRate { get; set; }
    public decimal ExciseRetailPrice { get; set; }
    public decimal ExciseStandardPrice { get; set; }
    /// <summary>التصنيف الضريبي للصنف: Standard | ZeroRated | Exempt | OutOfScope.</summary>
    public VatCategory VatCategory { get; set; } = VatCategory.Standard;
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
}
