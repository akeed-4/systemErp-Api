using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

/// <summary>
/// التسوية اليدوية للمخزون (إضافة/خصم) بأثرها الكامل: حركة مخزون وقيد محاسبي بقيمتها، فلا تتغيّر قيمة المخزون
/// دون أن يتغيّر حسابه في الدفاتر. التعديل والحذف يعكسان القيد ويعيدان احتساب الصنف.
/// </summary>
public interface IStockAdjustmentService
{
    Task<StockMovementDto> AdjustAsync(RecordStockMovementDto request, CancellationToken ct = default);
    Task<StockMovementDto> UpdateAsync(Guid id, RecordStockMovementDto request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
