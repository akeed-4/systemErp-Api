using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

[Route("api/v1/accounts"), RequireScreen("accounts")]
public class AccountsController : CrudController<AccountDto, CreateAccountDto, UpdateAccountDto>
{
    private readonly IAccountService _accounts;
    public AccountsController(IAccountService accounts) : base(accounts) => _accounts = accounts;

    [HttpGet("tree")]
    public async Task<IActionResult> Tree(CancellationToken ct) => Success(await _accounts.GetTreeAsync(ct));

    [HttpGet("by-code/{code}")]
    public async Task<IActionResult> ByCode(string code, CancellationToken ct) => Success(await _accounts.GetByCodeAsync(code, ct));
}
