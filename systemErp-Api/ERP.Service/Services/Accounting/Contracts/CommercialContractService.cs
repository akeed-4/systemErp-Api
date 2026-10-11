using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class CommercialContractService : CrudService<CommercialContract, CommercialContractDto, CreateCommercialContractDto, UpdateCommercialContractDto>, ICommercialContractService
{
    public static readonly string[] StandardStages =
        { "draft", "legal_review", "approved_signed", "active_execution", "milestone_billing", "initial_inspection", "final_closed" };
    public static readonly string[] NormalPurchasingStages =
        { "normal_agreement", "normal_delivery", "normal_delivery_return", "normal_milestone_invoice" };

    private readonly INumberSequenceService _numbers;
    private readonly IInvoiceService _invoices;

    public CommercialContractService(ErpDbContext db, INumberSequenceService numbers, IInvoiceService invoices) : base(db)
    {
        _numbers = numbers; _invoices = invoices;
    }

    protected override string Label => Messages.LabelContract;
    protected override bool Transactional => true;

    private static string[] StagesFor(string contractType) => contractType == "normal_purchasing" ? NormalPurchasingStages : StandardStages;

    protected override IQueryable<CommercialContract> ApplySearch(IQueryable<CommercialContract> q, string t)
        => q.Where(c => c.ContractNumber.Contains(t) || c.Title.Contains(t) || c.PartyName.Contains(t));

    protected override IQueryable<CommercialContract> ApplyFilters(IQueryable<CommercialContract> q, PaginationParams p)
        => string.IsNullOrWhiteSpace(p.Status) ? q : q.Where(c => c.Stage == p.Status);

    protected override Task ValidateAsync(CreateCommercialContractDto d, CommercialContract? existing, CancellationToken ct)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Title)) errors.Add(Messages.ContractTitleRequired);
        TradeHelper.RequireParty(d.PartyName, errors);
        if (d.ContractValue < 0) errors.Add(Messages.ContractValueCannotBeNegative);
        if (d.VatRate is < 0 or > 100) errors.Add(Messages.VatRateRange);
        if (d.StartDate.HasValue && d.EndDate.HasValue && d.EndDate < d.StartDate) errors.Add(Messages.EndDateBeforeStart);
        if (!string.IsNullOrWhiteSpace(d.PartyVatNumber) && !SaudiVat.IsValid(d.PartyVatNumber)) errors.Add(Messages.PartyVatNumberInvalid);
        if (d.Milestones.Sum(m => m.Percentage) > 100.0001m) errors.Add(Messages.MilestonePercentagesExceed100);
        if (d.Milestones.Any(m => m.Percentage < 0 || m.Amount < 0)) errors.Add(Messages.MilestoneAmountsCannotBeNegative);
        if (d.Milestones.Any(m => m.RetentionDeductionPercent is < 0 or > 100)) errors.Add(Messages.RetentionRateRange);
        if (d.RetentionPercent is < 0 or > 100) errors.Add(Messages.PerformanceBondRateRange);
        if (d.MaxPenaltyPercent is < 0 or > 100) errors.Add(Messages.MaxPenaltyRange);
        if (d.LatePenaltyPerDay < 0) errors.Add(Messages.DailyPenaltyCannotBeNegative);
        if (d.DurationMonths is < 1) errors.Add(Messages.ContractMinimumOneMonth);
        if (d.Items.Any(i => i.Quantity <= 0 || i.UnitPrice < 0 || i.VatRate is < 0 or > 100)) errors.Add(Messages.ContractItemsInvalid);
        if (existing != null && existing.Stage == "final_closed") errors.Add(Messages.CannotEditClosedContract);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
        return Task.CompletedTask;
    }

    protected override async Task OnCreatingAsync(CommercialContract e, CreateCommercialContractDto d, CancellationToken ct)
    {
        e.ContractNumber = await _numbers.NextAsync("commercial_contract", "CON-", ct);
        e.Stage = StagesFor(e.ContractType)[0];
        e.Status = ContractStatus.Draft;
        e.TotalInvoiced = 0; e.TotalCollected = 0;
        Recalculate(e);
    }

    protected override Task OnUpdatingAsync(CommercialContract e, UpdateCommercialContractDto d, CancellationToken ct)
    {
        var o = Db.Entry(e).OriginalValues;
        e.ContractNumber = o.GetValue<string>(nameof(CommercialContract.ContractNumber));
        e.Stage = o.GetValue<string>(nameof(CommercialContract.Stage));
        e.Status = o.GetValue<ContractStatus>(nameof(CommercialContract.Status));
        e.TotalInvoiced = o.GetValue<decimal>(nameof(CommercialContract.TotalInvoiced));
        e.TotalCollected = o.GetValue<decimal>(nameof(CommercialContract.TotalCollected));

        // المستخلصات المفوترة تُقفل: لا تعديل ولا حذف.
        foreach (var entry in Db.ChangeTracker.Entries<ContractMilestone>())
        {
            var wasBilled = entry.State != EntityState.Added
                && entry.OriginalValues.GetValue<ContractMilestoneStatus>(nameof(ContractMilestone.Status)) is ContractMilestoneStatus.Invoiced or ContractMilestoneStatus.Paid;
            if (!wasBilled) continue;
            if (entry.State == EntityState.Deleted) throw new ConflictException(Messages.CannotDeleteBilledMilestone);
            if (entry.State == EntityState.Modified) entry.CurrentValues.SetValues(entry.OriginalValues);
        }
        Recalculate(e);
        return Task.CompletedTask;
    }

    protected override async Task OnDeletingAsync(CommercialContract e, CancellationToken ct)
    {
        if (e.TotalInvoiced > 0 || await Db.Set<ContractMilestone>().AnyAsync(m => m.ContractId == e.Id && m.InvoiceId != null, ct))
            throw new ConflictException(Messages.CannotDeleteContractWithBilledMilestones);
        if (await Db.Set<DeliveryNote>().AnyAsync(n => n.ContractId == e.Id, ct))
            throw new ConflictException(Messages.CannotDeleteContractWithDeliveryNotes);
    }

    private static void Recalculate(CommercialContract c)
    {
        c.VatAmount = DocumentPricing.Round(c.ContractValue * c.VatRate / 100m);
        c.TotalValueWithVat = c.ContractValue + c.VatAmount;
        c.RetentionAmount = DocumentPricing.Round(c.ContractValue * c.RetentionPercent / 100m);
        foreach (var i in c.Items)
            i.TotalWithVat = DocumentPricing.Round(i.Quantity * i.UnitPrice * (1 + i.VatRate / 100m));
        var n = 1;
        foreach (var m in c.Milestones.OrderBy(m => m.MilestoneNumber == 0 ? int.MaxValue : m.MilestoneNumber))
        {
            if (m.MilestoneNumber == 0) m.MilestoneNumber = n;
            n = Math.Max(n, m.MilestoneNumber) + 1;
            if (m.Status is ContractMilestoneStatus.Invoiced or ContractMilestoneStatus.Paid) continue; // لا تُعاد حسابات المفوتر
            if (m.Amount == 0 && m.Percentage > 0) m.Amount = DocumentPricing.Round(c.ContractValue * m.Percentage / 100m);
            m.VatAmount = DocumentPricing.Round(m.Amount * c.VatRate / 100m);
            m.TotalWithVat = m.Amount + m.VatAmount;
            m.RetentionDeductionAmount = DocumentPricing.Round(m.Amount * m.RetentionDeductionPercent / 100m);
            m.NetPayableAmount = m.TotalWithVat - m.RetentionDeductionAmount;
        }
        c.RemainingBalance = c.TotalValueWithVat - c.TotalInvoiced;
    }

    public async Task<CommercialContractDto> AdvanceStageAsync(Guid id, AdvanceStageRequestDto r, CancellationToken ct = default)
    {
        var c = await Includes(Db.Set<CommercialContract>()).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException(Messages.ContractNotFound);
        var stages = StagesFor(c.ContractType);
        var target = Array.IndexOf(stages, r.Stage);
        var current = Array.IndexOf(stages, c.Stage);
        if (target < 0) throw new ValidationFailedException(string.Format(Messages.InvalidStageForContractType, string.Join(" | ", stages)));
        if (target <= current) throw new ConflictException(Messages.CannotGoBackOrRepeatStage);
        if (target > current + 1) throw new ConflictException(Messages.CannotSkipStages);

        c.Stage = r.Stage;
        if (r.Stage == "active_execution" && c.Status is ContractStatus.Draft or ContractStatus.UnderReview) c.Status = ContractStatus.Active;
        else if (r.Stage == "legal_review" && c.Status == ContractStatus.Draft) c.Status = ContractStatus.UnderReview;
        else if (r.Stage == "final_closed")
        {
            if (c.Milestones.Any(m => m.Status is ContractMilestoneStatus.Pending or ContractMilestoneStatus.Due))
                throw new ConflictException(Messages.CannotCloseContractWithUnbilledMilestones);
            c.Status = ContractStatus.Completed;
        }
        if (!string.IsNullOrWhiteSpace(r.Notes)) c.Notes = string.IsNullOrWhiteSpace(c.Notes) ? r.Notes : $"{c.Notes}\n{r.Notes}";
        await Db.SaveChangesAsync(ct);
        return ToDto(c);
    }

    /// <summary>فاتورة ضريبية للمستخلص عبر محرك الفوترة المركزي (بند خدمة بلا مخزون) وربطها بالمستخلص والعقد.</summary>
    public async Task<InvoiceDto> BillMilestoneAsync(Guid contractId, Guid milestoneId, CancellationToken ct = default)
        => await new TransactionRunner(Db).RunAsync(async token =>
        {
            var c = await Db.Set<CommercialContract>().Include(x => x.Milestones).FirstOrDefaultAsync(x => x.Id == contractId, token)
                ?? throw new NotFoundException(Messages.ContractNotFound);
            var m = c.Milestones.FirstOrDefault(x => x.Id == milestoneId) ?? throw new NotFoundException(Messages.MilestoneNotFound);

            if (m.Status is ContractMilestoneStatus.Invoiced or ContractMilestoneStatus.Paid) throw new ConflictException(Messages.MilestoneAlreadyBilled);
            var stages = StagesFor(c.ContractType);
            var activeIndex = Array.IndexOf(stages, c.ContractType == "normal_purchasing" ? "normal_milestone_invoice" : "active_execution");
            if (Array.IndexOf(stages, c.Stage) < activeIndex)
                throw new ConflictException(Messages.MilestonesBilledOnlyWhenActive);
            if (c.PartyId == null) throw new ValidationFailedException(Messages.LinkContractToCustomerBeforeBilling);
            if (m.Amount <= 0) throw new ValidationFailedException(Messages.MilestoneAmountMustBePositive);

            var invoice = await _invoices.CreateAsync(new CreateInvoiceDto
            {
                Kind = InvoiceKind.Sales, InvoiceType = TradeHelper.InvoiceTypeFor(c.PartyVatNumber),
                PartyId = c.PartyId, PartyName = c.PartyName, PartyVatNumber = c.PartyVatNumber, PartyPhone = c.PartyPhone, PartyEmail = c.PartyEmail,
                PaymentMethod = PaymentMethod.Credit, Status = "posted",
                Notes = $"مستخلص رقم {m.MilestoneNumber} - عقد {c.ContractNumber}",
                ReferenceType = "commercial_contract", ReferenceId = c.Id, ReferenceNumber = c.ContractNumber,
                RevenueAccountCode = string.IsNullOrWhiteSpace(c.RevenueAccountCode) ? null : c.RevenueAccountCode,
                Items = new List<InvoiceItemDto>
                {
                    new() { ItemId = Guid.Empty, ItemName = m.Title, Unit = "خدمة", Quantity = 1, UnitPrice = m.Amount, VatRate = c.VatRate },
                },
            }, token);

            m.Status = ContractMilestoneStatus.Invoiced;
            m.InvoicedAt = DateTime.UtcNow; m.InvoiceId = invoice.Id; m.InvoiceNumber = invoice.InvoiceNumber;
            c.TotalInvoiced += invoice.GrandTotal;
            c.RemainingBalance = c.TotalValueWithVat - c.TotalInvoiced;
            var billingIdx = Array.IndexOf(stages, c.ContractType == "normal_purchasing" ? "normal_milestone_invoice" : "milestone_billing");
            if (Array.IndexOf(stages, c.Stage) < billingIdx) c.Stage = stages[billingIdx];
            await Db.SaveChangesAsync(token);
            return invoice;
        }, ct);
}
