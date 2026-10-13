using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.CarShowroom;
using ERP.Core.DTOs.CarShowroom;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.CarShowroom;

[Route("api/v1/expensetypes"), RequireScreen("car-showroom")]
[RequireModule(PlatformModules.CarShowroom)]
public class ExpenseTypesController : CrudController<ExpenseTypeDto, CreateExpenseTypeDto, UpdateExpenseTypeDto>
{
    public ExpenseTypesController(IExpenseTypeService s) : base(s) { }
}
