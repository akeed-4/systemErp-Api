using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

[Route("api/v1/agreements"), RequireScreen("sales")]
public class AgreementsController : CrudController<AgreementDto, CreateAgreementDto, UpdateAgreementDto>
{
    public AgreementsController(IAgreementService s) : base(s) { }
}
