using ERP.Api.Infrastructure;
using ERP.Core.Contracts.Accounting;
using ERP.Core.DTOs.Accounting;
using Microsoft.AspNetCore.Mvc;

namespace ERP.Api.Controllers.Accounting;

[Route("api/v1/vouchers"), RequireScreen("vouchers")]
public class VouchersController : ErpControllerBase
{
    private readonly IVoucherService _vouchers;
    public VouchersController(IVoucherService vouchers) => _vouchers = vouchers;

    [HttpGet] public async Task<IActionResult> List([FromQuery] PaginationParams q, CancellationToken ct) => Success(await _vouchers.ListAsync(q, ct));
    [HttpGet("{id:guid}")] public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Success(await _vouchers.GetAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateVoucherDto dto, CancellationToken ct)
        => Success(await _vouchers.CreateAsync(dto, ct), "تم إنشاء السند وترحيله");

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateVoucherDto dto, CancellationToken ct)
        => Success(await _vouchers.UpdateAsync(id, dto, ct), "تم تعديل السند وإعادة ترحيله");

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await _vouchers.DeleteAsync(id, ct); return Success("تم حذف السند وعكس قيده"); }
}
