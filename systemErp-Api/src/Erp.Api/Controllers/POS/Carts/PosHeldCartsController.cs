using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.POS;
using ERP.Core.DTOs.POS;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.POS;

[Route("api/v1/pos/held-carts"), RequireScreen("sales")]
public class PosHeldCartsController : CrudController<PosHeldCartDto, CreatePosHeldCartDto, UpdatePosHeldCartDto>
{
    public PosHeldCartsController(IPosHeldCartService s) : base(s) { }
}
