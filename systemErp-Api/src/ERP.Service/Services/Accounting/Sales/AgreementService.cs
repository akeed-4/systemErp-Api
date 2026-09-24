using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class AgreementService : CrudService<Agreement, AgreementDto, CreateAgreementDto, UpdateAgreementDto>, IAgreementService
{
    private readonly INumberSequenceService _numbers;
    public AgreementService(ErpDbContext db, INumberSequenceService numbers) : base(db) => _numbers = numbers;

    protected override string Label => "الاتفاقية";
    protected override bool Transactional => true;

    protected override IQueryable<Agreement> ApplySearch(IQueryable<Agreement> q, string t)
        => q.Where(a => a.AgreementNumber.Contains(t) || a.PartyName.Contains(t));

    protected override Task ValidateAsync(CreateAgreementDto d, Agreement? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        TradeHelper.RequireParty(d.PartyName, errors);
        if (d.Type is not ("purchase" or "sales")) errors.Add("النوع: purchase | sales.");
        if (d.Status is not ("active" or "expired" or "cancelled")) errors.Add("الحالة: active | expired | cancelled.");
        if (d.EndDate.HasValue && d.EndDate < d.StartDate) errors.Add("تاريخ النهاية قبل البداية.");
        if (d.Items.Any(i => i.Quantity <= 0 || i.UnitPrice < 0)) errors.Add("كميات الاتفاقية موجبة والأسعار غير سالبة.");
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
        return Task.CompletedTask;
    }

    protected override async Task OnCreatingAsync(Agreement e, CreateAgreementDto d, CancellationToken ct)
        => e.AgreementNumber = await _numbers.NextAsync("agreement", "AGR-", ct);

    protected override Task OnUpdatingAsync(Agreement e, UpdateAgreementDto d, CancellationToken ct)
    {
        e.AgreementNumber = Db.Entry(e).OriginalValues.GetValue<string>(nameof(Agreement.AgreementNumber));
        return Task.CompletedTask;
    }
}
