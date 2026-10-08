using System.Globalization;
using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// مسير الرواتب الشهري. لكل موظف نشط التحق قبل نهاية الشهر:
/// الإجمالي = الأساسي + السكن + النقل + البدلات الأخرى + إضافات الشهر؛
/// التأمينات على (الأساسي + السكن) بنسبتَي الموظف والمنشأة؛ الصافي = الإجمالي − خصومات الشهر − حصة الموظف.
/// خصومات الشهر تشمل خصم الإجازات المعتمدة منقوصة الأجر: أجر اليوم (الراتب الثابت ÷ 30) × أيامها في الشهر × الجزء غير المدفوع.
/// أقساط السلف النشطة التي حلّ شهر خصمها تُطرح من الصافي وتُقفل من رصيد السلفة (ليست مصروفاً).
/// القيد: مدين الرواتب (الإجمالي − الخصومات) وحصة المنشأة في التأمينات، دائن الرواتب المستحقة (الصافي) والتأمينات المستحقة وسلف الموظفين (الأقساط).
/// صرف الرواتب وسداد التأمينات بسندات صرف على حسابيهما المستحقين.
/// </summary>
public class PayrollService : IPayrollService
{
    public const string SourceType = "payroll";

    private readonly ErpDbContext _db;
    private readonly IAccountingPostingService _posting;
    private readonly INumberSequenceService _numbers;
    private readonly ITransactionRunner _tx;
    private readonly IAuditService _audit;

    public PayrollService(ErpDbContext db, IAccountingPostingService posting, INumberSequenceService numbers, ITransactionRunner tx, IAuditService audit)
    {
        _db = db; _posting = posting; _numbers = numbers; _tx = tx; _audit = audit;
    }

    public async Task<PayrollRunDto> PreviewAsync(PayrollRunRequestDto r, CancellationToken ct = default)
        => Map((await BuildAsync(r, ct)).Run, "preview");

