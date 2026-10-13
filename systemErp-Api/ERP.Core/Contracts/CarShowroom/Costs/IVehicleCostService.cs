using ERP.Core.DTOs.CarShowroom;

namespace ERP.Core.Contracts.CarShowroom;

public interface IVehicleCostService
{
    Task<VehicleCostCardDto> GetCostCardAsync(Guid vehicleId, CancellationToken ct = default);
    /// <summary>قيود التعديل الموثقة على تكلفة السيارة بعد بيعها.</summary>
    Task<List<VehicleCostAdjustmentDto>> GetAdjustmentsAsync(Guid vehicleId, CancellationToken ct = default);
    /// <summary>التكلفة المتوقعة الكلية لأمر شراء: ثمن سياراته + مصاريفه المُرسمَلة التقديرية والفعلية.</summary>
    Task<ProcurementExpectedCostDto> GetProcurementExpectedCostAsync(Guid orderId, CancellationToken ct = default);
    /// <summary>
    /// يحوّل أرصدة حقول التكلفة القديمة (تكاليف إضافية/تجهيز) لكل مركبات المنشأة إلى أسطر «رصيد افتتاحي» دون تغيير تكلفة أي مركبة.
    /// آمن للتشغيل أكثر من مرة: لا يُنشئ إلا الفرق غير المغطّى بأسطر. يُرجع عدد الأسطر المُنشأة.
    /// </summary>
    Task<int> MigrateLegacyCostsAsync(CancellationToken ct = default);
}
