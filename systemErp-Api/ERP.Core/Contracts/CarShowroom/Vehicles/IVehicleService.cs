using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.CarShowroom;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.CarShowroom;

public interface IVehicleService : ICrudService<VehicleDto, CreateVehicleDto, UpdateVehicleDto>
{
    /// <summary>حساب الضريبة وفق نمط الاحتساب (يطابق calculateVat في الواجهة).</summary>
    Task<CarVatResult> CalculateVatAsync(CalculateCarVatRequestDto request, CancellationToken ct = default);
    /// <summary>المركبات المتاحة للبيع (لمنتقي المركبات في عقد البيع).</summary>
    Task<List<VehicleDto>> ListAvailableAsync(string? search, CancellationToken ct = default);
    /// <summary>استيراد جماعي للمركبات (من Excel). صف فاشل لا يوقف البقية.</summary>
    Task<ImportResultDto> ImportAsync(List<CreateVehicleDto> items, CancellationToken ct = default);
    /// <summary>يُرجع أرقام الهيكل (VIN) الموجودة فعلاً من ضمن القائمة المُرسَلة — للتحقق من التكرار دون تحميل كل المركبات.</summary>
    Task<List<string>> FindExistingChassisNumbersAsync(List<string> vins, CancellationToken ct = default);
    /// <summary>يولّد أرقام شاسيه مؤقتة فريدة (صيغة VIN صحيحة تبدأ بـ TMP) لسيارات فاتورة الشراء، تُستبدل بالأرقام الحقيقية عند الاستلام.</summary>
    Task<List<string>> GenerateProvisionalVinsAsync(int count, CancellationToken ct = default);
    /// <summary>المركبات الواردة من أمر شراء محدد (بحث/فرز/صفحات) — لعرض دفعة الاستلام دون تحميل كل المركبات.</summary>
    Task<PagedResult<VehicleDto>> ListByProcurementOrderAsync(Guid orderId, PaginationParams p, CancellationToken ct = default);
}
