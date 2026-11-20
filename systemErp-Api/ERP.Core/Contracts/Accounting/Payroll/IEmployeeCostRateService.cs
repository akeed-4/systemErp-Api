using ERP.Core.DTOs.Accounting;

namespace ERP.Core.Contracts.Accounting;

/// <summary>
/// التكلفة المعيارية لساعة الموظف (فني في الورشة، مشغّل في التأجير): سجل يُضاف إليه بتاريخ سريان. الغرض يحدد الإضافة المطلوبة وشاشة
/// الصلاحية (الفني: إعدادات الورش، المشغّل: إعدادات التأجير). المستندات تثبّت التكلفة على سطرها عبر <see cref="RateAtAsync"/>.
/// </summary>
public interface IEmployeeCostRateService
{
    Task<List<EmployeeCostRateDto>> ListAsync(string purpose, Guid? employeeId, CancellationToken ct = default);
    Task<EmployeeCostRateDto> AddAsync(CreateEmployeeCostRateDto request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
    /// <summary>التكلفة السارية في التاريخ (أحدث سطر لا يتجاوزه)، أو null إن لم تُحدَّد. بلا فحص صلاحية: يستدعيه مستند يفحص صلاحيته.</summary>
    Task<decimal?> RateAtAsync(Guid employeeId, string purpose, DateTime date, CancellationToken ct = default);
    /// <summary>الموظفون النشطون للاختيار في الغرض (العرض على شاشة إعدادات الإضافة أو شاشة مستنداتها: أوامر العمل أو عقود التأجير).</summary>
    Task<List<AddOnStaffDto>> StaffAsync(string purpose, CancellationToken ct = default);
    /// <summary>يسجّل موظفاً أساسياً عبر خدمة الموظفين نفسها (التعديل على شاشة إعدادات الإضافة)، مع تكلفته المعيارية إن حُدِّدت.</summary>
    Task<AddOnStaffDto> RegisterStaffAsync(RegisterAddOnStaffDto request, CancellationToken ct = default);
}
