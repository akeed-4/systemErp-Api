using ERP.Core.DTOs.Shared;

namespace ERP.Core.Contracts.Shared;

public interface ICountryService : ICrudService<CountryDto, CreateCountryDto, UpdateCountryDto>
{
    /// <summary>الدول الفعّالة لقوائم الاختيار (بالاسم العربي)، وتُعبّأ قائمة المنشأة بدول العالم أول مرة.</summary>
    Task<List<CountryDto>> LookupAsync(CancellationToken ct = default);
}
