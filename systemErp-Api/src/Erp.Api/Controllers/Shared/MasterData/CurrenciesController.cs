using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/currencies"), RequireScreen("master-data")]
public class CurrenciesController : CrudController<CurrencyDto, CreateCurrencyDto, UpdateCurrencyDto>
{
    public CurrenciesController(ICurrencyService s) : base(s) { }
}
