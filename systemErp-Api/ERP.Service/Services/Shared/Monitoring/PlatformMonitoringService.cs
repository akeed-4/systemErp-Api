using System.Diagnostics;
using System.Text.Json;
using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Data.ResponseModel;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// مراقبة المنصة لمدير المنصة (الحماية في الـ API بـ RequirePlatformAdmin). تقرأ القاعدة المشتركة وحدها (دليل المنشآت،
/// الاشتراكات، سجل الدخول، أخطاء الخادم) ولا تقرأ بيانات أعمال أي منشأة. قياس اتصال القواعد المستقلة يجري في «تشغيل الفحص»
/// فقط، بالتتابع وبمهلة لكل قاعدة، ونتيجته محفوظة؛ فتح اللوحة لا يتصل بأي قاعدة منشأة.
/// </summary>
public sealed class PlatformMonitoringService : IPlatformMonitoringService
{
    private const int DefaultDays = 7, MaxDays = 180;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RunStaleAfter = TimeSpan.FromMinutes(10);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ErpDbContext _db;
    private readonly ITenantConnectionResolver _resolver;
    private readonly ITenantContext _tenant;
    private readonly ICurrentUser _user;
    private readonly IPlatformSubscriptionService _subscriptions;

    public PlatformMonitoringService(ErpDbContext db, ITenantConnectionResolver resolver, ITenantContext tenant, ICurrentUser user,
        IPlatformSubscriptionService subscriptions)
    {
        _db = db; _resolver = resolver; _tenant = tenant; _user = user; _subscriptions = subscriptions;
    }

    private ErpDbContext Catalog => _db.Catalog;

    public async Task<PlatformMonitoringOverviewDto> OverviewAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var dto = new PlatformMonitoringOverviewDto { GeneratedAt = now, Subscriptions = await _subscriptions.SummaryAsync(ct) };

        var databases = await Catalog.Set<TenantDatabase>().AsNoTracking().Select(d => d.Status).ToListAsync(ct);
        dto.DedicatedReady = databases.Count(s => s == TenantDatabaseStatus.Ready);
        dto.DedicatedProvisioning = databases.Count(s => s == TenantDatabaseStatus.Provisioning);
        dto.DedicatedFailed = databases.Count(s => s == TenantDatabaseStatus.Failed);
        dto.SharedDatabaseTenants = Math.Max(0, dto.Subscriptions.TotalTenants - databases.Count);

        // زمن فعلي لاتصال القاعدة المشتركة (لا ادعاء لأداء الخادم أبعد من هذا)
        var watch = Stopwatch.StartNew();
        try { dto.CatalogReachable = await Catalog.Database.CanConnectAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { dto.CatalogReachable = false; }
        dto.CatalogLatencyMs = dto.CatalogReachable ? (int)watch.ElapsedMilliseconds : null;

        var day = now.AddHours(-24);
        var week = now.AddDays(-7);
        dto.ApiErrors24h = await Catalog.Set<ApiErrorLog>().AsNoTracking().CountAsync(e => e.OccurredAt >= day, ct);
        dto.ApiErrors7d = await Catalog.Set<ApiErrorLog>().AsNoTracking().CountAsync(e => e.OccurredAt >= week, ct);
        var logins = await Catalog.Set<LoginEvent>().AsNoTracking().Where(e => e.OccurredAt >= day)
            .GroupBy(e => e.Outcome == LoginOutcome.Succeeded).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        dto.SuccessfulLogins24h = logins.FirstOrDefault(x => x.Key)?.Count ?? 0;
        dto.FailedLogins24h = logins.FirstOrDefault(x => !x.Key)?.Count ?? 0;
        dto.ActiveModules = await ActiveModulesAsync(now, ct);
        dto.LastCheck = await LatestRunAsync(ct);
        return dto;
    }

    /// <summary>الوحدات المفعّلة في آخر اشتراك ساري (فعّال/تجريبي غير منتهٍ) لكل منشأة.</summary>
    private async Task<Dictionary<string, int>> ActiveModulesAsync(DateTime now, CancellationToken ct)
    {
        var subs = await Catalog.Set<Subscription>().IgnoreQueryFilters().AsNoTracking()
            .Select(s => new { s.TenantId, s.StartDate, s.CreatedAt, s.Status, s.ExpiryDate, s.ModuleKeys }).ToListAsync(ct);
        return subs.GroupBy(s => s.TenantId)
            .Select(g => g.OrderByDescending(s => s.StartDate).ThenByDescending(s => s.CreatedAt).First())
            .Where(s => s.Status is SubscriptionStatus.Active or SubscriptionStatus.Trial && s.ExpiryDate >= now)
            .SelectMany(s => PlatformModules.Parse(s.ModuleKeys))
            .GroupBy(m => m).ToDictionary(g => g.Key, g => g.Count());
    }

