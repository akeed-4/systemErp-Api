using System.Globalization;
using DevExtreme.AspNet.Data.ResponseModel;
using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

public class EmployeeLoanService : IEmployeeLoanService
{
    public const string SourceType = "employee_loan";

    private readonly ErpDbContext _db;
    private readonly IAccountingPostingService _posting;
    private readonly INumberSequenceService _numbers;
    private readonly ITransactionRunner _tx;
    private readonly IAuditService _audit;

    public EmployeeLoanService(ErpDbContext db, IAccountingPostingService posting, INumberSequenceService numbers, ITransactionRunner tx, IAuditService audit)
    {
        _db = db; _posting = posting; _numbers = numbers; _tx = tx; _audit = audit;
    }

    public Task<LoadResult> LoadAsync(DataSourceLoadOptions options, CancellationToken ct = default)
        => EntityLoader.LoadAsync(_db.Set<EmployeeLoan>().AsNoTracking(), options, Map, ct,
            EntityLoader.Desc(nameof(EmployeeLoan.Date)), EntityLoader.Desc(nameof(EmployeeLoan.LoanNumber)));

    public async Task<EmployeeLoanDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var loan = await _db.Set<EmployeeLoan>().AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NotFoundException(Messages.LoanNotFound);
        var dto = Map(loan);
        dto.Repayments = await _db.Set<EmployeeLoanRepayment>().AsNoTracking().Where(r => r.EmployeeLoanId == id).OrderBy(r => r.Date)
            .Select(r => new EmployeeLoanRepaymentDto { Id = r.Id, Date = r.Date, Amount = r.Amount, Source = r.Source, Period = r.Period }).ToListAsync(ct);
        return dto;
    }

    public Task<EmployeeLoanDto> CreateAsync(CreateEmployeeLoanDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            if (r.Amount <= 0) throw new ValidationFailedException(Messages.LoanAmountInvalid);
            ValidateSchedule(r.InstallmentAmount, r.FirstDeductionPeriod, r.Amount);
            if (r.Date == default) throw new ValidationFailedException(Messages.LoanDateRequired);
            var employee = await _db.Set<Employee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == r.EmployeeId, token)
                ?? throw new ValidationFailedException(Messages.LeaveEmployeeNotFound);
            if (employee.Status != EmployeeStatuses.Active) throw new ValidationFailedException(Messages.LoanEmployeeNotActive);

            var paymentAccount = string.IsNullOrWhiteSpace(r.PaymentAccountCode) ? DefaultAccounts.Cash : r.PaymentAccountCode.Trim();
            await DefaultAccounts.EnsureAsync(_db, token, DefaultAccounts.EmployeeLoans);
            if (!await _db.Set<Account>().AnyAsync(a => a.Code == paymentAccount, token))
                throw new ValidationFailedException(Messages.LoanPaymentAccountNotFound);

            var loan = new EmployeeLoan
            {
                EmployeeId = employee.Id, EmployeeCode = employee.Code, EmployeeName = employee.NameAr, Date = r.Date.Date,
                Amount = DocumentPricing.Round(r.Amount), InstallmentAmount = DocumentPricing.Round(r.InstallmentAmount),
                FirstDeductionPeriod = r.FirstDeductionPeriod.Trim(), PaymentAccountCode = paymentAccount,
                Reason = string.IsNullOrWhiteSpace(r.Reason) ? null : r.Reason.Trim(),
                LoanNumber = await _numbers.NextAsync("employee_loan", "LN-", token),
            };
            _db.Add(loan);
            await _db.SaveChangesAsync(token);

            var note = $"سلفة {loan.LoanNumber} للموظف {loan.EmployeeName}";
            var posted = await _posting.PostAsync(new GenericPostingRequest
            {
                Date = loan.Date, Description = note, SourceType = SourceType, SourceId = loan.Id, SourceNumber = loan.LoanNumber,
                Lines = new() { new(DefaultAccounts.EmployeeLoans, loan.Amount, 0, note), new(paymentAccount, 0, loan.Amount, note) },
            }, token);
            loan.JournalEntryId = posted.JournalEntryId;
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("create", nameof(EmployeeLoan), loan.Id.ToString(), $"{loan.LoanNumber} {loan.EmployeeName}: {loan.Amount:0.00}", token);
            return Map(loan);
        }, ct);

    public async Task<EmployeeLoanDto> RescheduleAsync(Guid id, RescheduleEmployeeLoanDto r, CancellationToken ct = default)
    {
        var loan = await FindAsync(id, ct);
        if (loan.Status != LoanStatuses.Active) throw new ConflictException(Messages.LoanNotActive);
        ValidateSchedule(r.InstallmentAmount, r.FirstDeductionPeriod, loan.Amount - loan.PaidAmount);
        loan.InstallmentAmount = DocumentPricing.Round(r.InstallmentAmount);
        loan.FirstDeductionPeriod = r.FirstDeductionPeriod.Trim();
        await _db.SaveChangesAsync(ct);
        return Map(loan);
    }

    public Task<EmployeeLoanDto> CancelAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var loan = await FindAsync(id, token);
            if (loan.Status != LoanStatuses.Active) throw new ConflictException(Messages.LoanNotActive);
            if (loan.PaidAmount > 0) throw new ConflictException(Messages.LoanHasRepayments);
            if (loan.JournalEntryId.HasValue) await _posting.ReverseAsync(loan.JournalEntryId.Value, $"إلغاء سلفة {loan.LoanNumber}", token);
            loan.Status = LoanStatuses.Cancelled;
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("cancel", nameof(EmployeeLoan), loan.Id.ToString(), loan.LoanNumber, token);
            return Map(loan);
        }, ct);

    private async Task<EmployeeLoan> FindAsync(Guid id, CancellationToken ct)
        => await _db.Set<EmployeeLoan>().FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw new NotFoundException(Messages.LoanNotFound);

    /// <summary>القسط موجب ولا يتجاوز المتبقي، وشهر البداية بصيغة yyyy-MM.</summary>
    private static void ValidateSchedule(decimal installment, string? period, decimal remaining)
    {
        if (installment <= 0 || installment > remaining) throw new ValidationFailedException(Messages.LoanInstallmentInvalid);
        if (!DateTime.TryParseExact(period?.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new ValidationFailedException(Messages.PayrollPeriodInvalid);
    }

    private static EmployeeLoanDto Map(EmployeeLoan l) => new()
    {
        Id = l.Id, CreatedAt = l.CreatedAt, LoanNumber = l.LoanNumber, EmployeeId = l.EmployeeId, EmployeeCode = l.EmployeeCode, EmployeeName = l.EmployeeName,
        Date = l.Date, Amount = l.Amount, InstallmentAmount = l.InstallmentAmount, FirstDeductionPeriod = l.FirstDeductionPeriod, PaidAmount = l.PaidAmount,
        Remaining = l.Status == LoanStatuses.Cancelled ? 0 : l.Amount - l.PaidAmount, Status = l.Status, Reason = l.Reason,
        PaymentAccountCode = l.PaymentAccountCode, JournalEntryId = l.JournalEntryId,
    };
}
