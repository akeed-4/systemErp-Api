using DevExtreme.AspNet.Data.ResponseModel;
using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// تصفية نهاية الخدمة. المكافأة (نظام العمل، المادة 84): أجر نصف شهر عن كل سنة من الخمس الأولى، وأجر شهر عن كل
/// سنة بعدها، وكسور السنة بنسبتها، على الأجر الأخير. في الاستقالة (المادة 85): لا شيء قبل سنتين، ثلثها حتى خمس
/// سنوات، ثلثاها حتى عشر، ثم كاملة. يُضاف بدل رصيد الإجازة السنوية (أجر اليوم = الأجر ÷ 30) ويُخصم رصيد السلف.
/// القيد: مدين مصروف المكافأة ومصروف الرواتب (بدل الإجازات والمستحقات)، دائن سلف الموظفين والرواتب المستحقة (الصافي).
/// </summary>
public class EndOfServiceService : IEndOfServiceService
{
    public const string SourceType = "end_of_service";
    private const decimal DaysPerYear = 365m;
    private const int HalfRateYears = 5;
    private const int ResignationNoAwardYears = 2;
    private const int ResignationFullAwardYears = 10;

    private readonly ErpDbContext _db;
    private readonly IAccountingPostingService _posting;
    private readonly INumberSequenceService _numbers;
    private readonly ITransactionRunner _tx;
    private readonly IAuditService _audit;

    public EndOfServiceService(ErpDbContext db, IAccountingPostingService posting, INumberSequenceService numbers, ITransactionRunner tx, IAuditService audit)
    {
        _db = db; _posting = posting; _numbers = numbers; _tx = tx; _audit = audit;
    }

    public async Task<EndOfServiceDto> PreviewAsync(EndOfServiceRequestDto request, CancellationToken ct = default)
        => Map((await BuildAsync(request, ct)).Settlement, "preview");

