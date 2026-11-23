using ERP.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Shared;

/// <summary>فروع النشاط (المستوى بين النشاط والمواقع).</summary>
[Route("api/v1/branches"), RequireScreen("branches")]
public class BranchesController : CrudController<BranchDto, CreateBranchDto, UpdateBranchDto>
{
    public BranchesController(IBranchService s) : base(s) { }
}
