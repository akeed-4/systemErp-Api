namespace ERP.Core.DTOs.CarShowroom;

/// <summary>شراء مركبة واحدة بخطوة واحدة (شاشة دورات الشراء): يُنشئ أمر شراء ببند واحد ويمرّره بمراحله حتى فاتورة الشراء والقيد.</summary>
public class QuickCarPurchaseRequestDto
{
    /// <summary>individual | corporate | bank_lease</summary>
    public string PurchaseCycle { get; set; } = "individual";
    public Guid SupplierId { get; set; }
    public DateTime? Date { get; set; }

    /// <summary>cash | credit | bank_lc | advance_milestone</summary>
    public string PaymentType { get; set; } = "cash";
    public int? CreditDays { get; set; }
    public string Currency { get; set; } = "SAR";
    public decimal ExchangeRate { get; set; } = 1;
    public string? FinancingBankName { get; set; }
    public string? BankApprovalNumber { get; set; }

    // بيانات المركبة المشتراة
    public string BrandName { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string? TrimName { get; set; }
    public int Year { get; set; }
    public string? ColorExterior { get; set; }
    public string? ColorInterior { get; set; }
    public string? FuelType { get; set; }
    public string? Transmission { get; set; }
    public string Vin { get; set; } = string.Empty;
    public string? EngineNumber { get; set; }
    public string? CustomsCardNumber { get; set; }

    /// <summary>سعر الشراء قبل الضريبة.</summary>
    public decimal PurchasePrice { get; set; }
    public decimal? SellingPrice { get; set; }
    public string? WarehouseLocation { get; set; }
    public string? SupplierInvoiceNumber { get; set; }
    public DateTime? SupplierInvoiceDate { get; set; }
    public string? Notes { get; set; }
}
