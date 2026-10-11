using DevExtreme.AspNet.Data.ResponseModel;
using ERP.Core.DTOs.Accounting;
using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Accounting;

/// <summary>سلف الموظفين: صرف بقيد، خصم الأقساط من المسير، إعادة جدولة، وإلغاء ما لم يُسدَّد منه شيء.</summary>
public interface IEmployeeLoanService
{
    Task<LoadResult> LoadAsync(DataSourceLoadOptions options, CancellationToken ct = default);
    Task<EmployeeLoanDto> GetAsync(Guid id, CancellationToken ct = default);
    Task<EmployeeLoanDto> CreateAsync(CreateEmployeeLoanDto request, CancellationToken ct = default);
    Task<EmployeeLoanDto> RescheduleAsync(Guid id, RescheduleEmployeeLoanDto request, CancellationToken ct = default);
    /// <summary>يلغي سلفة لم يُخصم منها شيء ويعكس قيد صرفها.</summary>
    Task<EmployeeLoanDto> CancelAsync(Guid id, CancellationToken ct = default);
}

/// <summary>تصفية نهاية الخدمة: معاينة الحساب، ترحيله بقيد وإنهاء خدمة الموظف، وعكسه.</summary>
public interface IEndOfServiceService
{
    Task<EndOfServiceDto> PreviewAsync(EndOfServiceRequestDto request, CancellationToken ct = default);
    Task<EndOfServiceDto> PostAsync(EndOfServiceRequestDto request, CancellationToken ct = default);
    Task<LoadResult> LoadAsync(DataSourceLoadOptions options, CancellationToken ct = default);
    Task<EndOfServiceDto> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>يعكس القيد ويعيد الموظف إلى رأس العمل وسلفه المسوّاة إلى أرصدتها.</summary>
    Task<EndOfServiceDto> ReverseAsync(Guid id, CancellationToken ct = default);
}
