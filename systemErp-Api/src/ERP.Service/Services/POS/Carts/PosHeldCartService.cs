using ERP.Core.Contracts.POS;
using ERP.Core.DTOs.POS;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.POS;

public class PosHeldCartService : CrudService<PosHeldCart, PosHeldCartDto, CreatePosHeldCartDto, UpdatePosHeldCartDto>, IPosHeldCartService
{
    private readonly INumberSequenceService _numbers;
    public PosHeldCartService(ErpDbContext db, INumberSequenceService numbers) : base(db) => _numbers = numbers;
    protected override string Label => "السلة المعلّقة";
    protected override bool Transactional => true;

    protected override Task ValidateAsync(CreatePosHeldCartDto d, PosHeldCart? existing, CancellationToken ct)
    {
        if (d.Items.Count == 0) throw new ValidationFailedException("السلة فارغة.");
        if (d.Items.Any(i => i.Quantity <= 0 || i.UnitPrice < 0 || i.Discount < 0)) throw new ValidationFailedException("كميات وأسعار السلة غير صالحة.");
        return Task.CompletedTask;
    }

    protected override async Task OnCreatingAsync(PosHeldCart e, CreatePosHeldCartDto d, CancellationToken ct)
        => e.CartReference = await _numbers.NextAsync("pos_hold", "HOLD-", ct);

    protected override Task OnUpdatingAsync(PosHeldCart e, UpdatePosHeldCartDto d, CancellationToken ct)
    {
        e.CartReference = Db.Entry(e).OriginalValues.GetValue<string>(nameof(PosHeldCart.CartReference));
        return Task.CompletedTask;
    }
}
