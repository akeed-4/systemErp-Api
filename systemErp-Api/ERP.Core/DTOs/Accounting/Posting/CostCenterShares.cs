namespace ERP.Core.DTOs.Accounting;

/// <summary>مبلغ لمركز تكلفة بعينه (فارغ = بلا مركز)، بالعملة الأساسية.</summary>
public record CostCenterAmount(Guid? CostCenterId, decimal Amount);

/// <summary>
/// نصيب مركز تكلفة من مستند مشتريات: صافيه، وما فيه من خدمات/مصروفات، وقيمة مخزونه الدفترية (فارغ = الصافي ناقص الخدمات).
/// فرق السعر لكل مركز = الصافي − الخدمات − المخزون.
/// </summary>
public record PurchaseCostCenterShare(Guid? CostCenterId, decimal Net, decimal Expense, decimal? Inventory);
