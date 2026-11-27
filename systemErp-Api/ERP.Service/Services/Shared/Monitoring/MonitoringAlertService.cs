using System.Text.Json;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Data.ResponseModel;
using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// فحوص المنشأة اليدوية («تشغيل الفحص») وتنبيهاتها. كل قاعدة تقرأ مصدراً معتمداً يمكن التحقق منه (حالة الفاتورة، القيد،
/// سجل المحاولات، خدمة سلامة الدفاتر) ولا تحسب أرقاماً مالية بنفسها. البصمة تمنع تكرار التنبيه للمشكلة نفسها: الفحص التالي
/// يحدّث آخر اكتشاف، ويعيد فتح المغلق إن عادت المشكلة، ويعلّم «زال الشرط» إن لم تعد قائمة (والإغلاق قرار المستخدم).
/// لا مهام خلفية في هذه المرحلة.
/// </summary>
public sealed class MonitoringAlertService : IMonitoringAlertService
{
    /// <summary>أقصى تنبيهات تفصيلية لقاعدة واحدة في الفحص (الباقي يظهر في عدد القاعدة).</summary>
    private const int MaxPerRule = 200;
    /// <summary>تشغيل «جارٍ» أقدم من هذا يُعدّ منقطعاً ولا يمنع فحصاً جديداً.</summary>
    private static readonly TimeSpan RunStaleAfter = TimeSpan.FromMinutes(10);
    /// <summary>-1 = القاعدة لا تنطبق على المنشأة، -2 = تعذّر تنفيذها.</summary>
    private const int NotApplicable = -1, Failed = -2;

    private readonly ErpDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _user;
    private readonly IAccountingIntegrityService _integrity;
    private readonly ITransactionRunner _tx;

    public MonitoringAlertService(ErpDbContext db, ITenantContext tenant, ICurrentUser user, IAccountingIntegrityService integrity, ITransactionRunner tx)
    {
        _db = db; _tenant = tenant; _user = user; _integrity = integrity; _tx = tx;
    }

    private sealed record Detection(string RuleKey, AlertSeverity Severity, string Module, string Fingerprint, bool CauseConfirmed,
        string? SourceType = null, Guid? SourceId = null, string? SourceNumber = null, object? Params = null);

    private sealed record Rule(string Key, Func<CancellationToken, Task<List<Detection>?>> Detect);

    // =====================================================================
    // الفحص
    // =====================================================================

    public async Task<MonitoringCheckRunDto> RunChecksAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var busy = await _db.Set<MonitoringCheckRun>().AnyAsync(r => r.Status == "running" && r.StartedAt > now - RunStaleAfter, ct);
        if (busy) throw new ConflictException(Messages.MonitoringCheckAlreadyRunning);

        var run = new MonitoringCheckRun { StartedAt = now, Status = "running", RunByUserId = _user.UserId, RunByName = _user.Name ?? "system" };
        _db.Add(run);
        await _db.SaveChangesAsync(ct);

        var counts = new Dictionary<string, int>();
        var detections = new List<Detection>();
        var executed = new HashSet<string>();
        var failures = new List<string>();
        foreach (var rule in Rules())
        {
            try
            {
                var found = await rule.Detect(ct);
                if (found == null) { counts[rule.Key] = NotApplicable; executed.Add(rule.Key); continue; }
                counts[rule.Key] = found.Count;
                detections.AddRange(found.Take(MaxPerRule));
                executed.Add(rule.Key);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // قاعدة فشلت لا توقف بقية الفحص، ولا تُعلَّم تنبيهاتها «زال الشرط» (لم تُفحص)
                counts[rule.Key] = Failed;
                failures.Add(rule.Key);
            }
        }

