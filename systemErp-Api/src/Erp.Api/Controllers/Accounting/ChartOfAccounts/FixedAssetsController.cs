using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

[Route("api/v1/fixedassets"), RequireScreen("accounts")]
public class FixedAssetsController : CrudController<FixedAssetDto, CreateFixedAssetDto, UpdateFixedAssetDto>
{
    public FixedAssetsController(IFixedAssetService s) : base(s) { }
}