    public Task<LoadResult> ApiErrorsAsync(DataSourceLoadOptions options, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var (start, end) = Range(from, to);
        var tenants = Catalog.Set<Tenant>().IgnoreQueryFilters();
        var rows = Catalog.Set<ApiErrorLog>().AsNoTracking().Where(e => e.OccurredAt >= start && e.OccurredAt < end)
            .Select(e => new PlatformApiErrorRowDto
            {
                Id = e.Id, TenantId = e.TenantId, TenantName = tenants.Where(t => t.Id == e.TenantId).Select(t => t.NameAr).FirstOrDefault(),
                TraceId = e.TraceId, Method = e.Method, Path = e.Path, StatusCode = e.StatusCode, ExceptionType = e.ExceptionType,
                Source = e.Source, Message = e.Message, OccurredAt = e.OccurredAt,
            });
        return ReportLoader.LoadAsync(rows, options, nameof(PlatformApiErrorRowDto.Id), ct,
            new SortingInfo { Selector = nameof(PlatformApiErrorRowDto.OccurredAt), Desc = true });
    }

    public Task<LoadResult> LoginEventsAsync(DataSourceLoadOptions options, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var (start, end) = Range(from, to);
        var tenants = Catalog.Set<Tenant>().IgnoreQueryFilters();
        var rows = Catalog.Set<LoginEvent>().AsNoTracking().Where(e => e.OccurredAt >= start && e.OccurredAt < end)
            .Select(e => new LoginEventRowDto
            {
                Id = e.Id, TenantId = e.TenantId, TenantName = tenants.Where(t => t.Id == e.TenantId).Select(t => t.NameAr).FirstOrDefault(),
                UserId = e.UserId, Identifier = e.Identifier, Outcome = e.Outcome, IpAddress = e.IpAddress, UserAgent = e.UserAgent,
                OccurredAt = e.OccurredAt,
            });
        return ReportLoader.LoadAsync(rows, options, nameof(LoginEventRowDto.Id), ct,
            new SortingInfo { Selector = nameof(LoginEventRowDto.OccurredAt), Desc = true });
    }

    // =====================================================================
    // الفحص اليدوي
    // =====================================================================

    public async Task<PlatformCheckRunDto> RunChecksAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        if (await Catalog.Set<PlatformCheckRun>().AnyAsync(r => r.Status == "running" && r.StartedAt > now - RunStaleAfter, ct))
            throw new ConflictException(Messages.MonitoringCheckAlreadyRunning);
        var run = new PlatformCheckRun { Id = Guid.NewGuid(), StartedAt = now, Status = "running", RunByName = _user.Name ?? "system" };
        Catalog.Add(run);
        await Catalog.SaveChangesAsync(ct);

