using System.Globalization;
using System.Text;
using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// مسير الرواتب الشهري. لكل موظف نشط التحق قبل نهاية الشهر:
/// الإجمالي = الأساسي + السكن + النقل + البدلات الأخرى + إضافات الشهر + أجر العمل الإضافي؛
/// التأمينات على (الأساسي + السكن) بنسبتَي الموظف والمنشأة؛ الصافي = الإجمالي − خصومات الشهر − حصة الموظف − أقساط السلف.
/// خصومات الشهر = الخصم اليدوي + خصم الإجازات المعتمدة منقوصة الأجر (أجر اليوم = الراتب الثابت ÷ 30) + خصم الغياب والتأخير.
/// أقساط السلف النشطة التي حلّ شهر خصمها تُطرح من الصافي وتُقفل من رصيد السلفة (ليست مصروفاً).
/// القيد: مدين الرواتب (الإجمالي − الخصومات) وحصة المنشأة في التأمينات، دائن الرواتب المستحقة (الصافي) والتأمينات المستحقة وسلف الموظفين (الأقساط).
/// صرف الرواتب وسداد التأمينات بسندات صرف على حسابيهما المستحقين.
/// المسير يُحفظ مسودة للمراجعة ثم يُعتمد فيُرحَّل، أو يُرحَّل مباشرة.
/// </summary>
public class PayrollService : IPayrollService
{
    public const string SourceType = "payroll";
    private const string Draft = "draft";
    private const string Posted = "posted";
    private const string Reversed = "reversed";

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

