namespace ERP.Core.DTOs.Accounting;

/// <summary>مبلغ لمركز تكلفة بعينه (فارغ = بلا مركز)، بالعملة الأساسية.</summary>
/// <remarks>WarehouseId: موقع المبلغ لحسابي مخزونه وتكلفته (فارغ = موقع الطلب).</remarks>
public record CostCenterAmount(Guid? CostCenterId, decimal Amount, Guid? WarehouseId = null);

/// <summary>
/// نصيب مركز تكلفة من مستند مشتريات: صافيه، وما فيه من خدمات/مصروفات، وقيمة مخزونه الدفترية (فارغ = الصافي ناقص الخدمات).
/// فرق السعر لكل مركز = الصافي − الخدمات − المخزون.
/// </summary>
/// <remarks>WarehouseId: موقع الحصة لحساب مخزونها (فارغ = موقع الطلب).</remarks>
public record PurchaseCostCenterShare(Guid? CostCenterId, decimal Net, decimal Expense, decimal? Inventory, Guid? WarehouseId = null);
