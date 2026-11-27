using DevExtreme.AspNet.Data;
using DevExtreme.AspNet.Data.ResponseModel;
using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>
/// قراءات مركز مراقبة المنشأة. للقراءة فقط (AsNoTracking)، ومنشأة المستخدم وحدها (مرشّح المنشأة على بياناتها، و`TenantId`
/// صريح على جداول المنصة). المبالغ المالية من الخدمات المعتمدة نفسها (لوحة المؤشرات والإحصاءات المالية): لا حساب مكرر.
/// القوائم بترقيم وفلترة الخادم (ReportLoader) وبفترة افتراضية محدودة، فلا يُسحب سجل كامل.
/// </summary>
public sealed class CommandCenterService : ICommandCenterService
{
    /// <summary>الفترة الافتراضية للسجلات إن لم تُحدَّد.</summary>
    private const int DefaultDays = 7;
    /// <summary>أقصى مدى لسجلات المراقبة (الاحتفاظ التشغيلي 180 يوماً).</summary>
    private const int MaxDays = 180;

    private readonly ErpDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IDashboardService _dashboard;
    private readonly IAccountingReportService _reports;
    private readonly IMonitoringAlertService _alerts;

    public CommandCenterService(ErpDbContext db, ITenantContext tenant, IDashboardService dashboard, IAccountingReportService reports,
        IMonitoringAlertService alerts)
    {
        _db = db; _tenant = tenant; _dashboard = dashboard; _reports = reports; _alerts = alerts;
    }

    private Guid TenantId => _tenant.TenantId ?? throw new ForbiddenException();

    public async Task<CommandCenterOverviewDto> OverviewAsync(string? period, CancellationToken ct = default)
    {
        var performance = await _dashboard.GetSalesPerformanceAsync(period, null, ct);
        var stats = await _reports.GetFinancialStatsAsync(ct);
        return new CommandCenterOverviewDto
        {
            Period = performance.Period, From = performance.From, To = performance.To,
            Current = performance.Current, Previous = performance.Previous,
            CashAndBankBalance = stats.CashAndBankBalance, ReceivablesBalance = stats.ReceivablesBalance,
            PayablesBalance = stats.PayablesBalance, InventoryValuation = stats.InventoryValuation,
            GeneratedAt = DateTime.UtcNow,
        };
    }