    public Task<PayrollRunDto> SaveDraftAsync(PayrollRunRequestDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var (run, _) = await BuildAsync(r, token);
            if (run.Lines.Count == 0) throw new ValidationFailedException(Messages.PayrollNoEmployees);
            await EnsureNotPostedAsync(run.Period, token);
            // مسودة الشهر السابقة تُستبدل وتحتفظ برقمها
            run.RunNumber = await RemoveDraftAsync(run.Period, token) ?? await _numbers.NextAsync("payroll_run", "PAY-", token);
            run.Status = Draft;
            _db.Add(run);
            await _db.SaveChangesAsync(token);
            return Map(run, run.Status);
        }, ct);

    public Task<PayrollRunDto> ApproveAsync(Guid id, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var draft = await _db.Set<PayrollRun>().Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, token)
                ?? throw new NotFoundException(Messages.PayrollRunNotFound);
            if (draft.Status != Draft) throw new ConflictException(Messages.PayrollRunNotDraft);
            await EnsureNotPostedAsync(draft.Period, token);

            // يُعاد الحساب من بيانات اليوم بإضافات وخصومات المسودة: ما اعتُمد هو ما رُوجع
            var (run, installments) = await BuildAsync(new PayrollRunRequestDto
            {
                Period = draft.Period, Notes = draft.Notes,
                Adjustments = draft.Lines.Select(l => new PayrollAdjustmentDto { EmployeeId = l.EmployeeId, Additions = l.Additions, Deductions = l.ManualDeductions }).ToList(),
            }, token);
            if (run.Lines.Count != draft.Lines.Count || run.TotalNet != draft.TotalNet || run.TotalGross != draft.TotalGross)
                throw new ConflictException(Messages.PayrollDraftStale);

            run.RunNumber = draft.RunNumber;
            _db.RemoveRange(draft.Lines);
            _db.Remove(draft);
            await _db.SaveChangesAsync(token);
            return await PostRunAsync(run, installments, token);
        }, ct);

    public async Task DeleteDraftAsync(Guid id, CancellationToken ct = default)
    {
        var draft = await _db.Set<PayrollRun>().Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException(Messages.PayrollRunNotFound);
        if (draft.Status != Draft) throw new ConflictException(Messages.PayrollRunNotDraft);
        _db.RemoveRange(draft.Lines);
        _db.Remove(draft);
        await _db.SaveChangesAsync(ct);
    }

    public Task<PayrollRunDto> PostAsync(PayrollRunRequestDto r, CancellationToken ct = default)
        => _tx.RunAsync(async token =>
        {
            var (run, installments) = await BuildAsync(r, token);
            if (run.Lines.Count == 0) throw new ValidationFailedException(Messages.PayrollNoEmployees);
            await EnsureNotPostedAsync(run.Period, token);
            run.RunNumber = await RemoveDraftAsync(run.Period, token) ?? await _numbers.NextAsync("payroll_run", "PAY-", token);
            return await PostRunAsync(run, installments, token);
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
            if (run.Status != Posted) throw new ConflictException(Messages.PayrollRunAlreadyReversed);
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
            run.Status = Reversed;
            await _db.SaveChangesAsync(token);
            await _audit.LogAsync("reverse", nameof(PayrollRun), run.Id.ToString(), $"{run.RunNumber} {run.Period}", token);
            return Map(run, run.Status);
        }, ct);

    public async Task<TextFileDto> GetWageFileAsync(Guid id, CancellationToken ct = default)
    {
        var run = await _db.Set<PayrollRun>().AsNoTracking().Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException(Messages.PayrollRunNotFound);
        if (run.Status != Posted) throw new ConflictException(Messages.PayrollWageFileNeedsPostedRun);
        var ids = run.Lines.Select(l => l.EmployeeId).ToList();
        var employees = await _db.Set<Employee>().AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);

        // البنك يرفض الملف إن نقص رقم هوية أو آيبان: يُذكر أصحابها بدل ملف ناقص
        var incomplete = run.Lines.Where(l => !employees.TryGetValue(l.EmployeeId, out var e) || string.IsNullOrWhiteSpace(e.Iban) || string.IsNullOrWhiteSpace(e.NationalId))
            .Select(l => l.EmployeeCode).OrderBy(c => c).ToList();
        if (incomplete.Count > 0) throw new ValidationFailedException(string.Format(Messages.PayrollWageFileMissingData, string.Join(", ", incomplete)));

        static string Amount(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        static string Text(string? v) => "\"" + (v ?? string.Empty).Replace("\"", "\"\"") + "\"";
        var csv = new StringBuilder();
        csv.AppendLine("EmployeeId,EmployeeName,BankName,IBAN,BasicSalary,HousingAllowance,OtherEarnings,Deductions,NetSalary");
        foreach (var l in run.Lines.OrderBy(l => l.EmployeeCode))
        {
            var e = employees[l.EmployeeId];
            // الأساسي + السكن + المكتسبات الأخرى − الاستقطاعات = الصافي
            var otherEarnings = l.Gross - l.BasicSalary - l.HousingAllowance;
            var deductions = l.Deductions + l.EmployeeGosi + l.LoanDeduction;
            csv.AppendLine(string.Join(",", Text(e.NationalId), Text(l.EmployeeName), Text(e.BankName), Text(e.Iban),
                Amount(l.BasicSalary), Amount(l.HousingAllowance), Amount(otherEarnings), Amount(deductions), Amount(l.Net)));
        }
        return new TextFileDto { FileName = $"WPS-{run.Period}-{run.RunNumber}.csv", Content = csv.ToString() };
    }

    // ---------- داخلي ----------

    private async Task EnsureNotPostedAsync(string period, CancellationToken ct)
    {
        if (await _db.Set<PayrollRun>().AnyAsync(p => p.Period == period && p.Status == Posted, ct))
            throw new ConflictException(string.Format(Messages.PayrollPeriodAlreadyPosted, period));
    }

    /// <summary>يحذف مسودة الشهر إن وُجدت ويعيد رقمها ليُعاد استخدامه.</summary>
    private async Task<string?> RemoveDraftAsync(string period, CancellationToken ct)
    {
        var draft = await _db.Set<PayrollRun>().Include(p => p.Lines).FirstOrDefaultAsync(p => p.Period == period && p.Status == Draft, ct);
        if (draft == null) return null;
        _db.RemoveRange(draft.Lines);
        _db.Remove(draft);
        await _db.SaveChangesAsync(ct);
        return draft.RunNumber;
    }

    /// <summary>يحفظ المسير مرحَّلاً ويصدر قيده ويسجّل أقساط السلف المخصومة سداداً على سلفها.</summary>
    private async Task<PayrollRunDto> PostRunAsync(PayrollRun run, List<(Guid LoanId, decimal Amount)> installments, CancellationToken token)
    {
        run.Status = Posted;
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
    }

    /// <summary>يحسب المسير من بيانات الموظفين وحضورهم وإجازاتهم وسلفهم وإضافات/خصومات الشهر دون حفظ.</summary>
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
        // حضور الشهر: غياب وتأخير يُخصمان، وإضافي يُضاف
        var attendance = (await _db.Set<AttendanceRecord>().AsNoTracking().Where(a => a.Date >= month && a.Date <= monthEnd).ToListAsync(ct))
            .ToLookup(a => a.EmployeeId);

        var run = new PayrollRun { Period = month.ToString("yyyy-MM", CultureInfo.InvariantCulture), Date = monthEnd, Notes = r.Notes };
        // سلف نشطة حلّ شهر خصمها (صيغة yyyy-MM تُقارن نصياً)
        var activeLoans = (await _db.Set<EmployeeLoan>().AsNoTracking()
                .Where(l => l.Status == LoanStatuses.Active && l.FirstDeductionPeriod.CompareTo(run.Period) <= 0).OrderBy(l => l.Date).ToListAsync(ct))
            .ToLookup(l => l.EmployeeId);
        var installments = new List<(Guid LoanId, decimal Amount)>();
        foreach (var e in employees)
        {
            var (additions, manualDeductions) = adjustments.GetValueOrDefault(e.Id);
            var package = AttendancePay.Package(e);
            // يوم بنصف أجر يُعدّ نصف يوم بلا أجر؛ الشهر 30 يوماً فلا يتجاوز الخصم الراتب الثابت
            var unpaidDays = Math.Min(LeaveBalances.PayrollMonthDays, unpaidLeaves[e.Id].Sum(l =>
                LeaveBalances.Span(l.StartDate > month ? l.StartDate : month, l.EndDate < monthEnd ? l.EndDate : monthEnd) * (100 - l.PayPercent) / 100));
            var leaveDeduction = DocumentPricing.Round(package / LeaveBalances.PayrollMonthDays * unpaidDays);

            var records = attendance[e.Id].ToList();
            var absentDays = Math.Min(LeaveBalances.PayrollMonthDays - unpaidDays, records.Count(a => a.Status == AttendanceStatuses.Absent));
            var lateMinutes = records.Sum(a => a.LateMinutes);
            var overtimeHours = records.Sum(a => a.OvertimeHours);
            var attendanceDeduction = AttendancePay.Deduction(e, absentDays, lateMinutes);
            var overtimePay = AttendancePay.Overtime(e, overtimeHours);

            var deductions = manualDeductions + leaveDeduction + attendanceDeduction;
            var gross = package + additions + overtimePay;
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
                Additions = additions, ManualDeductions = manualDeductions, Deductions = deductions,
                UnpaidLeaveDays = unpaidDays, LeaveDeduction = leaveDeduction,
                AbsentDays = absentDays, LateMinutes = lateMinutes, AttendanceDeduction = attendanceDeduction,
                OvertimeHours = overtimeHours, OvertimePay = overtimePay,
                LoanDeduction = loanDeduction, Gross = gross, EmployeeGosi = employeeGosi,
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
            Additions = l.Additions, ManualDeductions = l.ManualDeductions, Deductions = l.Deductions,
            UnpaidLeaveDays = l.UnpaidLeaveDays, LeaveDeduction = l.LeaveDeduction,
            AbsentDays = l.AbsentDays, LateMinutes = l.LateMinutes, AttendanceDeduction = l.AttendanceDeduction,
            OvertimeHours = l.OvertimeHours, OvertimePay = l.OvertimePay,
            LoanDeduction = l.LoanDeduction, Gross = l.Gross, EmployeeGosi = l.EmployeeGosi, EmployerGosi = l.EmployerGosi, Net = l.Net,
        }).ToList(),
    };
}
