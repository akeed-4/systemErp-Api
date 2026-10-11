using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class AgreementService : CrudService<Agreement, AgreementDto, CreateAgreementDto, UpdateAgreementDto>, IAgreementService
{
    private readonly INumberSequenceService _numbers;
    public AgreementService(ErpDbContext db, INumberSequenceService numbers) : base(db) => _numbers = numbers;

    protected override string Label => Messages.LabelAgreement;
    protected override bool Transactional => true;

    protected override IQueryable<Agreement> ApplySearch(IQueryable<Agreement> q, string t)
        => q.Where(a => a.AgreementNumber.Contains(t) || a.PartyName.Contains(t));

    protected override Task ValidateAsync(CreateAgreementDto d, Agreement? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        TradeHelper.RequireParty(d.PartyName, errors);
        if (d.Type is not ("purchase" or "sales")) errors.Add(Messages.AgreementTypeValues);
        if (d.Status is not ("draft" or "active" or "expired" or "cancelled" or "terminated")) errors.Add(Messages.AgreementStatusValues);
        if (d.EndDate.HasValue && d.EndDate < d.StartDate) errors.Add(Messages.EndDateBeforeStart);
        if (d.Items.Any(i => i.Quantity <= 0 || i.UnitPrice < 0)) errors.Add(Messages.AgreementQuantitiesAndPricesInvalid);
        if (d.Items.Any(i => i.Discount < 0 || i.VatRate is < 0 or > 100)) errors.Add(Messages.DiscountAndVatInvalid);
        if (d.Items.Any(i => i.MinQuantity < 0 || i.MaxQuantity < 0 || (i.MaxQuantity > 0 && i.MaxQuantity < i.MinQuantity)))
            errors.Add(Messages.OrderMinMaxInvalid);
        if (d.Items.GroupBy(i => i.ItemId).Any(g => g.Count() > 1)) errors.Add(Messages.DuplicateItemInAgreement);
        PreparePayments(d.Payments, errors);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
        return Task.CompletedTask;
    }

    /// <summary>
    /// جدول السداد اختياري؛ إن وُجد فكل دفعة بنسبة موجبة ومجموع النسب 100% بالضبط.
    /// الدفعات تُرقَّم 1..n بترتيب إرسالها، والدفعة غير المسمّاة تأخذ بياناً افتراضياً.
    /// </summary>
    private static void PreparePayments(List<AgreementPaymentDto> payments, List<string> errors)
    {
        if (payments.Count == 0) return;
        if (payments.Any(p => p.Percentage <= 0 || p.Percentage > 100)) errors.Add(Messages.AgreementPaymentPercentageRange);
        else if (payments.Sum(p => p.Percentage) != 100m) errors.Add(string.Format(Messages.AgreementPaymentsMustTotal100, payments.Sum(p => p.Percentage)));

        for (var i = 0; i < payments.Count; i++)
        {
            payments[i].Sequence = i + 1;
            payments[i].Description = string.IsNullOrWhiteSpace(payments[i].Description)
                ? string.Format(Messages.AgreementPaymentDefaultName, i + 1) : payments[i].Description!.Trim();
        }
    }

    protected override AgreementDto ToDto(Agreement e)
    {
        var dto = base.ToDto(e);
        dto.Payments = dto.Payments.OrderBy(p => p.Sequence).ToList();
        return dto;
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
                throw new ConflictException(string.Format(Messages.CannotRemoveItemWithApprovedOrders, removed.Entity.ItemName));
        foreach (var i in e.Items)
        {
            i.UsedQuantity = used.GetValueOrDefault(i.ItemId);
            if (i.Quantity < i.UsedQuantity)
                throw new ValidationFailedException(string.Format(Messages.AgreedQtyBelowConsumed, i.ItemName, i.UsedQuantity));
        }
    }

    protected override async Task OnDeletingAsync(Agreement e, CancellationToken ct)
    {
        if (await Db.Set<CommercialOrder>().AnyAsync(o => o.AgreementId == e.Id, ct))
            throw new ConflictException(Messages.CannotDeleteAgreementWithOrders);
    }
}
