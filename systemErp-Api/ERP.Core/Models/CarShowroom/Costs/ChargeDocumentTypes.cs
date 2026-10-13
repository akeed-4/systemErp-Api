namespace ERP.Core.Models.CarShowroom;

/// <summary>المستندات التي يُربط بها سطر المصروف (AdditionalChargeLine.DocumentType).</summary>
public static class ChargeDocumentTypes
{
    public const string ProcurementOrder = "car_procurement_order";
    public const string SalesContract = "car_sales_contract";
    public const string Invoice = "invoice";
    /// <summary>مصروف مسجَّل على السيارة مباشرة دون مستند.</summary>
    public const string Vehicle = "vehicle";
    /// <summary>أسطر يُنشئها اعتماد سند التكاليف الإضافية (لا تُنشأ يدوياً).</summary>
    public const string LandedCostVoucher = "landed_cost_voucher";
    /// <summary>مصروف عملية تم على سيارة ليست في النظام (استعلام قبل الشراء).</summary>
    public const string TammOperation = "tamm_operation";
    public static readonly string[] All = { ProcurementOrder, SalesContract, Invoice, Vehicle };
}