    public async Task<CommandCenterLiveDto> LiveAsync(CancellationToken ct = default)
    {
        var tenantId = TenantId;
        var now = DateTime.UtcNow;
        var zone = await TenantTimeZone.ForCurrentTenantAsync(_db, ct);
        var localNow = TenantTimeZone.ToLocal(now, zone);
        var dayStart = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localNow.Date, DateTimeKind.Unspecified), zone);
        var since24h = now.AddHours(-24);

        // مستندات اليوم المرحّلة حسب النوع (أُنشئت اليوم بتوقيت المنشأة)
        var invoices = await _db.Set<InvoiceBase>().AsNoTracking()
            .Where(i => i.CreatedAt >= dayStart && i.Status == "posted")
            .GroupBy(i => i.Kind).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int Kind(InvoiceKind k) => invoices.FirstOrDefault(x => x.Key == k)?.Count ?? 0;

        var live = new CommandCenterLiveDto
        {
            GeneratedAt = now,
            DayStart = dayStart,
            SalesInvoicesToday = Kind(InvoiceKind.Sales),
            PurchaseInvoicesToday = Kind(InvoiceKind.Purchase),
            ReturnsToday = Kind(InvoiceKind.SalesReturn) + Kind(InvoiceKind.PurchaseReturn),
            JournalEntriesToday = await _db.Set<JournalEntry>().AsNoTracking().CountAsync(j => j.CreatedAt >= dayStart, ct),
            StockMovementsToday = await _db.Set<StockMovement>().AsNoTracking().CountAsync(m => m.CreatedAt >= dayStart, ct),
            AuditEventsToday = await _db.Set<AuditLog>().AsNoTracking().CountAsync(a => a.Timestamp >= dayStart, ct),
            TotalActiveUsers = await _db.Set<User>().AsNoTracking().CountAsync(u => u.IsActive, ct),
            ActiveUsers24h = await _db.Set<User>().AsNoTracking().CountAsync(u => u.IsActive && u.LastLoginAt >= since24h, ct),
            // جدول المنصة في القاعدة المشتركة: المنشأة صراحةً
            FailedLogins24h = await _db.Catalog.Set<LoginEvent>().AsNoTracking()
                .CountAsync(e => e.TenantId == tenantId && e.OccurredAt >= since24h && e.Outcome != LoginOutcome.Succeeded, ct),
            Zatca = await ZatcaSummaryAsync(now, ct),
            LastCheck = await _alerts.LatestRunAsync(ct),
        };

        var open = await _db.Set<MonitoringAlert>().AsNoTracking()
            .Where(a => a.Status == AlertStatus.Open || a.Status == AlertStatus.Acknowledged || a.Status == AlertStatus.InProgress)
            .GroupBy(a => a.Severity).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        live.OpenAlertsBySeverity = open.ToDictionary(x => x.Key.ToString().ToLowerInvariant(), x => x.Count);
        return live;
    }

    /// <summary>
    /// حالة الإرسال للهيئة من حالة الفواتير نفسها (المصدر المعتمد) ومن سجل المحاولات لآخر 30 يوماً. غير الخاضع للإرسال
    /// (المشتريات ومرتجعاتها، فواتير هامش الربح، الربط غير مفعّل) لا يُعدّ معلقاً ولا فاشلاً.
    /// </summary>
    private async Task<CommandCenterZatcaSummaryDto> ZatcaSummaryAsync(DateTime now, CancellationToken ct)
    {
        var tenant = await _db.Set<Tenant>().AsNoTracking().Select(t => new { t.ZatcaConfig.IsEnabled }).FirstOrDefaultAsync(ct);
        var dto = new CommandCenterZatcaSummaryDto { Enabled = tenant?.IsEnabled == true };

        var statuses = await _db.Set<InvoiceBase>().AsNoTracking()
            .Where(i => i.Status == "posted" && (i.Kind == InvoiceKind.Sales || i.Kind == InvoiceKind.SalesReturn))
            .GroupBy(i => i.ZatcaStatus).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int Of(ZatcaSubmissionStatus s) => statuses.FirstOrDefault(x => x.Key == s)?.Count ?? 0;
        dto.Cleared = Of(ZatcaSubmissionStatus.Cleared);
        dto.Reported = Of(ZatcaSubmissionStatus.Reported);
        dto.AcceptedWithWarnings = Of(ZatcaSubmissionStatus.Warning);
        dto.Rejected = Of(ZatcaSubmissionStatus.Rejected);

        if (dto.Enabled)
        {
            var overdueBefore = now.AddHours(-24);
            // كل جدول بأسطره (TPC): فواتير البيع ومرتجعاتها وفواتير المعارض
            var sales = Pending(_db.Set<SalesInvoice>()).Where(i => !i.Items.Any(l => l.VatCategory == VatCategory.MarginScheme));
            var returns = Pending(_db.Set<SalesReturn>()).Where(i => !i.Items.Any(l => l.VatCategory == VatCategory.MarginScheme));
            var showroom = Pending(_db.Set<Invoice>()).Where(i => !i.Items.Any(l => l.VatCategory == VatCategory.MarginScheme));
            dto.Pending = await sales.CountAsync(ct) + await returns.CountAsync(ct) + await showroom.CountAsync(ct);
            dto.PendingOverdue = await sales.CountAsync(i => i.InvoiceType == InvoiceType.Simplified && i.IssueDate < overdueBefore, ct)
                + await returns.CountAsync(i => i.InvoiceType == InvoiceType.Simplified && i.IssueDate < overdueBefore, ct)
                + await showroom.CountAsync(i => i.InvoiceType == InvoiceType.Simplified && i.IssueDate < overdueBefore, ct);
        }

        var since = now.AddDays(-30);
        var attempts = await _db.Set<ZatcaSubmissionAttempt>().AsNoTracking().Where(a => a.AttemptedAt >= since)
            .GroupBy(a => a.Outcome).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        dto.AttemptsByOutcome = attempts.ToDictionary(x => ToSnake(x.Key.ToString()), x => x.Count);
        return dto;
    }

    /// <summary>فواتير بيع مرحّلة لم تُرسل (الأسطر المستثناة — هامش الربح — يفلترها المستدعي بجدول أسطر نوعها).</summary>
    private static IQueryable<T> Pending<T>(IQueryable<T> set) where T : InvoiceBase
        => set.AsNoTracking().Where(i => i.Status == "posted" && i.ZatcaStatus == ZatcaSubmissionStatus.NotSubmitted
            && (i.Kind == InvoiceKind.Sales || i.Kind == InvoiceKind.SalesReturn));

    public Task<LoadResult> ZatcaAttemptsAsync(DataSourceLoadOptions options, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var (start, end) = Range(from, to);
        var rows = _db.Set<ZatcaSubmissionAttempt>().AsNoTracking()
            .Where(a => a.AttemptedAt >= start && a.AttemptedAt < end)
            .Select(a => new ZatcaAttemptRowDto
            {
                Id = a.Id, InvoiceId = a.InvoiceId, InvoiceNumber = a.InvoiceNumber, DocumentKind = a.DocumentKind, DocumentUuid = a.DocumentUuid,
                Icv = a.Icv, UserName = a.UserName, AttemptedAt = a.AttemptedAt, DurationMs = a.DurationMs, Outcome = a.Outcome,
                HttpStatus = a.HttpStatus, IssueCodes = a.IssueCodes, Message = a.Message, IsResubmission = a.IsResubmission,
            });
        return ReportLoader.LoadAsync(rows, options, nameof(ZatcaAttemptRowDto.Id), ct, new SortingInfo { Selector = nameof(ZatcaAttemptRowDto.AttemptedAt), Desc = true });
    }

    public Task<LoadResult> AuditActivityAsync(DataSourceLoadOptions options, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var (start, end) = Range(from, to);
        var rows = _db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.Timestamp >= start && a.Timestamp < end)
            .Select(a => new AuditActivityRowDto
            {
                Id = a.Id, Timestamp = a.Timestamp, Action = a.Action, EntityName = a.EntityName, EntityId = a.EntityId,
                PerformedBy = a.PerformedBy, UserId = a.UserId, Details = a.Details,
            });
        return ReportLoader.LoadAsync(rows, options, nameof(AuditActivityRowDto.Id), ct, new SortingInfo { Selector = nameof(AuditActivityRowDto.Timestamp), Desc = true });
    }

    public Task<LoadResult> LoginEventsAsync(DataSourceLoadOptions options, DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var tenantId = TenantId;
        var (start, end) = Range(from, to);
        var rows = _db.Catalog.Set<LoginEvent>().AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.OccurredAt >= start && e.OccurredAt < end)
            .Select(e => new LoginEventRowDto
            {
                Id = e.Id, TenantId = e.TenantId, UserId = e.UserId, Identifier = e.Identifier, Outcome = e.Outcome,
                IpAddress = e.IpAddress, UserAgent = e.UserAgent, OccurredAt = e.OccurredAt,
            });
        return ReportLoader.LoadAsync(rows, options, nameof(LoginEventRowDto.Id), ct, new SortingInfo { Selector = nameof(LoginEventRowDto.OccurredAt), Desc = true });
    }

    /// <summary>أصناف ذات رصيد (بضاعة/منتج صناعي) برصيد سالب أو تحت حد الطلب المعرّف على الصنف. حتى 200 صنف بالأشد أولاً.</summary>
    public async Task<List<InventoryAlertRowDto>> InventoryAlertsAsync(CancellationToken ct = default)
    {
        var rows = await _db.Set<Product>().AsNoTracking()
            .Where(p => (p.ItemType == ProductItemTypes.Stock || p.ItemType == ProductItemTypes.Manufactured || p.ItemType == null || p.ItemType == "")
                && (p.CurrentStock < 0 || (p.MinStockLevel > 0 && p.CurrentStock < p.MinStockLevel)))
            .OrderBy(p => p.CurrentStock < 0 ? 0 : 1).ThenBy(p => p.CurrentStock - p.MinStockLevel)
            .Take(200)
            .Select(p => new InventoryAlertRowDto
            {
                ProductId = p.Id, Sku = p.Sku, NameAr = p.NameAr, NameEn = p.NameEn, Unit = p.Unit,
                CurrentStock = p.CurrentStock, MinStockLevel = p.MinStockLevel,
                Kind = p.CurrentStock < 0 ? "negative_stock" : "below_min",
            })
            .ToListAsync(ct);
        return rows;
    }

    /// <summary>الفترة المطلوبة (الافتراضي آخر 7 أيام)، بحد أقصى 180 يوماً.</summary>
    private static (DateTime Start, DateTime End) Range(DateTime? from, DateTime? to)
    {
        var end = (to ?? DateTime.UtcNow).AddDays(to.HasValue ? 1 : 0);
        if (to.HasValue) end = end.Date;
        var start = from?.Date ?? end.AddDays(-DefaultDays);
        if (start < end.AddDays(-MaxDays)) start = end.AddDays(-MaxDays);
        return (start, end);
    }

    internal static string ToSnake(string pascal)
        => string.Concat(pascal.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}
