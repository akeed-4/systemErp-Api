using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

/// <summary>
/// قائمة دول المنشأة: الإدارة بصلاحية البيانات الرئيسية، وقائمة الاختيار (الفعّالة) لأي مستخدم مسجّل لأنها تُستخدم في
/// نماذج كثيرة (العنوان الوطني، بيانات ZATCA للعميل والمورد، البنوك، المنشأة).
/// </summary>
[Route("api/v1/countries"), RequireScreen("master-data")]
public class CountriesController : CrudController<CountryDto, CreateCountryDto, UpdateCountryDto>
{
    private readonly ICountryService _countries;
    public CountriesController(ICountryService s) : base(s) => _countries = s;

    [HttpGet("lookup"), AuthenticatedOnly]
    public async Task<IActionResult> Lookup(CancellationToken ct) => Success(await _countries.LookupAsync(ct));
}
