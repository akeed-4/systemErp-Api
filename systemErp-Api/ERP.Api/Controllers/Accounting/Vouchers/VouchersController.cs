using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

[Route("api/v1/vouchers"), RequireScreen("vouchers")]
public class VouchersController : ErpControllerBase
{
    private readonly IVoucherService _vouchers;
    private readonly IPartyAgingService _aging;
    public VouchersController(IVoucherService vouchers, IPartyAgingService aging) { _vouchers = vouchers; _aging = aging; }

    /// <summary>الفواتير الآجلة المفتوحة لحساب عميل/مورد: يُوزَّع عليها السند عند السداد.</summary>
    [HttpGet("OpenInvoices")]
    public async Task<IActionResult> OpenInvoices([FromQuery] string partyAccountCode, CancellationToken ct)
        => Success(await _aging.OpenInvoicesAsync(partyAccountCode, ct));

    [HttpGet] public async Task<IActionResult> List([FromQuery] PaginationParams q, CancellationToken ct) => Success(await _vouchers.ListAsync(q, ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _vouchers.GetAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateVoucherDto dto, CancellationToken ct)
        => Success(await _vouchers.CreateAsync(dto, ct), Messages.VoucherCreatedAndPosted);

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateVoucherDto dto, CancellationToken ct)
        => Success(await _vouchers.UpdateAsync(id, dto, ct), Messages.VoucherUpdatedAndReposted);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await _vouchers.DeleteAsync(id, ct); return Success(Messages.VoucherDeletedAndReversed); }
}