    public Task<EndOfServiceDto> PostAsync(EndOfServiceRequestDto request, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var (settlement, employee, loans) = await BuildAsync(request, token, tracked: true);
            settlement.SettlementNumber = await _numbers.NextAsync("end_of_service", "EOS-", token);
            _db.Add(settlement);
            await _db.SaveChangesAsync(token);

            await DefaultAccounts.EnsureAsync(_db, token, DefaultAccounts.EndOfServiceExpense, DefaultAccounts.SalariesExpense,
                DefaultAccounts.SalariesPayable, DefaultAccounts.EmployeeLoans);
            var note = $"تصفية نهاية خدمة {settlement.SettlementNumber} للموظف {settlement.EmployeeName}";
            var lines = new List<PostingLine>();
            if (settlement.Award > 0) lines.Add(new(DefaultAccounts.EndOfServiceExpense, settlement.Award, 0, note, employee.CostCenterId));
            // بدل الإجازات والمستحقات الأخرى ناقص الخصومات: مصروف رواتب (أو استرداد منه إن كان سالباً)
            var salaries = settlement.LeavePayout + settlement.OtherAdditions - settlement.OtherDeductions;
            if (salaries > 0) lines.Add(new(DefaultAccounts.SalariesExpense, salaries, 0, note, employee.CostCenterId));
            else if (salaries < 0) lines.Add(new(DefaultAccounts.SalariesExpense, 0, -salaries, note, employee.CostCenterId));
            if (settlement.LoanDeduction > 0) lines.Add(new(DefaultAccounts.EmployeeLoans, 0, settlement.LoanDeduction, note));
            if (settlement.Net > 0) lines.Add(new(DefaultAccounts.SalariesPayable, 0, settlement.Net, note));
            if (lines.Count > 0)
            {
                var posted = await _posting.PostAsync(new GenericPostingRequest
                {
                    Date = settlement.LastWorkingDay, Description = note, SourceType = SourceType, SourceId = settlement.Id,
                    SourceNumber = settlement.SettlementNumber, Lines = lines,
                }, token);
                settlement.JournalEntryId = posted.JournalEntryId;
            }

            // السلف تُسوّى بكامل أرصدتها
            foreach (var loan in loans)
            {
                var remaining = loan.Amount - loan.PaidAmount;
                _db.Add(new EmployeeLoanRepayment
                {
                    EmployeeLoanId = loan.Id, Date = settlement.LastWorkingDay, Amount = remaining,
                    Source = LoanRepaymentSources.Settlement, SettlementId = settlement.Id,
                });
                loan.PaidAmount = loan.Amount;
                loan.Status = LoanStatuses.Settled;
            }
            // طلبات الإجازة المعلّقة تسقط بانتهاء الخدمة
            foreach (var pending in await _db.Set<LeaveRequest>().Where(l => l.EmployeeId == employee.Id && l.Status == LeaveStatuses.Pending).ToListAsync(token))
            {
                pending.Status = LeaveStatuses.Cancelled;
                pending.DecidedAt = DateTime.UtcNow;
                pending.DecisionNote = settlement.SettlementNumber;
            }
            employee.Status = EmployeeStatuses.Terminated;
            employee.TerminationDate = settlement.LastWorkingDay;
            employee.TerminationReason = settlement.Reason;
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("post", nameof(EndOfServiceSettlement), settlement.Id.ToString(),
                $"{settlement.SettlementNumber} {settlement.EmployeeName}: مكافأة {settlement.Award:0.00}، صافي {settlement.Net:0.00}", token);
            return Map(settlement, settlement.Status);
        }, ct);

    public Task<LoadResult> LoadAsync(DataSourceLoadOptions options, CancellationToken ct = default)
        => EntityLoader.LoadAsync(_db.Set<EndOfServiceSettlement>().AsNoTracking(), options, s => Map(s, s.Status), ct,
            EntityLoader.Desc(nameof(EndOfServiceSettlement.LastWorkingDay)), EntityLoader.Desc(nameof(EndOfServiceSettlement.SettlementNumber)));

    public async Task<EndOfServiceDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var settlement = await _db.Set<EndOfServiceSettlement>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException(Messages.EndOfServiceNotFound);
        return Map(settlement, settlement.Status);
    }

    public Task<EndOfServiceDto> ReverseAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var settlement = await _db.Set<EndOfServiceSettlement>().FirstOrDefaultAsync(s => s.Id == id, token)
                ?? throw new NotFoundException(Messages.EndOfServiceNotFound);
            if (settlement.Status != "posted") throw new ConflictException(Messages.EndOfServiceAlreadyReversed);
            if (settlement.JournalEntryId.HasValue)
                await _posting.ReverseAsync(settlement.JournalEntryId.Value, $"عكس تصفية نهاية خدمة {settlement.SettlementNumber}", token);

            // السلف المسوّاة في التصفية تعود إلى أرصدتها
            var repayments = await _db.Set<EmployeeLoanRepayment>().Where(r => r.SettlementId == settlement.Id).ToListAsync(token);
            var loanIds = repayments.Select(r => r.EmployeeLoanId).ToList();
            var loans = await _db.Set<EmployeeLoan>().Where(l => loanIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, token);
            foreach (var repayment in repayments)
            {
                var loan = loans[repayment.EmployeeLoanId];
                loan.PaidAmount -= repayment.Amount;
                loan.Status = LoanStatuses.Active;
                _db.Remove(repayment);
            }
            var employee = await _db.Set<Employee>().FirstOrDefaultAsync(e => e.Id == settlement.EmployeeId, token);
            if (employee != null)
            {
                employee.Status = EmployeeStatuses.Active;
                employee.TerminationDate = null;
                employee.TerminationReason = null;
            }
            settlement.Status = "reversed";
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("reverse", nameof(EndOfServiceSettlement), settlement.Id.ToString(), settlement.SettlementNumber, token);
            return Map(settlement, settlement.Status);
        }, ct);

    /// <summary>المكافأة الكاملة عن مدة خدمة بالسنين على أجر شهري.</summary>
    public static decimal FullAward(decimal years, decimal wage)
        => Math.Min(years, HalfRateYears) * wage / 2 + Math.Max(0, years - HalfRateYears) * wage;

    /// <summary>نسبة الاستحقاق من المكافأة حسب سبب انتهاء الخدمة ومدتها.</summary>
    public static decimal AwardFactor(string reason, decimal years) => reason switch
    {
        EndOfServiceReasons.Dismissal => 0,
        EndOfServiceReasons.Resignation => years < ResignationNoAwardYears ? 0 : years < HalfRateYears ? 1m / 3 : years < ResignationFullAwardYears ? 2m / 3 : 1,
        _ => 1,
    };

    /// <summary>يحسب التصفية دون حفظ. tracked: الموظف والسلف متتبَّعة ليعدّلها الترحيل.</summary>
    private async Task<(EndOfServiceSettlement Settlement, Employee Employee, List<EmployeeLoan> Loans)> BuildAsync(
        EndOfServiceRequestDto r, CancellationToken ct, bool tracked = false)
    {
        if (!EndOfServiceReasons.All.Contains(r.Reason)) throw new ValidationFailedException(Messages.EndOfServiceReasonInvalid);
        if (r.OtherAdditions < 0 || r.OtherDeductions < 0) throw new ValidationFailedException(Messages.EndOfServiceAmountsInvalid);
        var employees = _db.Set<Employee>().AsQueryable();
        var employee = await (tracked ? employees : employees.AsNoTracking()).FirstOrDefaultAsync(e => e.Id == r.EmployeeId, ct)
            ?? throw new ValidationFailedException(Messages.LeaveEmployeeNotFound);
        if (employee.Status == EmployeeStatuses.Terminated
            || await _db.Set<EndOfServiceSettlement>().AnyAsync(s => s.EmployeeId == employee.Id && s.Status == "posted", ct))
            throw new ConflictException(Messages.EndOfServiceAlreadySettled);
        var lastDay = r.LastWorkingDay.Date;
        if (lastDay == default || lastDay < employee.HireDate.Date) throw new ValidationFailedException(Messages.EmployeeTerminationBeforeHire);

        var years = LeaveBalances.Span(employee.HireDate, lastDay) / DaysPerYear;
        var wage = employee.BasicSalary + employee.HousingAllowance + employee.TransportAllowance + employee.OtherAllowances;
        var full = FullAward(years, wage);
        var factor = AwardFactor(r.Reason, years);

        var annual = await _db.Set<LeaveRequest>().AsNoTracking()
            .Where(l => l.EmployeeId == employee.Id && l.IsAnnual && l.Status == LeaveStatuses.Approved).ToListAsync(ct);
        var leaveDays = LeaveBalances.Compute(employee, lastDay, annual).Balance;

        var loansQuery = _db.Set<EmployeeLoan>().Where(l => l.EmployeeId == employee.Id && l.Status == LoanStatuses.Active);
        var loans = await (tracked ? loansQuery : loansQuery.AsNoTracking()).ToListAsync(ct);

        var settlement = new EndOfServiceSettlement
        {
            EmployeeId = employee.Id, EmployeeCode = employee.Code, EmployeeName = employee.NameAr, HireDate = employee.HireDate.Date,
            LastWorkingDay = lastDay, Reason = r.Reason, ServiceYears = Math.Round(years, 4), Wage = wage,
            FullAward = DocumentPricing.Round(full), AwardFactor = Math.Round(factor, 4), Award = DocumentPricing.Round(full * factor),
            LeaveBalanceDays = Math.Round(leaveDays, 2),
            LeavePayout = DocumentPricing.Round(wage / LeaveBalances.PayrollMonthDays * leaveDays),
            OtherAdditions = DocumentPricing.Round(r.OtherAdditions), OtherDeductions = DocumentPricing.Round(r.OtherDeductions),
            LoanDeduction = loans.Sum(l => l.Amount - l.PaidAmount),
            Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim(),
        };
        settlement.Net = settlement.Award + settlement.LeavePayout + settlement.OtherAdditions - settlement.OtherDeductions - settlement.LoanDeduction;
        if (settlement.Net < 0) throw new ValidationFailedException(string.Format(Messages.EndOfServiceNetNegative, settlement.Net.ToString("0.00")));
        return (settlement, employee, loans);
    }

    private static EndOfServiceDto Map(EndOfServiceSettlement s, string status) => new()
    {
        Id = status == "preview" ? null : s.Id, SettlementNumber = string.IsNullOrEmpty(s.SettlementNumber) ? null : s.SettlementNumber,
        EmployeeId = s.EmployeeId, EmployeeCode = s.EmployeeCode, EmployeeName = s.EmployeeName, HireDate = s.HireDate, LastWorkingDay = s.LastWorkingDay,
        Reason = s.Reason, ServiceYears = s.ServiceYears, Wage = s.Wage, FullAward = s.FullAward, AwardFactor = s.AwardFactor, Award = s.Award,
        LeaveBalanceDays = s.LeaveBalanceDays, LeavePayout = s.LeavePayout, OtherAdditions = s.OtherAdditions, OtherDeductions = s.OtherDeductions,
        LoanDeduction = s.LoanDeduction, Net = s.Net, Status = status, JournalEntryId = s.JournalEntryId, Notes = s.Notes,
    };
}