    public Task<PayrollRunDto> PostAsync(PayrollRunRequestDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var (run, installments) = await BuildAsync(r, token);
            if (run.Lines.Count == 0) throw new ValidationFailedException(Messages.PayrollNoEmployees);
            if (await _db.Set<PayrollRun>().AnyAsync(p => p.Period == run.Period && p.Status == "posted", token))
                throw new ConflictException(string.Format(Messages.PayrollPeriodAlreadyPosted, run.Period));

            run.RunNumber = await _numbers.NextAsync("payroll_run", "PAY-", token);
            _db.Add(run);
            await _db.SaveChangesAsync(token);

            await DefaultAccounts.EnsureAsync(_db, token, DefaultAccounts.SalariesExpense, DefaultAccounts.SocialInsuranceExpense,
                DefaultAccounts.SalariesPayable, DefaultAccounts.SocialInsurancePayable, DefaultAccounts.EmployeeLoans);
            var note = $"مسير رواتب {run.Period}";
            var lines = new List<PostingLine>();
            // مصروف الرواتب وحصة المنشأة على مركز تكلفة كل موظف
            foreach (var group in run.Lines.GroupBy(l => l.CostCenterId))
            {
                var salaries = group.Sum(l => l.Gross - l.Deductions);
                var employerGosi = group.Sum(l => l.EmployerGosi);
                if (salaries > 0) lines.Add(new(DefaultAccounts.SalariesExpense, salaries, 0, note, group.Key));
                if (employerGosi > 0) lines.Add(new(DefaultAccounts.SocialInsuranceExpense, employerGosi, 0, note, group.Key));
            }
            lines.Add(new(DefaultAccounts.SalariesPayable, 0, run.TotalNet, note));
            if (run.TotalEmployeeGosi + run.TotalEmployerGosi > 0)
                lines.Add(new(DefaultAccounts.SocialInsurancePayable, 0, run.TotalEmployeeGosi + run.TotalEmployerGosi, note));
            if (run.TotalLoanDeductions > 0) lines.Add(new(DefaultAccounts.EmployeeLoans, 0, run.TotalLoanDeductions, note));

            // الأقساط المخصومة تُسجَّل سداداً على سلفها
            if (installments.Count > 0)
            {
                var loanIds = installments.Select(i => i.LoanId).ToList();
                var loans = await _db.Set<EmployeeLoan>().Where(l => loanIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, token);
                foreach (var (loanId, amount) in installments)
                {
                    var loan = loans[loanId];
                    _db.Add(new EmployeeLoanRepayment
                    {
                        EmployeeLoanId = loanId, Date = run.Date, Amount = amount, Source = LoanRepaymentSources.Payroll, Period = run.Period, PayrollRunId = run.Id,
                    });
                    loan.PaidAmount += amount;
                    if (loan.PaidAmount >= loan.Amount) loan.Status = LoanStatuses.Settled;
                }
            }

            var posted = await _posting.PostAsync(new GenericPostingRequest
            {
                Date = run.Date, Description = note, SourceType = SourceType, SourceId = run.Id, SourceNumber = run.RunNumber, Lines = lines,
            }, token);
            run.JournalEntryId = posted.JournalEntryId;
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("post", nameof(PayrollRun), run.Id.ToString(), $"{run.RunNumber} {run.Period}: صافي {run.TotalNet:0.00} لـ {run.Lines.Count} موظف", token);
            return Map(run, run.Status);
        }, ct);

    public async Task<List<PayrollRunDto>> ListAsync(CancellationToken ct = default)
        => (await _db.Set<PayrollRun>().AsNoTracking().OrderByDescending(p => p.Period).ThenByDescending(p => p.CreatedAt).ToListAsync(ct))
            .Select(p => Map(p, p.Status)).ToList();

    public async Task<PayrollRunDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var run = await _db.Set<PayrollRun>().AsNoTracking().Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException(Messages.PayrollRunNotFound);
        return Map(run, run.Status);
    }

    public Task<PayrollRunDto> ReverseAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var run = await _db.Set<PayrollRun>().Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, token)
                ?? throw new NotFoundException(Messages.PayrollRunNotFound);
            if (run.Status != "posted") throw new ConflictException(Messages.PayrollRunAlreadyReversed);
            if (run.JournalEntryId.HasValue) await _posting.ReverseAsync(run.JournalEntryId.Value, $"عكس مسير رواتب {run.Period}", token);
            // أقساط السلف المخصومة في المسير تعود إلى أرصدة سلفها
            var repayments = await _db.Set<EmployeeLoanRepayment>().Where(p => p.PayrollRunId == run.Id).ToListAsync(token);
            if (repayments.Count > 0)
            {
                var loanIds = repayments.Select(p => p.EmployeeLoanId).ToList();
                var loans = await _db.Set<EmployeeLoan>().Where(l => loanIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, token);
                foreach (var repayment in repayments)
                {
                    var loan = loans[repayment.EmployeeLoanId];
                    loan.PaidAmount -= repayment.Amount;
                    if (loan.Status == LoanStatuses.Settled) loan.Status = LoanStatuses.Active;
                    _db.Remove(repayment);
                }
            }
            run.Status = "reversed";
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("reverse", nameof(PayrollRun), run.Id.ToString(), $"{run.RunNumber} {run.Period}", token);
            return Map(run, run.Status);
        }, ct);

    /// <summary>يحسب المسير من بيانات الموظفين وإضافات/خصومات الشهر دون حفظ.</summary>
    private async Task<(PayrollRun Run, List<(Guid LoanId, decimal Amount)> Installments)> BuildAsync(PayrollRunRequestDto r, CancellationToken ct)
    {
        if (!DateTime.TryParseExact(r.Period?.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
            throw new ValidationFailedException(Messages.PayrollPeriodInvalid);
        var monthEnd = month.AddMonths(1).AddDays(-1);
        if (r.Adjustments.Any(a => a.Additions < 0 || a.Deductions < 0)) throw new ValidationFailedException(Messages.PayrollAdjustmentInvalid);
        var adjustments = r.Adjustments.GroupBy(a => a.EmployeeId)
            .ToDictionary(g => g.Key, g => (Additions: g.Sum(a => a.Additions), Deductions: g.Sum(a => a.Deductions)));

        var employees = await _db.Set<Employee>().AsNoTracking().Where(e => e.Status == EmployeeStatuses.Active && e.HireDate <= monthEnd).OrderBy(e => e.Code).ToListAsync(ct);
        // إجازات معتمدة منقوصة الأجر تقع أيام منها في الشهر
        var unpaidLeaves = (await _db.Set<LeaveRequest>().AsNoTracking()
                .Where(l => l.Status == LeaveStatuses.Approved && l.PayPercent < 100 && l.StartDate <= monthEnd && l.EndDate >= month).ToListAsync(ct))
            .ToLookup(l => l.EmployeeId);

        var run = new PayrollRun { Period = month.ToString("yyyy-MM", CultureInfo.InvariantCulture), Date = monthEnd, Notes = r.Notes };
        // سلف نشطة حلّ شهر خصمها (صيغة yyyy-MM تُقارن نصياً)
        var activeLoans = (await _db.Set<EmployeeLoan>().AsNoTracking()
                .Where(l => l.Status == LoanStatuses.Active && l.FirstDeductionPeriod.CompareTo(run.Period) <= 0).OrderBy(l => l.Date).ToListAsync(ct))
            .ToLookup(l => l.EmployeeId);
        var installments = new List<(Guid LoanId, decimal Amount)>();
        foreach (var e in employees)
        {
            var (additions, deductions) = adjustments.GetValueOrDefault(e.Id);
            var package = e.BasicSalary + e.HousingAllowance + e.TransportAllowance + e.OtherAllowances;
            // يوم بنصف أجر يُعدّ نصف يوم بلا أجر؛ الشهر 30 يوماً فلا يتجاوز الخصم الراتب الثابت
            var unpaidDays = Math.Min(LeaveBalances.PayrollMonthDays, unpaidLeaves[e.Id].Sum(l =>
                LeaveBalances.Span(l.StartDate > month ? l.StartDate : month, l.EndDate < monthEnd ? l.EndDate : monthEnd) * (100 - l.PayPercent) / 100));
            var leaveDeduction = DocumentPricing.Round(package / LeaveBalances.PayrollMonthDays * unpaidDays);
            deductions += leaveDeduction;
            var gross = package + additions;
            var insurable = e.BasicSalary + e.HousingAllowance;
            var employeeGosi = DocumentPricing.Round(insurable * e.EmployeeGosiRate / 100);
            var loanDeduction = 0m;
            foreach (var loan in activeLoans[e.Id])
            {
                var installment = Math.Min(loan.InstallmentAmount, loan.Amount - loan.PaidAmount);
                if (installment <= 0) continue;
                installments.Add((loan.Id, installment));
                loanDeduction += installment;
            }
            var net = gross - deductions - employeeGosi - loanDeduction;
            if (net < 0) throw new ValidationFailedException(string.Format(Messages.PayrollNetNegative, e.NameAr));
            run.Lines.Add(new PayrollLine
            {
                EmployeeId = e.Id, EmployeeCode = e.Code, EmployeeName = e.NameAr, CostCenterId = e.CostCenterId,
                BasicSalary = e.BasicSalary, HousingAllowance = e.HousingAllowance, TransportAllowance = e.TransportAllowance, OtherAllowances = e.OtherAllowances,
                Additions = additions, Deductions = deductions, UnpaidLeaveDays = unpaidDays, LeaveDeduction = leaveDeduction, LoanDeduction = loanDeduction, Gross = gross, EmployeeGosi = employeeGosi,
                EmployerGosi = DocumentPricing.Round(insurable * e.EmployerGosiRate / 100), Net = net,
            });
        }
        run.TotalGross = run.Lines.Sum(l => l.Gross); run.TotalDeductions = run.Lines.Sum(l => l.Deductions);
        run.TotalEmployeeGosi = run.Lines.Sum(l => l.EmployeeGosi); run.TotalEmployerGosi = run.Lines.Sum(l => l.EmployerGosi);
        run.TotalLoanDeductions = run.Lines.Sum(l => l.LoanDeduction);
        run.TotalNet = run.Lines.Sum(l => l.Net);
        return (run, installments);
    }

    private static PayrollRunDto Map(PayrollRun run, string status) => new()
    {
        Id = run.Id == Guid.Empty || status == "preview" ? null : run.Id, RunNumber = string.IsNullOrEmpty(run.RunNumber) ? null : run.RunNumber,
        Period = run.Period, Date = run.Date, Status = status, Notes = run.Notes, JournalEntryId = run.JournalEntryId,
        TotalGross = run.TotalGross, TotalDeductions = run.TotalDeductions, TotalEmployeeGosi = run.TotalEmployeeGosi,
        TotalEmployerGosi = run.TotalEmployerGosi, TotalLoanDeductions = run.TotalLoanDeductions, TotalNet = run.TotalNet,
        Lines = run.Lines.OrderBy(l => l.EmployeeCode).Select(l => new PayrollLineDto
        {
            EmployeeId = l.EmployeeId, EmployeeCode = l.EmployeeCode, EmployeeName = l.EmployeeName, BasicSalary = l.BasicSalary,
            HousingAllowance = l.HousingAllowance, TransportAllowance = l.TransportAllowance, OtherAllowances = l.OtherAllowances,
            Additions = l.Additions, Deductions = l.Deductions, UnpaidLeaveDays = l.UnpaidLeaveDays, LeaveDeduction = l.LeaveDeduction, LoanDeduction = l.LoanDeduction, Gross = l.Gross, EmployeeGosi = l.EmployeeGosi, EmployerGosi = l.EmployerGosi, Net = l.Net,
        }).ToList(),
    };
}