        var result = new PlatformCheckRunDto { Id = run.Id, StartedAt = now, RunByName = run.RunByName };
        try
        {
            var names = await Catalog.Set<Tenant>().IgnoreQueryFilters().AsNoTracking().Select(t => new { t.Id, t.NameAr }).ToDictionaryAsync(t => t.Id, t => t.NameAr, ct);
            result.Databases = await ProbeDatabasesAsync(names, ct);
            result.Alerts = await PlatformAlertsAsync(result.Databases, names, now, ct);
            run.Status = "succeeded";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Status = "failed";
            run.Error = MonitoringText.Clip(MonitoringText.Scrub(ex.Message), 500);
        }
        run.CompletedAt = DateTime.UtcNow;
        result.Status = run.Status;
        result.CompletedAt = run.CompletedAt;
        result.Error = run.Error;
        run.ResultJson = JsonSerializer.Serialize(result, Json);
        await Catalog.SaveChangesAsync(CancellationToken.None);
        return result;
    }

    /// <summary>كل قاعدة مستقلة جاهزة: محاولة اتصال واحدة بمهلة 5 ثوانٍ (بالتتابع). القيد التجهيزي/الفاشل يُعرض بحالته وسببه المقصوص.</summary>
    private async Task<List<PlatformDatabaseHealthDto>> ProbeDatabasesAsync(Dictionary<Guid, string> names, CancellationToken ct)
    {
        var list = new List<PlatformDatabaseHealthDto>();
        var databases = await Catalog.Set<TenantDatabase>().AsNoTracking().Select(d => new { d.TenantId, d.Status, d.LastError }).ToListAsync(ct);
        foreach (var d in databases)
        {
            var row = new PlatformDatabaseHealthDto
            {
                TenantId = d.TenantId, TenantName = names.GetValueOrDefault(d.TenantId, string.Empty),
                Status = d.Status.ToString().ToLowerInvariant(), LastError = MonitoringText.Clip(MonitoringText.Scrub(d.LastError), 300),
            };
            if (d.Status == TenantDatabaseStatus.Ready)
            {
                var connection = await _resolver.ReadyDedicatedAsync(d.TenantId, ct);
                if (connection != null)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(ProbeTimeout);
                    var watch = Stopwatch.StartNew();
                    try
                    {
                        await using var probe = new ErpDbContext(_resolver.OptionsFor(connection), _tenant);
                        row.Reachable = await probe.Database.CanConnectAsync(timeout.Token);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                    {
                        row.Reachable = false;
                    }
                    row.LatencyMs = row.Reachable == true ? (int)watch.ElapsedMilliseconds : null;
                }
            }
            list.Add(row);
        }
        return list;
    }

    /// <summary>تنبيهات المنصة المحسوبة من البيانات الفعلية (لقطة الفحص).</summary>
    private async Task<List<PlatformAlertDto>> PlatformAlertsAsync(List<PlatformDatabaseHealthDto> databases, Dictionary<Guid, string> names,
        DateTime now, CancellationToken ct)
    {
        var alerts = new List<PlatformAlertDto>();
        foreach (var d in databases)
        {
            if (d.Reachable == false)
                alerts.Add(new() { RuleKey = "dedicated_db_unreachable", Severity = "critical", TenantId = d.TenantId, TenantName = d.TenantName });
            else if (d.Status == "failed")
                alerts.Add(new() { RuleKey = "dedicated_db_failed", Severity = "high", TenantId = d.TenantId, TenantName = d.TenantName });
            else if (d.Status == "provisioning")
                alerts.Add(new() { RuleKey = "dedicated_db_provisioning", Severity = "medium", TenantId = d.TenantId, TenantName = d.TenantName });
        }

        var hour = now.AddHours(-1);
        var errors = await Catalog.Set<ApiErrorLog>().AsNoTracking().CountAsync(e => e.OccurredAt >= hour, ct);
        if (errors >= 10)
            alerts.Add(new() { RuleKey = "api_errors_spike", Severity = "high", Params = new() { ["count"] = errors.ToString() } });

        var failedByTenant = await Catalog.Set<LoginEvent>().AsNoTracking()
            .Where(e => e.OccurredAt >= hour && e.Outcome != LoginOutcome.Succeeded)
            .GroupBy(e => e.TenantId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        foreach (var f in failedByTenant.Where(f => f.Key != null && f.Count >= 20))
            alerts.Add(new()
            {
                RuleKey = "failed_logins_spike", Severity = "medium", TenantId = f.Key, TenantName = names.GetValueOrDefault(f.Key!.Value, string.Empty),
                Params = new() { ["count"] = f.Count.ToString() },
            });
        var unknown = failedByTenant.Where(f => f.Key == null).Sum(f => f.Count);
        if (unknown >= 50)
            alerts.Add(new() { RuleKey = "failed_logins_unknown_spike", Severity = "medium", Params = new() { ["count"] = unknown.ToString() } });

        var summary = await _subscriptions.SummaryAsync(ct);
        if (summary.ExpiringSoon > 0)
            alerts.Add(new() { RuleKey = "subscriptions_expiring", Severity = "info", Params = new() { ["count"] = summary.ExpiringSoon.ToString() } });
        return alerts;
    }

    public async Task<PlatformCheckRunDto?> LatestRunAsync(CancellationToken ct = default)
    {
        var run = await Catalog.Set<PlatformCheckRun>().AsNoTracking().OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync(ct);
        if (run == null) return null;
        var dto = string.IsNullOrEmpty(run.ResultJson) ? null : JsonSerializer.Deserialize<PlatformCheckRunDto>(run.ResultJson, Json);
        dto ??= new PlatformCheckRunDto();
        dto.Id = run.Id; dto.StartedAt = run.StartedAt; dto.CompletedAt = run.CompletedAt; dto.Status = run.Status;
        dto.RunByName = run.RunByName; dto.Error = run.Error;
        return dto;
    }

    private static (DateTime Start, DateTime End) Range(DateTime? from, DateTime? to)
    {
        var end = to.HasValue ? to.Value.Date.AddDays(1) : DateTime.UtcNow;
        var start = from?.Date ?? end.AddDays(-DefaultDays);
        if (start < end.AddDays(-MaxDays)) start = end.AddDays(-MaxDays);
        return (start, end);
    }
}
