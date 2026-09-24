using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Shared;
using ERP.Core.Contracts.POS;
using ERP.Core.DTOs.POS;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.POS;

[Route("api/v1/pos/offers"), RequireScreen("sales")]
public class PosOffersController : CrudController<PosOfferDto, CreatePosOfferDto, UpdatePosOfferDto>
{
    public PosOffersController(IPosOfferService s) : base(s) { }
}
