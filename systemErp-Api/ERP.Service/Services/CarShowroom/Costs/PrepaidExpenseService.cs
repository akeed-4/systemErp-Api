using System.Globalization;
using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.CarShowroom;
using ERP.Service.Data;
using ERP.Service.Services.Accounting;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.CarShowroom;

/// <summary>
/// المصروفات المدفوعة مقدماً (اشتراك تم السنوي): تُقيَّد أصلاً عند الدفع وتُطفأ شهرياً بزر «ترحيل إطفاء الشهر».
/// لا مُجدوِل في النظام: الإطفاء بطلب المستخدم، وفهرس فريد على (المصروف، الفترة) يمنع إطفاء الشهر مرتين.
/// </summary>
public class PrepaidExpenseService : IPrepaidExpenseService
{
    public const string PaymentSource = "car_prepaid_expense";
    public const string AmortizationSource = "car_prepaid_amortization";

    private readonly ErpDbContext _db;
    private readonly ITransactionRunner _tx;
    private readonly INumberSequenceService _numbers;
    private readonly IAccountingPostingService _posting;
    private readonly IAuditService _audit;

    public PrepaidExpenseService(ErpDbContext db, ITransactionRunner tx, INumberSequenceService numbers, IAccountingPostingService posting, IAuditService audit)
    {
        _db = db; _tx = tx; _numbers = numbers; _posting = posting; _audit = audit;
    }

    public async Task<PagedResult<PrepaidExpenseDto>> ListAsync(PaginationParams p, CancellationToken ct = default)
    {
        var q = _db.Set<PrepaidExpense>().AsNoTracking().AsQueryable();
        if (Enum.TryParse<PrepaidExpenseStatus>(p.Status, true, out var status)) q = q.Where(e => e.Status == status);
        var total = await q.CountAsync(ct);
        var items = await q.Include(e => e.Amortizations).OrderByDescending(e => e.StartDate).ThenByDescending(e => e.Number)
            .Skip((p.NormalizedPage - 1) * p.NormalizedSize).Take(p.NormalizedSize).ToListAsync(ct);
        return new PagedResult<PrepaidExpenseDto> { Items = items.Select(ToDto).ToList(), TotalCount = total, PageNumber = p.NormalizedPage, PageSize = p.NormalizedSize };
    }

    private static PrepaidExpenseDto ToDto(PrepaidExpense e)
    {
        var dto = Mapper.Map<PrepaidExpenseDto>(e);
        dto.Amortizations = dto.Amortizations.OrderBy(a => a.Period).ToList();
        return dto;
    }

    public Task<PrepaidExpenseDto> CreateAsync(CreatePrepaidExpenseDto d, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            if (d.TotalAmount <= 0 || d.Months is < 1 or > 120 || d.VatAmount < 0 || d.StartDate == default)
                throw new ValidationFailedException(Messages.PrepaidExpenseValuesInvalid);
            var type = await _db.Set<ExpenseType>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == d.ExpenseTypeId && t.IsActive, token)
                ?? throw new ValidationFailedException(Messages.ExpenseTypeNotFoundOrInactive);
            var settings = await SalesCharges.SettingsAsync(_db, token);
            await ChargePosting.EnsureAccountsAsync(_db, settings, token);

            if (!d.SupplierId.HasValue) d.PaymentAccountCode = await PaymentMethodRules.AccountForAsync(_db, d.PaymentMethodCode, d.PaymentAccountCode, token);
            string credit;
            if (d.SupplierId.HasValue)
                credit = await _db.Set<Supplier>().AsNoTracking().Where(s => s.Id == d.SupplierId).Select(s => s.AccountCode).FirstOrDefaultAsync(token)
                    ?? throw new ValidationFailedException(Messages.SupplierNotFound);
            else if (!string.IsNullOrWhiteSpace(d.PaymentAccountCode))
            {
                credit = d.PaymentAccountCode.Trim();
                if (!await _db.Set<Account>().AnyAsync(a => a.Code == credit, token)) throw new ValidationFailedException(Messages.ChargePaymentAccountInvalid);
            }
            else credit = settings.AccruedChargesAccount;

