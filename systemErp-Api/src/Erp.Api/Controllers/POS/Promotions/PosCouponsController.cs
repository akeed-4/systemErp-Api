using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.POS;
using ERP.Core.DTOs.POS;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.POS;

[Route("api/v1/pos/coupons"), RequireScreen("sales")]
public class PosCouponsController : CrudController<PosCouponDto, CreatePosCouponDto, UpdatePosCouponDto>
{
    private readonly IPosCouponService _coupons;
    public PosCouponsController(IPosCouponService s) : base(s) => _coupons = s;

    [HttpPost("validate")]
    [RequireScreen("sales", ScreenAction.View)]
    public async Task<IActionResult> Validate([FromBody] CouponValidationRequestDto dto, CancellationToken ct) => Success(await _coupons.ValidateAsync(dto, ct));
}