        try
        {
            var (opened, cleared) = await _tx.RunAsync(token => ApplyAsync(detections, executed, now, token), ct);
            run.AlertsOpened = opened;
            run.AlertsCleared = cleared;
            run.Status = "succeeded";
            run.Error = failures.Count == 0 ? null : MonitoringText.Clip("rules failed: " + string.Join(", ", failures), 500);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // تراجعت المعاملة: ما تتبّعه السياق من تنبيهات وسجلها لا يُعاد حفظه مع سجل التشغيل
            foreach (var entry in _db.ChangeTracker.Entries().Where(e => e.Entity is MonitoringAlert or MonitoringAlertEvent).ToList())
                entry.State = EntityState.Detached;
            run.Status = "failed";
            run.Error = MonitoringText.Clip(MonitoringText.Scrub(ex.Message), 500);
        }
        run.CompletedAt = DateTime.UtcNow;
        run.SummaryJson = JsonSerializer.Serialize(counts);
        await _db.SaveChangesAsync(CancellationToken.None);
        return ToDto(run);
    }

    /// <summary>يدمج نتيجة الفحص مع التنبيهات القائمة (بالبصمة) ويسجل كل تغيير في سجل التنبيه.</summary>
    private async Task<(int Opened, int Cleared)> ApplyAsync(List<Detection> detections, HashSet<string> executed, DateTime now, CancellationToken ct)
    {
        var keys = executed.ToList();
        var existing = await _db.Set<MonitoringAlert>().Where(a => keys.Contains(a.RuleKey)).ToListAsync(ct);
        var byPrint = existing.ToDictionary(a => a.Fingerprint);
        var seen = new HashSet<string>();
        int opened = 0, cleared = 0;

        foreach (var d in detections)
        {
            if (!seen.Add(d.Fingerprint)) continue;
            var json = d.Params == null ? null : MonitoringText.Clip(JsonSerializer.Serialize(d.Params), 1000);
            if (byPrint.TryGetValue(d.Fingerprint, out var alert))
            {
                alert.LastDetectedAt = now;
                alert.OccurrenceCount++;
                alert.ParamsJson = json;
                alert.Severity = d.Severity;
                if (!alert.ConditionActive) alert.ConditionActive = true;
                if (alert.Status is AlertStatus.Resolved or AlertStatus.Closed)
                {
                    alert.Status = AlertStatus.Open;
                    alert.ResolvedAt = null;
                    alert.ResolvedByName = null;
                    AddEvent(alert.Id, "reopened", null, system: true, now);
                    opened++;
                }
                continue;
            }
            var created = new MonitoringAlert
            {
                RuleKey = d.RuleKey, Severity = d.Severity, Module = d.Module, Fingerprint = d.Fingerprint, CauseConfirmed = d.CauseConfirmed,
                SourceType = d.SourceType, SourceId = d.SourceId, SourceNumber = MonitoringText.Clip(d.SourceNumber, 64), ParamsJson = json,
                Status = AlertStatus.Open, ConditionActive = true, FirstDetectedAt = now, LastDetectedAt = now, OccurrenceCount = 1,
            };
            _db.Add(created); // معرّف Guid يُولَّد عند الإضافة فيُربط به السجل قبل الحفظ
            AddEvent(created.Id, "detected", null, system: true, now);
            opened++;
        }

        foreach (var alert in existing.Where(a => a.ConditionActive && !seen.Contains(a.Fingerprint)))
        {
            alert.ConditionActive = false;
            AddEvent(alert.Id, "condition_cleared", null, system: true, now);
            cleared++;
        }
        await _db.SaveChangesAsync(ct);
        return (opened, cleared);
    }

    // =====================================================================
    // القواعد (null = لا تنطبق على المنشأة)
    // =====================================================================

    private IEnumerable<Rule> Rules()
    {
        yield return new Rule("posted_invoice_without_journal", PostedWithoutJournalAsync);
        yield return new Rule("zatca_rejected", ZatcaRejectedAsync);
        yield return new Rule("zatca_submission_failed", ZatcaFailedAttemptsAsync);
        yield return new Rule("zatca_reporting_overdue", ZatcaOverdueAsync);
        yield return new Rule("balance_drift", BalanceDriftAsync);
        yield return new Rule("inventory_ledger_mismatch", InventoryMismatchAsync);
        yield return new Rule("negative_stock", NegativeStockAsync);
        yield return new Rule("failed_logins", FailedLoginsAsync);
        yield return new Rule("api_errors", ApiErrorsAsync);
    }

    /// <summary>مستند مرحّل بقيمة بلا قيد: الترحيل يجري في معاملة واحدة، فهذه حالة شاذة تُراجع (السبب غير مثبت).</summary>
    private async Task<List<Detection>?> PostedWithoutJournalAsync(CancellationToken ct)
    {
        var rows = await _db.Set<InvoiceBase>().AsNoTracking()
            .Where(i => i.Status == "posted" && i.JournalEntryId == null && i.GrandTotal != 0)
            .OrderByDescending(i => i.IssueDate).Take(MaxPerRule + 1)
            .Select(i => new { i.Id, i.InvoiceNumber, i.Kind, i.GrandTotal }).ToListAsync(ct);
        return rows.Select(r => new Detection("posted_invoice_without_journal", AlertSeverity.Critical, "accounting",
            $"posted_invoice_without_journal:{r.Id}", false, KindKey(r.Kind), r.Id, r.InvoiceNumber,
            new { number = r.InvoiceNumber, total = r.GrandTotal, kind = KindKey(r.Kind) })).ToList();
    }

    private async Task<bool> ZatcaEnabledAsync(CancellationToken ct)
        => await _db.Set<Tenant>().AsNoTracking().Select(t => t.ZatcaConfig.IsEnabled).FirstOrDefaultAsync(ct);

    /// <summary>فاتورة رفضتها الهيئة (حكم الهيئة نفسه): السبب مثبت في رسائلها.</summary>
    private async Task<List<Detection>?> ZatcaRejectedAsync(CancellationToken ct)
    {
        if (!await ZatcaEnabledAsync(ct)) return null;
        var rows = await _db.Set<InvoiceBase>().AsNoTracking()
            .Where(i => i.Status == "posted" && i.ZatcaStatus == ZatcaSubmissionStatus.Rejected)
            .OrderByDescending(i => i.IssueDate).Take(MaxPerRule + 1)
            .Select(i => new { i.Id, i.InvoiceNumber, i.Kind }).ToListAsync(ct);
        return rows.Select(r => new Detection("zatca_rejected", AlertSeverity.High, "zatca", $"zatca_rejected:{r.Id}", true,
            KindKey(r.Kind), r.Id, r.InvoiceNumber, new { number = r.InvoiceNumber })).ToList();
    }

    /// <summary>آخر محاولة لفاتورة (خلال 30 يوماً) فشلت قبل حكم الهيئة والفاتورة ما زالت لم تُرسل.</summary>
    private async Task<List<Detection>?> ZatcaFailedAttemptsAsync(CancellationToken ct)
    {
        if (!await ZatcaEnabledAsync(ct)) return null;
        var since = DateTime.UtcNow.AddDays(-30);
        var attempts = await _db.Set<ZatcaSubmissionAttempt>().AsNoTracking().Where(a => a.AttemptedAt >= since)
            .Select(a => new { a.InvoiceId, a.InvoiceNumber, a.DocumentKind, a.AttemptedAt, a.Outcome, a.IssueCodes, a.HttpStatus }).ToListAsync(ct);
        var latest = attempts.GroupBy(a => a.InvoiceId).Select(g => g.OrderByDescending(a => a.AttemptedAt).First())
            .Where(a => a.Outcome is ZatcaAttemptOutcome.PreCheckFailed or ZatcaAttemptOutcome.LocalError or ZatcaAttemptOutcome.Unreachable)
            .ToList();
        if (latest.Count == 0) return new();
        var ids = latest.Select(a => a.InvoiceId).ToList();
        var stillPending = (await _db.Set<InvoiceBase>().AsNoTracking()
            .Where(i => ids.Contains(i.Id) && i.ZatcaStatus == ZatcaSubmissionStatus.NotSubmitted).Select(i => i.Id).ToListAsync(ct)).ToHashSet();
        return latest.Where(a => stillPending.Contains(a.InvoiceId)).Select(a => new Detection("zatca_submission_failed",
            a.Outcome == ZatcaAttemptOutcome.Unreachable ? AlertSeverity.Medium : AlertSeverity.High, "zatca",
            $"zatca_submission_failed:{a.InvoiceId}", a.Outcome != ZatcaAttemptOutcome.Unreachable, a.DocumentKind, a.InvoiceId, a.InvoiceNumber,
            new { number = a.InvoiceNumber, outcome = CommandCenterService.ToSnake(a.Outcome.ToString()), codes = a.IssueCodes, http = a.HttpStatus })).ToList();
    }

    /// <summary>المبسطة تُبلَّغ خلال 24 ساعة من إصدارها: تنبيه واحد بعدد المتأخر (غير الخاضع للإرسال مستثنى).</summary>
    private async Task<List<Detection>?> ZatcaOverdueAsync(CancellationToken ct)
    {
        if (!await ZatcaEnabledAsync(ct)) return null;
        var before = DateTime.UtcNow.AddHours(-24);
        var count = await Overdue(_db.Set<SalesInvoice>(), before).CountAsync(i => !i.Items.Any(l => l.VatCategory == VatCategory.MarginScheme), ct)
            + await Overdue(_db.Set<SalesReturn>(), before).CountAsync(i => !i.Items.Any(l => l.VatCategory == VatCategory.MarginScheme), ct)
            + await Overdue(_db.Set<Invoice>(), before).CountAsync(i => !i.Items.Any(l => l.VatCategory == VatCategory.MarginScheme), ct);
        return count == 0 ? new() : new()
        {
            new("zatca_reporting_overdue", AlertSeverity.Medium, "zatca", "zatca_reporting_overdue", true, Params: new { count }),
        };
    }

    private static IQueryable<T> Overdue<T>(IQueryable<T> set, DateTime before) where T : InvoiceBase
        => set.AsNoTracking().Where(i => i.Status == "posted" && i.ZatcaStatus == ZatcaSubmissionStatus.NotSubmitted
            && i.Kind == InvoiceKind.Sales && i.InvoiceType == InvoiceType.Simplified && i.IssueDate < before);

    /// <summary>رصيد حساب مخزَّن يخالف مجموع قيوده (خدمة سلامة الدفاتر). قد يعود لترحيل لم يكتمل: السبب محتمل.</summary>
    private async Task<List<Detection>?> BalanceDriftAsync(CancellationToken ct)
    {
        var result = await _integrity.CheckBalancesAsync(ct);
        if (result.IsConsistent || result.DriftedCount == 0) return new();
        return new()
        {
            new("balance_drift", AlertSeverity.High, "accounting", "balance_drift", false,
                Params: new { count = result.DriftedCount, difference = result.TotalAbsoluteDifference }),
        };
    }

    /// <summary>قيمة المخزون في سجله التفصيلي تخالف أرصدة حسابات المخزون (خدمة سلامة الدفاتر).</summary>
    private async Task<List<Detection>?> InventoryMismatchAsync(CancellationToken ct)
    {
        var result = await _integrity.GetInventoryReconciliationAsync(ct);
        if (result.IsReconciled) return new();
        var difference = result.Rows.Sum(r => Math.Abs(r.Difference));
        return new()
        {
            new("inventory_ledger_mismatch", AlertSeverity.Medium, "inventory", "inventory_ledger_mismatch", false,
                Params: new { rows = result.Rows.Count(r => !r.IsReconciled), difference }),
        };
    }

    private async Task<List<Detection>?> NegativeStockAsync(CancellationToken ct)
    {
        var count = await _db.Set<Product>().AsNoTracking()
            .CountAsync(p => (p.ItemType == ProductItemTypes.Stock || p.ItemType == ProductItemTypes.Manufactured) && p.CurrentStock < 0, ct);
        return count == 0 ? new() : new() { new("negative_stock", AlertSeverity.Medium, "inventory", "negative_stock", true, Params: new { count }) };
    }

    /// <summary>خمس محاولات دخول فاشلة أو أكثر لمستخدمي المنشأة خلال 24 ساعة.</summary>
    private async Task<List<Detection>?> FailedLoginsAsync(CancellationToken ct)
    {
        var tenantId = _tenant.TenantId;
        var since = DateTime.UtcNow.AddHours(-24);
        var count = await _db.Catalog.Set<LoginEvent>().AsNoTracking()
            .CountAsync(e => e.TenantId == tenantId && e.OccurredAt >= since && e.Outcome != LoginOutcome.Succeeded, ct);
        return count < 5 ? new() : new() { new("failed_logins", AlertSeverity.Medium, "security", "failed_logins", true, Params: new { count }) };
    }

    private async Task<List<Detection>?> ApiErrorsAsync(CancellationToken ct)
    {
        var tenantId = _tenant.TenantId;
        var since = DateTime.UtcNow.AddHours(-24);
        var count = await _db.Catalog.Set<ApiErrorLog>().AsNoTracking().CountAsync(e => e.TenantId == tenantId && e.OccurredAt >= since, ct);
        return count == 0 ? new() : new() { new("api_errors", AlertSeverity.Low, "system", "api_errors", true, Params: new { count }) };
    }

    private static string KindKey(InvoiceKind kind) => CommandCenterService.ToSnake(kind.ToString());

    // =====================================================================
    // القراءة والإجراءات
    // =====================================================================

    public async Task<MonitoringCheckRunDto?> LatestRunAsync(CancellationToken ct = default)
    {
        var run = await _db.Set<MonitoringCheckRun>().AsNoTracking().OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync(ct);
        return run == null ? null : ToDto(run);
    }

    public Task<LoadResult> LoadAlertsAsync(DataSourceLoadOptions options, string? status, CancellationToken ct = default)
    {
        var query = _db.Set<MonitoringAlert>().AsNoTracking();
        var key = (status ?? "active").Trim().ToLowerInvariant();
        if (key == "active")
            query = query.Where(a => a.Status == AlertStatus.Open || a.Status == AlertStatus.Acknowledged || a.Status == AlertStatus.InProgress);
        else if (key != "all" && Enum.TryParse<AlertStatus>(key.Replace("_", string.Empty), ignoreCase: true, out var only))
            query = query.Where(a => a.Status == only);
        var rows = query.Select(a => new MonitoringAlertRowDto
        {
            Id = a.Id, RuleKey = a.RuleKey, Severity = a.Severity, Module = a.Module, SourceType = a.SourceType, SourceId = a.SourceId,
            SourceNumber = a.SourceNumber, ParamsJson = a.ParamsJson, CauseConfirmed = a.CauseConfirmed, Status = a.Status,
            ConditionActive = a.ConditionActive, FirstDetectedAt = a.FirstDetectedAt, LastDetectedAt = a.LastDetectedAt,
            OccurrenceCount = a.OccurrenceCount, AssignedToUserId = a.AssignedToUserId, AssignedToName = a.AssignedToName,
            ResolvedAt = a.ResolvedAt, ResolvedByName = a.ResolvedByName,
        });
        return ReportLoader.LoadAsync(rows, options, nameof(MonitoringAlertRowDto.Id), ct,
            new SortingInfo { Selector = nameof(MonitoringAlertRowDto.LastDetectedAt), Desc = true });
    }

    public async Task<MonitoringAlertDetailsDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var alert = await _db.Set<MonitoringAlert>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException(Messages.MonitoringAlertNotFound);
        var events = await _db.Set<MonitoringAlertEvent>().AsNoTracking().Where(e => e.AlertId == id).OrderByDescending(e => e.At)
            .Select(e => new MonitoringAlertEventDto { Id = e.Id, Action = e.Action, Note = e.Note, UserName = e.UserName, At = e.At })
            .ToListAsync(ct);
        return new MonitoringAlertDetailsDto { Alert = ToRow(alert), Events = events };
    }

    public async Task<MonitoringAlertDetailsDto> ActAsync(Guid id, MonitoringAlertActionRequestDto request, CancellationToken ct = default)
    {
        var alert = await _db.Set<MonitoringAlert>().FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException(Messages.MonitoringAlertNotFound);
        var now = DateTime.UtcNow;
        var note = MonitoringText.Clip(request.Note, 1000);
        var action = (request.Action ?? string.Empty).Trim().ToLowerInvariant();
        var active = alert.Status is AlertStatus.Open or AlertStatus.Acknowledged or AlertStatus.InProgress;

        switch (action)
        {
            case "acknowledge" when alert.Status == AlertStatus.Open:
                alert.Status = AlertStatus.Acknowledged;
                break;
            case "start" when alert.Status is AlertStatus.Open or AlertStatus.Acknowledged:
                alert.Status = AlertStatus.InProgress;
                break;
            case "assign" when active:
                var assignee = request.AssigneeUserId is { } uid
                    ? await _db.Set<User>().AsNoTracking().Where(u => u.Id == uid && u.IsActive).Select(u => new { u.Id, u.Name }).FirstOrDefaultAsync(ct)
                    : null;
                if (assignee == null) throw new ValidationFailedException(Messages.MonitoringAssigneeInvalid);
                alert.AssignedToUserId = assignee.Id;
                alert.AssignedToName = assignee.Name;
                note = MonitoringText.Clip(string.IsNullOrWhiteSpace(note) ? assignee.Name : $"{assignee.Name}: {note}", 1000);
                break;
            case "note":
                if (string.IsNullOrWhiteSpace(note)) throw new ValidationFailedException(Messages.MonitoringNoteRequired);
                break;
            case "resolve" when active:
                alert.Status = AlertStatus.Resolved;
                alert.ResolvedAt = now;
                alert.ResolvedByName = _user.Name ?? "system";
                break;
            case "close" when alert.Status != AlertStatus.Closed:
                alert.Status = AlertStatus.Closed;
                alert.ResolvedAt ??= now;
                alert.ResolvedByName ??= _user.Name ?? "system";
                break;
            case "reopen" when alert.Status is AlertStatus.Resolved or AlertStatus.Closed:
                alert.Status = AlertStatus.Open;
                alert.ResolvedAt = null;
                alert.ResolvedByName = null;
                break;
            default:
                throw new ConflictException(Messages.MonitoringActionInvalid);
        }

        AddEvent(alert.Id, action == "acknowledge" ? "acknowledged" : action == "assign" ? "assigned" : action == "start" ? "started"
            : action == "resolve" ? "resolved" : action == "close" ? "closed" : action == "reopen" ? "reopened" : "note", note, system: false, now);
        await _db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private void AddEvent(Guid alertId, string action, string? note, bool system, DateTime at)
        => _db.Add(new MonitoringAlertEvent
        {
            AlertId = alertId, Action = action, Note = note, At = at,
            UserId = system ? null : _user.UserId, UserName = system ? "system" : _user.Name ?? "system",
        });

    private static MonitoringAlertRowDto ToRow(MonitoringAlert a) => new()
    {
        Id = a.Id, RuleKey = a.RuleKey, Severity = a.Severity, Module = a.Module, SourceType = a.SourceType, SourceId = a.SourceId,
        SourceNumber = a.SourceNumber, ParamsJson = a.ParamsJson, CauseConfirmed = a.CauseConfirmed, Status = a.Status,
        ConditionActive = a.ConditionActive, FirstDetectedAt = a.FirstDetectedAt, LastDetectedAt = a.LastDetectedAt,
        OccurrenceCount = a.OccurrenceCount, AssignedToUserId = a.AssignedToUserId, AssignedToName = a.AssignedToName,
        ResolvedAt = a.ResolvedAt, ResolvedByName = a.ResolvedByName,
    };

    private static MonitoringCheckRunDto ToDto(MonitoringCheckRun r) => new()
    {
        Id = r.Id, StartedAt = r.StartedAt, CompletedAt = r.CompletedAt, Status = r.Status, RunByName = r.RunByName,
        AlertsOpened = r.AlertsOpened, AlertsCleared = r.AlertsCleared, Error = r.Error,
        Rules = string.IsNullOrEmpty(r.SummaryJson) ? new() : JsonSerializer.Deserialize<Dictionary<string, int>>(r.SummaryJson) ?? new(),
    };
}
