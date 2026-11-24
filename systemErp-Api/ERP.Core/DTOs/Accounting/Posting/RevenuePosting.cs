namespace ERP.Core.DTOs.Accounting;

/// <summary>
/// جزء من إيراد المستند على حساب ومركز تكلفة بعينهما (حساب فارغ = حساب إيراد المستند ثم مبيعات موقع الجزء ثم الافتراضي).
/// WarehouseId: موقع الجزء (فارغ = موقع الطلب).
/// </summary>
public record RevenuePosting(string? AccountCode, Guid? CostCenterId, decimal Amount, Guid? WarehouseId = null);
