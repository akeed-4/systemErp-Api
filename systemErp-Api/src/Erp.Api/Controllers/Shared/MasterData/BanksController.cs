using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

[Route("api/v1/banks"), RequireScreen("master-data")]
public class BanksController : CrudController<BankEntityDto, CreateBankEntityDto, UpdateBankEntityDto>
{
    public BanksController(IBankService s) : base(s) { }
}
