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
        if (d.Status is not ("draft" or "active" or "expired" or "cancelled" or "terminated")) errors.Add("الحالة: draft | active | expired | cancelled | terminated.");
        if (d.EndDate.HasValue && d.EndDate < d.StartDate) errors.Add("تاريخ النهاية قبل البداية.");
        if (d.Items.Any(i => i.Quantity <= 0 || i.UnitPrice < 0)) errors.Add("كميات الاتفاقية موجبة والأسعار غير سالبة.");
        if (d.Items.Any(i => i.Discount < 0 || i.VatRate is < 0 or > 100)) errors.Add("الخصم غير سالب ونسبة الضريبة بين 0 و100.");
        if (d.Items.Any(i => i.MinQuantity < 0 || i.MaxQuantity < 0 || (i.MaxQuantity > 0 && i.MaxQuantity < i.MinQuantity)))
            errors.Add("الحد الأدنى والأقصى للأمر غير سالبين، والأقصى (إن حُدِّد) لا يقل عن الأدنى.");
        if (d.Items.GroupBy(i => i.ItemId).Any(g => g.Count() > 1)) errors.Add("الصنف مكرر في بنود الاتفاقية.");
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
        return Task.CompletedTask;
    }

    protected override async Task OnCreatingAsync(Agreement e, CreateAgreementDto d, CancellationToken ct)
    {
        e.AgreementNumber = await _numbers.NextAsync("agreement", "AGR-", ct);
        foreach (var i in e.Items) i.UsedQuantity = 0; // لا استهلاك قبل وجود أوامر
    }

    protected override async Task OnUpdatingAsync(Agreement e, UpdateAgreementDto d, CancellationToken ct)
    {
        e.AgreementNumber = Db.Entry(e).OriginalValues.GetValue<string>(nameof(Agreement.AgreementNumber));
        // الاستهلاك يُشتق من الأوامر المعتمدة، لا مما يرسله العميل.
        var used = await AgreementUsage.UsedByItemAsync(Db, e.Id, null, ct);
        foreach (var removed in Db.ChangeTracker.Entries<AgreementItem>().Where(x => x.State == EntityState.Deleted))
            if (used.GetValueOrDefault(removed.Entity.ItemId) > 0)
                throw new ConflictException($"لا يمكن حذف الصنف {removed.Entity.ItemName}: عليه أوامر معتمدة.");
        foreach (var i in e.Items)
        {
            i.UsedQuantity = used.GetValueOrDefault(i.ItemId);
            if (i.Quantity < i.UsedQuantity)
                throw new ValidationFailedException($"الكمية المتفق عليها للصنف {i.ItemName} أقل من المستهلك فعلاً ({i.UsedQuantity:0.####}).");
        }
    }

    protected override async Task OnDeletingAsync(Agreement e, CancellationToken ct)
    {
        if (await Db.Set<CommercialOrder>().AnyAsync(o => o.AgreementId == e.Id, ct))
            throw new ConflictException("لا يمكن حذف اتفاقية مرتبطة بأوامر.");
    }
}