            // الضريبة المستردة ضريبة مدخلات عند الدفع؛ غير المستردة جزء من المبلغ الذي يُطفأ
            var recoverable = type.IsVatRecoverable ? DocumentPricing.Round(d.VatAmount) : 0;
            var amount = DocumentPricing.Round(d.TotalAmount) + (type.IsVatRecoverable ? 0 : DocumentPricing.Round(d.VatAmount));
            var expense = new PrepaidExpense
            {
                Number = await _numbers.NextAsync("prepaid_expense", "PPE-", token),
                ExpenseTypeId = type.Id, ExpenseTypeNameAr = type.NameAr, Description = string.IsNullOrWhiteSpace(d.Description) ? null : d.Description.Trim(),
                StartDate = d.StartDate, Months = d.Months, TotalAmount = amount, VatAmount = recoverable,
                SupplierId = d.SupplierId, PaymentAccountCode = d.SupplierId.HasValue ? null : credit,
            };
            _db.Add(expense);
            var lines = new List<PostingLine> { new(settings.PrepaidExpensesAccount, amount, 0) };
            if (recoverable > 0) lines.Add(new(DefaultAccounts.InputVat, recoverable, 0));
            lines.Add(new(credit, 0, amount + recoverable));
            var posted = await _posting.PostAsync(new GenericPostingRequest
            {
                Date = d.StartDate, Description = $"مصروف مدفوع مقدماً {expense.Number} - {type.NameAr}", SourceType = PaymentSource,
                SourceId = expense.Id, SourceNumber = expense.Number, Lines = lines,
            }, token);
            expense.JournalEntryId = posted.JournalEntryId;
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("PREPAID_EXPENSE_CREATED", nameof(PrepaidExpense), expense.Id.ToString(), $"{expense.Number}: {amount:0.00} على {d.Months} شهراً", token);
            return ToDto(expense);
        }, ct);

    public Task<PrepaidExpenseDto> CancelAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var expense = await _db.Set<PrepaidExpense>().Include(e => e.Amortizations).FirstOrDefaultAsync(e => e.Id == id, token)
                ?? throw new NotFoundException(Messages.PrepaidExpenseNotFound);
            if (expense.Amortizations.Count > 0 || expense.Status != PrepaidExpenseStatus.Active) throw new ConflictException(Messages.PrepaidExpenseHasAmortization);
            if (expense.JournalEntryId.HasValue) await _posting.ReverseAsync(expense.JournalEntryId.Value, $"إلغاء {expense.Number}", token);
            expense.Status = PrepaidExpenseStatus.Cancelled;
            await _db.SaveChangesAsync(token);
            return ToDto(expense);
        }, ct);

    public Task<AmortizePrepaidExpensesResultDto> AmortizeAsync(AmortizePrepaidExpensesDto d, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var period = string.IsNullOrWhiteSpace(d.Period) ? SaudiTime.Today.ToString("yyyy-MM", CultureInfo.InvariantCulture) : d.Period.Trim();
            if (!DateTime.TryParseExact(period, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
                throw new ValidationFailedException(Messages.PrepaidPeriodInvalid);
            var settings = await SalesCharges.SettingsAsync(_db, token);
            await ChargePosting.EnsureAccountsAsync(_db, settings, token);

            // ما يشمله الشهر ولم يُطفأ له بعد
            var due = await _db.Set<PrepaidExpense>().Include(e => e.Amortizations)
                .Where(e => e.Status == PrepaidExpenseStatus.Active && e.StartDate < month.AddMonths(1) && !e.Amortizations.Any(a => a.Period == period)).ToListAsync(token);
            var typeIds = due.Select(e => e.ExpenseTypeId).Distinct().ToList();
            var accounts = await _db.Set<ExpenseType>().AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.AccountCode, token);
            var result = new AmortizePrepaidExpensesResultDto { Period = period };

            foreach (var expense in due)
            {
                var index = (month.Year - expense.StartDate.Year) * 12 + month.Month - expense.StartDate.Month;
                if (index >= expense.Months) continue;
                // قسط شهري ثابت، وفرق التقريب على الشهر الأخير
                var monthly = DocumentPricing.Round(expense.TotalAmount / expense.Months);
                var amount = index == expense.Months - 1 ? expense.TotalAmount - monthly * (expense.Months - 1) : monthly;
                if (amount <= 0) continue;

                var amortization = new PrepaidExpenseAmortization { Period = period, Amount = amount };
                expense.Amortizations.Add(amortization);
                _db.Add(amortization);
                var account = accounts.GetValueOrDefault(expense.ExpenseTypeId);
                var posted = await _posting.PostAsync(new GenericPostingRequest
                {
                    Date = month.AddMonths(1).AddDays(-1), Description = $"إطفاء {expense.ExpenseTypeNameAr} {expense.Number} عن {period}",
                    SourceType = AmortizationSource, SourceId = expense.Id, SourceNumber = expense.Number,
                    Lines = new List<PostingLine>
                    {
                        new(string.IsNullOrWhiteSpace(account) ? settings.VehicleExpenseAccount : account, amount, 0),
                        new(settings.PrepaidExpensesAccount, 0, amount),
                    },
                }, token);
                amortization.JournalEntryId = posted.JournalEntryId;
                expense.AmortizedAmount += amount;
                if (expense.Amortizations.Count >= expense.Months) expense.Status = PrepaidExpenseStatus.Completed;
                result.Posted++; result.Amount += amount;
            }
            await _db.SaveChangesAsync(token);
            if (result.Posted > 0)
                await _audit.LogAsync("PREPAID_AMORTIZED", nameof(PrepaidExpense), string.Empty, $"إطفاء {period}: {result.Posted} مصروف بمبلغ {result.Amount:0.00}", token);
            return result;
        }, ct);
}
