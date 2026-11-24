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

    /// <summary>الرمز المتوقع التالي تحت البنوك (110) أو الصناديق (111)؛ null لغيرهما. يُصرف الفعلي عند الحفظ برمز فارغ.</summary>
    [HttpGet("treasury-next-code/{parentCode}")]
    public async Task<IActionResult> TreasuryNextCode(string parentCode, CancellationToken ct)
        => Success(new { code = await _accounts.PeekTreasuryCodeAsync(parentCode, ct) });

    [HttpGet("ByCode/{code}")]
    public async Task<IActionResult> ByCode(string code, CancellationToken ct) => Success(await _accounts.GetByCodeAsync(code, ct));}
