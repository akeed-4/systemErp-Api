using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting.Sales;
using ERP.Service.Data;
using ERP.Service.Services.Shared;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// الربط مع هيئة الزكاة (المرحلة الثانية): إصدار شهادة وحدة إصدار الفواتير، ثم توقيع كل فاتورة مبيعات مرحّلة
/// وإرسالها — المبسطة تُبلَّغ والضريبية تُعتمد. الربط لكل مستخدم: وحدته (<see cref="ZatcaUnit"/>) تحمل شهادته
/// وسلسلة فواتيره (عدّاد + تجزئة السابقة) التي لا تنقطع، والفاتورة تُرسل بوحدة المستخدم الذي يرسلها.
/// </summary>
public class ZatcaService : IZatcaService
{
    private const string ProtectedPrefix = "enc:";
    private const string SampleBuyerVat = "399999999800003";
    /// <summary>أنواع الفواتير في الشهادة (TSCZ): ضريبية ومبسطة.</summary>
    private const string InvoiceTypes = "1100";

    // حالات طلب الشهادة: «جارٍ» يُحفظ قبل الإرسال، و«مجهول» حين لم يُعرف رد الهيئة، و«مغلقة» بقرار المسؤول (لا تُحذف)
    private const string ComplianceRequestingStatus = "compliance_csid_requesting";
    private const string ComplianceUnknownStatus = "compliance_csid_unknown";
    private const string ProductionRequestingStatus = "production_requesting";
    private const string ProductionUnknownStatus = "production_unknown";
    /// <summary>الهيئة أصدرت شهادة الامتثال لكن حفظها محلياً فشل (السبب في ErrorMessage).</summary>
    private const string ComplianceUnsavedStatus = "compliance_csid_unsaved";
    private const string AbandonedStatus = "compliance_csid_abandoned";
    private static readonly string[] StuckStatuses = { ComplianceRequestingStatus, ComplianceUnknownStatus, ComplianceUnsavedStatus, ProductionRequestingStatus, ProductionUnknownStatus };
    /// <summary>بعدها لا يمكن أن يكون الطلب جارياً: مهلة عميل الهيئة مع فحص الوصول وهامش أمان.</summary>
    private static readonly TimeSpan RequestStaleAfter = ZatcaApiClient.ClientTimeout + ZatcaApiClient.ProbeTimeout + TimeSpan.FromMinutes(1);

    /// <summary>إرسال واحد في كل مرة للوحدة: العدّاد والتجزئة السابقة يؤخذان ويُحدَّثان بالتسلسل.</summary>
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Gates = new();
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> OnboardingGates = new();

    private readonly ErpDbContext _db;
    private readonly IZatcaApiClient _api;
    private readonly ISecretProtector _secrets;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _user;
    private readonly IMonitoringRecorder? _monitoring;
    /// <summary>محاولة الإرسال الجارية (مركز المراقبة): تُملأ عند كل مخرج من الإرسال وتُسجَّل بعده في سياق مستقل.</summary>
    private ZatcaSubmissionAttempt? _attempt;

    public ZatcaService(ErpDbContext db, IZatcaApiClient api, ISecretProtector secrets, IAuditService audit, ICurrentUser user,
        IMonitoringRecorder? monitoring = null)
    {
        _db = db; _api = api; _secrets = secrets; _audit = audit; _user = user; _monitoring = monitoring;
    }

    public string GenerateTlvQr(string sellerName, string vatNumber, DateTime timestamp, decimal total, decimal vat)
        => ZatcaTlv.Encode(sellerName, vatNumber, timestamp, total, vat);

    // ---------------- الربط ----------------

    public async Task<TenantDto> OnboardAsync(ZatcaOnboardRequestDto request, CancellationToken ct = default)
    {
        var targetId = request.UserId ?? _user.UserId ?? Guid.Empty;
        var gate = OnboardingGates.GetOrAdd(targetId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try { return await OnboardCoreAsync(request, ct); }
        finally { gate.Release(); }
    }

    private async Task<TenantDto> OnboardCoreAsync(ZatcaOnboardRequestDto request, CancellationToken ct)
    {
        var tenant = await _db.Set<Tenant>().FirstAsync(ct);
        var config = tenant.ZatcaConfig;
        var otp = request.Otp?.Trim() ?? string.Empty;

        // المستخدم يربط نفسه مباشرة؛ ربط مستخدم آخر للمالك ومدير النظام
        var me = _user.UserId ?? throw new UnauthorizedAppException();
        var targetId = request.UserId ?? me;
        if (targetId != me && !IsAdmin) throw new ForbiddenException();
        var target = await _db.Set<User>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == targetId, ct) ?? throw new NotFoundException(Messages.ZatcaUserNotFound);
        if (!target.IsActive || target.Role == UserRole.Employee) throw new ValidationFailedException(Messages.ZatcaUserNotFound);
        var unit = await _db.Set<ZatcaUnit>().FirstOrDefaultAsync(u => u.UserId == targetId, ct);
        // الجلسة المغلقة بقرار المسؤول تبقى للتدقيق ولا تُستأنف: المحاولة التالية تبدأ جلسة جديدة بمفتاح جديد
        var session = await _db.Set<ZatcaOnboardingSession>()
            .Where(s => s.UserId == targetId && s.Environment == config.Environment && s.Status != "completed" && s.Status != AbandonedStatus && s.Status != "abandoned")
            .OrderByDescending(s => s.StartedAt).FirstOrDefaultAsync(ct);

        if (session != null) await ExpireStaleRequestAsync(session);
        if (session?.Status is ComplianceRequestingStatus or ProductionRequestingStatus)
            throw new ConflictException(Messages.ZatcaOnboardingInProgress);
        if (session?.Status is ComplianceUnknownStatus or ProductionUnknownStatus)
            throw new ConflictException(session.Status == ProductionUnknownStatus ? Messages.ZatcaProductionUnresolved : Messages.ZatcaOnboardingUnresolved);

        var missing = new List<string>();
        if (otp.Length == 0 && session?.ComplianceRequestId == null) missing.Add(Messages.ZatcaFieldOtp);
        if (!SaudiVat.IsValid(tenant.VatNumber)) missing.Add(Messages.ZatcaFieldVatNumber);
        if (string.IsNullOrWhiteSpace(config.Street)) missing.Add(Messages.BuyerFieldStreet);
        if (!IsDigits(config.BuildingNumber, 4)) missing.Add(Messages.BuyerFieldBuildingNo);
        if (string.IsNullOrWhiteSpace(config.District)) missing.Add(Messages.BuyerFieldDistrict);
        if (string.IsNullOrWhiteSpace(tenant.City)) missing.Add(Messages.BuyerFieldCity);
        if (!IsDigits(config.PostalCode, 5)) missing.Add(Messages.BuyerFieldPostalCode);
        if (missing.Count > 0)
        {
            var message = string.Format(Messages.ZatcaOnboardingDataRequired, string.Join("، ", missing));
            throw new ValidationFailedException(message, missing.Prepend(message));
        }

        var baseUrl = BaseUrl(config);
        var seller = Seller(tenant);
        if (session == null)
        {
            if (otp.Length == 0) throw new ValidationFailedException(Messages.ZatcaFieldOtp);
            session = CreateOnboardingSession(target, unit, config, tenant);
            _db.Add(session);
            if (unit == null || !unit.IsActive) config.ComplianceStatus = ComplianceStatus.InProgress;
            await _db.SaveChangesAsync(ct);
        }

        if (session.ComplianceRequestId == null)
        {
            // فحص الوصول قبل الالتزام: انقطاع الاتصال الخارجي يُرفض هنا والجلسة قابلة لإعادة المحاولة، فلا تُعلَّق
            await EnsureReachableAsync(baseUrl, ct);

            session.Status = ComplianceRequestingStatus;
            session.ErrorMessage = null;
            await _db.SaveChangesAsync(CancellationToken.None);
            // منذ هذه اللحظة لا يُلغى الطلب بانقطاع اتصال العميل أو إلغاء الطلب: مهلة عميل HTTP وحدها تحدّه، ومصيره
            // (شهادة أو رفض أو مجهول) يُحفظ دائماً، فلا تبقى الجلسة «جارية» إلا إذا توقفت العملية نفسها
            ZatcaCsidResult compliance;
            try
            {
                compliance = await _api.RequestComplianceCsidAsync(baseUrl, session.Csr, otp, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // لا رد مؤكد من الهيئة: النتيجة مجهولة (لا رفض)، فتُعرض تنبيهاً يحتاج مراجعة لا فشلاً قابلاً للإعادة
                session.Status = ComplianceUnknownStatus;
                session.ErrorMessage = string.Format(Messages.ZatcaComplianceCsidUnknown, $"{ex.GetType().Name}: {ex.Message}");
                await _db.SaveChangesAsync(CancellationToken.None);
                throw new ConflictException(session.ErrorMessage);
            }

            if (!compliance.Success)
            {
                // رفض صريح من الهيئة يختلف عن انقطاع أو انتهاء مهلة بلا رد (Reached = false): الثاني مصيره مجهول
                session.Status = compliance.Reached ? "compliance_csid_rejected" : ComplianceUnknownStatus;
                session.ErrorMessage = compliance.Reached ? compliance.Message : string.Format(Messages.ZatcaComplianceCsidUnknown, compliance.Message);
                if (compliance.Reached && (unit == null || !unit.IsActive))
                    config.ComplianceStatus = ComplianceStatus.NotEnrolled;
                await _db.SaveChangesAsync(CancellationToken.None);
                if (!compliance.Reached) throw new ConflictException(session.ErrorMessage!);
                throw new ValidationFailedException(string.Format(Messages.ZatcaComplianceCsidFailed, compliance.Message));
            }

            // الهيئة أصدرت الشهادة: الشهادة تُحفظ فوراً قبل أي عمل آخر. فشل معالجتها (تشفير، فك ترميز) يُسجَّل على
            // الجلسة بسببه الكامل ورقم طلب الهيئة بدل أن يضيع في خطأ 500 لا أثر له
            try
            {
                session.ComplianceRequestId = compliance.RequestId;
                session.ComplianceBinarySecurityToken = Protect(compliance.BinarySecurityToken!);
                session.ComplianceSecret = Protect(compliance.Secret!);
                session.CertificatePem = CertificateOf(compliance.BinarySecurityToken!);
                session.Status = "compliance_tests";
                await _db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                session.ComplianceBinarySecurityToken = null;
                session.ComplianceSecret = null;
                session.CertificatePem = null;
                session.Status = ComplianceUnsavedStatus;
                session.ErrorMessage = "ZATCA issued the Compliance CSID but it could not be stored: " + Diagnose(ex);
                await _db.SaveChangesAsync(CancellationToken.None);
                throw new ConflictException(string.Format(Messages.ZatcaComplianceCsidNotStored, ex.Message));
            }
        }

        if (!await _db.Set<ZatcaComplianceTest>().AnyAsync(t => t.SessionId == session.Id, ct))
        {
            // توليد مستندات الامتثال وتوقيعها محلياً: فشله يُحفظ بسببه على الجلسة، والمحاولة التالية تعيد التوليد
            // بالشهادة المحفوظة نفسها دون رمز تحقق جديد ودون طلب شهادة جديدة
            try { await GenerateComplianceTestsAsync(session, seller, ct); }
            catch (Exception ex) when (ex is not ErpException and not OperationCanceledException)
            {
                foreach (var entry in _db.ChangeTracker.Entries<ZatcaComplianceTest>().Where(e => e.State == EntityState.Added).ToList())
                    entry.State = EntityState.Detached;
                session.Status = "compliance_tests_failed";
                session.ErrorMessage = "Generating/signing the compliance test documents failed: " + Diagnose(ex);
                await _db.SaveChangesAsync(CancellationToken.None);
                throw new ConflictException(string.Format(Messages.ZatcaComplianceTestsGenerationFailed, ex.Message));
            }
        }

        // A persisted "submitted" state left by a process interruption has no confirmed ZATCA verdict.
        var interrupted = await _db.Set<ZatcaComplianceTest>()
            .Where(t => t.SessionId == session.Id && t.Status == "submitted").ToListAsync(ct);
        foreach (var test in interrupted)
        {
            test.Status = "processing_unknown";
            test.ResponseMessage = Messages.ZatcaComplianceTestInterrupted;
            test.CompletedAt = DateTime.UtcNow;
        }
        if (interrupted.Count > 0) await _db.SaveChangesAsync(ct);

        // Every test document is stored before submission; generated samples never enter invoice/ledger tables.
        foreach (var test in await _db.Set<ZatcaComplianceTest>()
                     .Where(t => t.SessionId == session.Id && t.Status == "generated").OrderBy(t => t.GeneratedAt).ToListAsync(ct))
            await SubmitComplianceTestAsync(session, test, baseUrl, ct);

        var tests = await _db.Set<ZatcaComplianceTest>().Where(t => t.SessionId == session.Id).ToListAsync(ct);
        if (tests.Count == 0 || tests.Any(t => t.Status != "accepted"))
        {
            session.Status = "tests_attention";
            // سبب الرفض من الهيئة (المحفوظ على الجلسة) يسبق الإرشاد العام، فيرى المستخدم ما رفضته الهيئة فعلاً
            var reasons = session.ErrorMessage;
            session.ErrorMessage = string.IsNullOrWhiteSpace(reasons) ? Messages.ZatcaComplianceTestsAttention
                : reasons.StartsWith(Messages.ZatcaComplianceTestsAttention, StringComparison.Ordinal) ? reasons
                : $"{Messages.ZatcaComplianceTestsAttention} {reasons}";
            if (session.ErrorMessage.Length > 4000) session.ErrorMessage = session.ErrorMessage[..4000];
            await _db.SaveChangesAsync(ct);
            throw new ConflictException(session.ErrorMessage);
        }

        await IssueProductionCsidAsync(session, unit, target, config, tenant, baseUrl, ct);
        return Mapper.Map<TenantDto>(tenant);
    }

    public async Task<ZatcaOnboardingStatusDto?> GetOnboardingStatusAsync(Guid? userId = null, CancellationToken ct = default)
    {
        var me = _user.UserId ?? throw new UnauthorizedAppException();
        var targetId = userId ?? me;
        if (targetId != me && !IsAdmin) throw new ForbiddenException();
        var session = await _db.Set<ZatcaOnboardingSession>()
            .Where(s => s.UserId == targetId).OrderByDescending(s => s.StartedAt).FirstOrDefaultAsync(ct);
        if (session == null) return null;
        // طلب «جارٍ» تجاوز المهلة يعني أن العملية توقفت قبل حفظ مصيره: يظهر مجهولاً لا جارياً إلى الأبد
        await ExpireStaleRequestAsync(session);
        return await ToStatusAsync(session, ct);
    }

    // ---------------- تشخيص ومعالجة الجلسات العالقة (للمالك ومدير النظام) ----------------

    public async Task<ZatcaConnectivityReportDto> CheckConnectivityAsync(ZatcaEnvironment? environment = null, CancellationToken ct = default)
    {
        if (!IsAdmin) throw new ForbiddenException();
        var config = (await _db.Set<Tenant>().AsNoTracking().FirstAsync(ct)).ZatcaConfig;
        var env = environment ?? config.Environment;
        var baseUrl = BaseUrl(config, env);
        var uri = new Uri(baseUrl);

        var report = new ZatcaConnectivityReportDto
        {
            Environment = env,
            Host = uri.Host,
            EndpointUrl = $"{baseUrl}/compliance",
            HttpClientTimeoutSeconds = (int)ZatcaApiClient.ClientTimeout.TotalSeconds,
            ProxyConfigured = new[] { "HTTPS_PROXY", "HTTP_PROXY", "ALL_PROXY" }
                .Any(v => !string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable(v))),
            CheckedAt = DateTime.UtcNow,
        };
        // كل خطوة مستقلة فيظهر موضع الانقطاع بدقة (اسم، منفذ، TLS، أو رد HTTP)
        report.Steps.Add(await ZatcaConnectivityProbe.DnsAsync(uri.Host, ct));
        report.Steps.Add(await ZatcaConnectivityProbe.TcpAsync(uri.Host, uri.Port, ct));
        report.Steps.Add(await ZatcaConnectivityProbe.TlsAsync(uri.Host, uri.Port, ct));

        var probe = await _api.ProbeAsync(baseUrl, ct);
        report.Steps.Add(new ZatcaConnectivityStepDto
        {
            Name = "https",
            Success = probe.Reached,
            ElapsedMs = probe.ElapsedMs,
            StatusCode = probe.HttpStatus,
            Detail = probe.Detail,
            ExceptionType = probe.ExceptionType,
            ExceptionChain = probe.ExceptionChain?.ToList() ?? new List<string>(),
        });
        report.Reachable = probe.Reached;
        return report;
    }

    public async Task<List<ZatcaStuckSessionDto>> ListStuckSessionsAsync(CancellationToken ct = default)
    {
        if (!IsAdmin) throw new ForbiddenException();
        var sessions = await _db.Set<ZatcaOnboardingSession>()
            .Where(s => StuckStatuses.Contains(s.Status)).OrderByDescending(s => s.StartedAt).ToListAsync(ct);
        foreach (var s in sessions) await ExpireStaleRequestAsync(s);
        return sessions.Select(s =>
        {
            var blocked = RecoveryBlockedReason(s);
            return new ZatcaStuckSessionDto
            {
                Id = s.Id,
                UserId = s.UserId,
                UserName = s.UserName,
                Environment = s.Environment,
                Status = s.Status,
                ErrorMessage = s.ErrorMessage,
                StartedAt = s.StartedAt,
                LastUpdatedAt = s.UpdatedAt,
                HasStoredCsid = HasStoredCsid(s),
                CanRecover = blocked == null,
                RequiresPortalConfirmation = s.Environment != ZatcaEnvironment.Sandbox,
                RecoveryBlockedReason = blocked,
            };
        }).ToList();
    }

    public async Task<ZatcaOnboardingStatusDto> RecoverStuckSessionAsync(Guid sessionId, ZatcaSessionRecoveryRequestDto request, CancellationToken ct = default)
    {
        if (!IsAdmin) throw new ForbiddenException();
        var ownerId = await _db.Set<ZatcaOnboardingSession>().Where(s => s.Id == sessionId).Select(s => (Guid?)s.UserId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.ZatcaUserNotFound);
        // البوابة نفسها التي يمر بها الربط: لا تُغلق جلسة بينما محاولة ربط لصاحبها جارية في هذه العملية
        var gate = OnboardingGates.GetOrAdd(ownerId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try { return await RecoverStuckSessionCoreAsync(sessionId, request, ct); }
        finally { gate.Release(); }
    }

    private async Task<ZatcaOnboardingStatusDto> RecoverStuckSessionCoreAsync(Guid sessionId, ZatcaSessionRecoveryRequestDto request, CancellationToken ct)
    {
        var session = await _db.Set<ZatcaOnboardingSession>().FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new NotFoundException(Messages.ZatcaUserNotFound);
        await ExpireStaleRequestAsync(session);

        var blocked = RecoveryBlockedReason(session);
        if (blocked != null) throw new ConflictException(blocked);

        // واجهة «فاتورة» لا توفر استعلاماً عن مصير طلب شهادة. التحقق إذن: (1) النظام لا يحفظ أي شهادة أو رقم طلب لهذه
        // الجلسة (فُحص أعلاه)، (2) في المحاكاة والإنتاج إقرار المسؤول بأنه راجع وحدات EGS في بوابة فاتورة ولم يجد شهادة
        // لهذا الطلب؛ بوابة المطورين لا تسجل شهادات الامتثال على المكلف، فالشهادة اليتيمة فيها بلا أثر.
        var sandbox = session.Environment == ZatcaEnvironment.Sandbox;
        if (!sandbox && !request.ConfirmNoCsidIssued)
            throw new ValidationFailedException(Messages.ZatcaRecoveryConfirmRequired);

        var note = request.Note?.Trim();
        if (note is { Length: > 500 }) note = note[..500];
        var previous = session.Status;
        session.Status = AbandonedStatus;
        session.CompletedAt = DateTime.UtcNow;
        var cause = previous == ComplianceUnsavedStatus ? $" Cause: {session.ErrorMessage}" : string.Empty;
        session.ErrorMessage = $"Closed by {_user.Name ?? "admin"} at {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC from '{previous}'. "
            + (previous == ComplianceUnsavedStatus
                ? $"ZATCA issued Compliance CSID request {session.ComplianceRequestId} but it was never stored; a compliance CSID only runs compliance checks and cannot report invoices"
                : "No CSID was stored for this session")
            + (sandbox ? " (developer portal: compliance CSIDs are not registered to the taxpayer)." : "; the admin reviewed the EGS units in the Fatoora portal.")
            + (string.IsNullOrEmpty(note) ? string.Empty : $" Note: {note}") + cause;
        if (session.ErrorMessage.Length > 4000) session.ErrorMessage = session.ErrorMessage[..4000];

        // حالة الامتثال في المنشأة تعود «غير مسجلة» إن لم يبق ربط فعّال ولا محاولة أخرى مفتوحة
        var tenant = await _db.Set<Tenant>().FirstAsync(ct);
        if (tenant.ZatcaConfig.ComplianceStatus == ComplianceStatus.InProgress
            && !await _db.Set<ZatcaUnit>().AnyAsync(u => u.IsActive, ct)
            && !await _db.Set<ZatcaOnboardingSession>().AnyAsync(s => s.Id != session.Id && s.Status != "completed" && s.Status != AbandonedStatus
                && s.Status != "compliance_csid_rejected" && s.Status != "production_rejected", ct))
            tenant.ZatcaConfig.ComplianceStatus = ComplianceStatus.NotEnrolled;

        await _db.SaveChangesAsync(CancellationToken.None);
        await _audit.LogAsync("ZATCA_SESSION_RECOVERED", nameof(ZatcaOnboardingSession), session.Id.ToString(),
            $"إغلاق جلسة ربط عالقة للمستخدم {session.UserName} ({session.Environment}) من الحالة {previous}"
            + (sandbox ? "" : " بعد إقرار المسؤول بعدم إصدار شهادة") + (string.IsNullOrEmpty(note) ? "" : $" - {note}"), CancellationToken.None);
        return await ToStatusAsync(session, ct);
    }

    /// <summary>سبب منع إغلاق الجلسة، أو null إن كان إغلاقها آمناً.</summary>
    private static string? RecoveryBlockedReason(ZatcaOnboardingSession s) => s.Status switch
    {
        ComplianceRequestingStatus => string.Format(Messages.ZatcaRecoveryStillRequesting, RequestStaleAfter.TotalMinutes.ToString("0", CultureInfo.InvariantCulture)),
        ProductionRequestingStatus or ProductionUnknownStatus => Messages.ZatcaRecoveryProductionStage,
        ComplianceUnknownStatus when HasStoredCsid(s) => Messages.ZatcaRecoveryHasCsid,
        ComplianceUnknownStatus => null,
        // شهادة امتثال صدرت ولم تُحفظ: لا تصلح إلا لفحوص الامتثال ولا تُبلّغ بها فواتير، فإغلاق الجلسة آمن
        ComplianceUnsavedStatus => null,
        _ => string.Format(Messages.ZatcaRecoveryNotStuck, s.Status),
    };

    /// <summary>
    /// سبب فشل محلي قابل للتشخيص بلا سجلات الاستضافة: نوع الاستثناء ورسالته وأسبابه الداخلية وأول أسطر موضعه.
    /// لا يحوي رموز تحقق ولا أسراراً ولا مفاتيح (رسائل .NET لا تضمّن قيم المدخلات).
    /// </summary>
    private static string Diagnose(Exception ex)
    {
        var root = ex;
        while (root.InnerException != null) root = root.InnerException;
        var frames = (root.StackTrace ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(4);
        var text = string.Join(" <- ", ZatcaApiClient.ExceptionChain(ex)) + " | at: " + string.Join(" / ", frames);
        return text.Length > 3000 ? text[..3000] : text;
    }

    private static bool HasStoredCsid(ZatcaOnboardingSession s)
        => s.ComplianceRequestId != null || s.ComplianceBinarySecurityToken != null || s.ProductionRequestId != null;

    /// <summary>
    /// طلب «جارٍ» أقدم من المهلة لم يعد جارياً حتماً (العميل يقطع بعد <see cref="ZatcaApiClient.ClientTimeout"/>، ومصيره
    /// يُحفظ دائماً)، فتوقف العملية قبل الحفظ هو التفسير الوحيد: يُسجَّل مجهولاً ليظهر للمسؤول ويُعالج.
    /// </summary>
    private async Task<bool> ExpireStaleRequestAsync(ZatcaOnboardingSession session)
    {
        if (session.Status is not (ComplianceRequestingStatus or ProductionRequestingStatus)) return false;
        if ((session.UpdatedAt ?? session.StartedAt) > DateTime.UtcNow - RequestStaleAfter) return false;
        var compliance = session.Status == ComplianceRequestingStatus;
        session.Status = compliance ? ComplianceUnknownStatus : ProductionUnknownStatus;
        session.ErrorMessage = compliance ? Messages.ZatcaComplianceRequestInterrupted : Messages.ZatcaProductionRequestInterrupted;
        await _db.SaveChangesAsync(CancellationToken.None);
        return true;
    }

    /// <summary>يرفض قبل إرسال طلب الشهادة إن تعذّر الوصول إلى الهيئة، فلا يُستهلك رمز التحقق ولا تُعلَّق الجلسة.</summary>
    private async Task EnsureReachableAsync(string baseUrl, CancellationToken ct)
    {
        var probe = await _api.ProbeAsync(baseUrl, ct);
        if (probe.Reached) return;
        throw new ValidationFailedException(string.Format(Messages.ZatcaUnreachable, new Uri(baseUrl).Host, probe.Detail));
    }

    public async Task<ZatcaOnboardingStatusDto> RetryComplianceTestAsync(Guid sessionId, Guid testId, CancellationToken ct = default)
    {
        var ownerId = await _db.Set<ZatcaOnboardingSession>().Where(s => s.Id == sessionId).Select(s => (Guid?)s.UserId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(Messages.ZatcaUserNotFound);
        var gate = OnboardingGates.GetOrAdd(ownerId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try { return await RetryComplianceTestCoreAsync(sessionId, testId, ct); }
        finally { gate.Release(); }
    }

    private async Task<ZatcaOnboardingStatusDto> RetryComplianceTestCoreAsync(Guid sessionId, Guid testId, CancellationToken ct)
    {
        var me = _user.UserId ?? throw new UnauthorizedAppException();
        var session = await _db.Set<ZatcaOnboardingSession>().FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new NotFoundException(Messages.ZatcaUserNotFound);
        if (session.UserId != me && !IsAdmin) throw new ForbiddenException();
        var test = await _db.Set<ZatcaComplianceTest>()
            .FirstOrDefaultAsync(t => t.Id == testId && t.SessionId == sessionId, ct)
            ?? throw new NotFoundException(Messages.ZatcaUserNotFound);
        if (test.Status != "rejected") throw new ConflictException(Messages.ZatcaComplianceRetryRejectedOnly);

        var tenant = await _db.Set<Tenant>().FirstAsync(ct);
        var config = tenant.ZatcaConfig;
        await SubmitComplianceTestAsync(session, test, BaseUrl(config, session.Environment), ct);
        var tests = await _db.Set<ZatcaComplianceTest>().Where(t => t.SessionId == session.Id).ToListAsync(ct);
        if (tests.Count > 0 && tests.All(t => t.Status == "accepted"))
        {
            var unit = await _db.Set<ZatcaUnit>().FirstOrDefaultAsync(u => u.UserId == session.UserId, ct);
            var target = await _db.Set<User>().AsNoTracking().FirstAsync(u => u.Id == session.UserId, ct);
            await IssueProductionCsidAsync(session, unit, target, config, tenant, BaseUrl(config, session.Environment), ct);
        }
        else
        {
            session.Status = "tests_attention";
            await _db.SaveChangesAsync(ct);
        }
        return await ToStatusAsync(session, ct);
    }

    public async Task<List<ZatcaUnitDto>> ListUnitsAsync(CancellationToken ct = default)
    {
        var me = _user.UserId ?? throw new UnauthorizedAppException();
        var all = IsAdmin;
        var units = await _db.Set<ZatcaUnit>().AsNoTracking().Where(u => all || u.UserId == me).OrderBy(u => u.UserName).ToListAsync(ct);
        return units.Select(u =>
        {
            var dto = Mapper.Map<ZatcaUnitDto>(u);
            dto.IsMine = u.UserId == me;
            return dto;
        }).ToList();
    }

    public async Task DisableUnitAsync(Guid unitId, CancellationToken ct = default)
    {
        var me = _user.UserId ?? throw new UnauthorizedAppException();
        var unit = await _db.Set<ZatcaUnit>().FirstOrDefaultAsync(u => u.Id == unitId, ct) ?? throw new NotFoundException(Messages.ZatcaUserNotFound);
        if (unit.UserId != me && !IsAdmin) throw new ForbiddenException();
        unit.IsActive = false;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("ZATCA_UNIT_DISABLED", nameof(ZatcaUnit), unit.Id.ToString(), $"إيقاف ربط المستخدم {unit.UserName} مع هيئة الزكاة", ct);
    }

    // ---------------- إرسال الفاتورة ----------------

    /// <summary>
    /// الإرسال كما هو (الفحوص، القفل، السلسلة، إعادة المحاولة اليدوية)، ثم تسجيل المحاولة بنتيجتها الفعلية في سجل المحاولات.
    /// الفاتورة المقبولة سابقاً لا تُعدّ محاولة. فشل التسجيل لا يغيّر نتيجة الإرسال.
    /// </summary>
    public async Task<ZatcaSubmitResultDto> SubmitInvoiceAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        _attempt = null;
        try
        {
            return await SubmitInvoiceCoreAsync(invoiceId, ct);
        }
        finally
        {
            var attempt = _attempt;
            _attempt = null;
            if (attempt != null && _monitoring != null)
            {
                attempt.DurationMs = (int)Math.Min(int.MaxValue, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                await _monitoring.RecordZatcaAttemptAsync(attempt, CancellationToken.None);
            }
        }
    }

    private async Task<ZatcaSubmitResultDto> SubmitInvoiceCoreAsync(Guid invoiceId, CancellationToken ct)
    {
        var invoice = await _db.Set<InvoiceBase>().FirstOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new NotFoundException(Messages.InvoiceNotFound);
        await _db.Entry(invoice).Collection(nameof(InvoiceBase<InvoiceItem>.Items)).LoadAsync(ct); // بنود جدول المستند (مبيعات/مرتجع/معارض)
        if (invoice.Status != "posted") throw new ConflictException(Messages.OnlyPostedInvoicesSubmitted);
        if (invoice.Kind is InvoiceKind.Purchase or InvoiceKind.PurchaseReturn) throw new ConflictException(Messages.PurchaseInvoicesNotSubmittedToZatca);
        // هامش ربح السيارات المستعملة المؤهلة: علّقت الهيئة إلزامه بالمرحلة الثانية (مايو 2023) ولا تصنيف له في مواصفاتها
        if (invoice.Lines.Any(l => l.VatCategory == VatCategory.MarginScheme)) throw new ConflictException(Messages.MarginSchemeNotSubmittedToZatca);
        // فاتورة قبلتها الهيئة لا تُرسل مرتين
        if (invoice.ZatcaStatus is ZatcaSubmissionStatus.Cleared or ZatcaSubmissionStatus.Reported or ZatcaSubmissionStatus.Warning)
            return Result(invoice, success: true, Messages.ZatcaAlreadySubmitted);

        // من هنا فصاعداً محاولة إرسال فعلية؛ الافتراضي «رُفضت قبل الإرسال» حتى يتقدّم الإرسال
        _attempt = new ZatcaSubmissionAttempt
        {
            InvoiceId = invoice.Id, InvoiceNumber = invoice.InvoiceNumber, DocumentKind = invoice.Kind.ToString(), DocumentUuid = invoice.Uuid,
            UserId = _user.UserId, UserName = _user.Name, AttemptedAt = DateTime.UtcNow, Outcome = ZatcaAttemptOutcome.PreCheckFailed,
            IsResubmission = invoice.ZatcaStatus == ZatcaSubmissionStatus.Rejected,
        };

        var tenant = await _db.Set<Tenant>().AsNoTracking().FirstAsync(ct);
        var config = tenant.ZatcaConfig;
        if (!config.IsEnabled) return await FailAsync(invoice, Messages.ZatcaNotEnabled, ct);

        // الفاتورة تُرسل بوحدة المستخدم الذي يرسلها: مربوطة، مفعّلة، على البيئة الحالية، وشهادتها سارية
        var senderId = _user.UserId;
        var units = await _db.Set<ZatcaUnit>().AsNoTracking().Where(u => u.UserId == senderId)
            .Select(u => new { u.Id, u.IsActive, u.Environment, u.CertificatePem }).ToListAsync(ct);
        var unit = units.FirstOrDefault(u => u.IsActive);
        if (unit != null && _attempt != null) _attempt.UnitId = unit.Id;
        if (unit == null)
            return await PreCheckFailAsync(invoice, new[] { units.Count == 0 ? "NoActiveUnit" : "UnitDisabled" }, ct,
                string.Format(Messages.ZatcaUserNotLinked, _user.Name ?? _user.Email));
        if (string.IsNullOrWhiteSpace(config.CustomEndpointUrl) && unit.Environment != config.Environment)
            return await PreCheckFailAsync(invoice, new[] { "UnitEnvMismatch" }, ct);
        if (CertificateExpiry(unit.CertificatePem) is { } expiry && expiry < DateTime.UtcNow)
            return await PreCheckFailAsync(invoice, new[] { "CertificateExpired" }, ct);

        // بيانات الفاتورة التي ترفضها الهيئة حتماً: تُصحَّح قبل الإرسال
        var simplifiedInvoice = await IsSimplifiedAsync(invoice, ct);
        var dataProblems = DataChecks(invoice, tenant, simplifiedInvoice);
        if (dataProblems.Count > 0) return await PreCheckFailAsync(invoice, dataProblems, ct);

        // المبسطة تُبلَّغ خلال 24 ساعة من إصدارها: التأخير لا يمنع الإرسال لكنه يُنبَّه عليه
        var overdue = simplifiedInvoice && IssuedAtUtc(invoice) < DateTime.UtcNow.AddHours(-24);

        // قفل السلسلة: إرسال واحد لكل وحدة؛ انتظار طويل يعني إرسالاً عالقاً فيُطلب إعادة المحاولة بدل الانتظار
        var gate = Gates.GetOrAdd(unit.Id, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(ChainWait, ct)) return await PreCheckFailAsync(invoice, new[] { "ChainBusy" }, ct);
        try
        {
            var unitId = unit.Id;
            var chain = await _db.Set<ZatcaUnit>().FirstAsync(u => u.Id == unitId, ct); // تُقرأ داخل القفل: العدّاد كما تركه الإرسال السابق
            // إعادة إرسال مرفوضة (بعد تصحيح إعدادات المنشأة أو شهادة الوحدة مثلاً): مستند جديد لدى الهيئة بمعرّف جديد،
            // فالهيئة لا تقبل UUID مكرراً؛ ومحاولتها المرفوضة تبقى حلقة في السلسلة
            if (invoice.ZatcaStatus == ZatcaSubmissionStatus.Rejected) invoice.Uuid = Guid.NewGuid();
            if (_attempt != null) { _attempt.DocumentUuid = invoice.Uuid; _attempt.Icv = chain.Counter + 1; }

            ZatcaDocument document;
            ZatcaSignedInvoice signed;
            ZatcaApiResult response;
            try
            {
                document = await DocumentAsync(invoice, tenant, chain.Counter + 1, chain.LastHash ?? ZatcaInvoiceXml.FirstPreviousHash, ct);
                // ZatcaKeys لا ImportFromPem: استيراد PKCS#8 في CNG يفشل تحت IIS بلا ملف تعريف مستخدم
                using (var key = ZatcaKeys.ImportPrivateKey(Reveal(chain.PrivateKeyPem)))
                    signed = ZatcaSigner.Sign(document, chain.CertificatePem, key, DateTime.UtcNow);

                var submission = new ZatcaSubmission(signed.Hash, signed.Uuid, signed.Xml);
                var secret = Reveal(chain.SecretKey);
                var baseUrl = BaseUrl(config, chain.Environment); // الوحدة ترسل للبيئة التي صدرت شهادتها منها
                response = document.IsSimplified
                    ? await _api.ReportAsync(baseUrl, Reveal(chain.BinarySecurityToken), secret, submission, ct)
                    : await _api.ClearAsync(baseUrl, Reveal(chain.BinarySecurityToken), secret, submission, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // تعذّر تجهيز الفاتورة أو توقيعها أو إرسالها قبل أي حكم (بيانات ناقصة، شهادة تالفة…): السلسلة لم تتغيّر،
                // ويُحفظ السبب على الفاتورة ليظهر للمستخدم بدل رسالة عامة
                if (_attempt != null) _attempt.Outcome = ZatcaAttemptOutcome.LocalError;
                return await FailAsync(invoice, string.Format(Messages.ZatcaSubmitFailed, ex.Message), ct);
            }

            // لا حكم من الهيئة (مصادقة، رابط، ضغط، خادم، انقطاع): السلسلة لا تتقدّم، ويُعاد الإرسال لاحقاً بالعدّاد نفسه.
            // الحالة وطريقة معالجتها من طبقة HTTP في القاموس (انقطاع الاتصال بلا رمز = الخدمة غير متاحة)
            if (!response.Reached)
            {
                var status = response.HttpStatus == 0 ? 503 : response.HttpStatus;
                if (_attempt != null) { _attempt.Outcome = ZatcaAttemptOutcome.Unreachable; _attempt.HttpStatus = status; }
                var http = ZatcaErrorDictionary.ResolveHttp(status);
                var failed = await FailAsync(invoice, $"{Explain(http)}\n{string.Format(Messages.ZatcaUnreachable, response.Message)}", ct,
                    new List<ZatcaIssueDto> { IssueDto($"HTTP {status}", http with { Chain = ZatcaChainEffect.Keep }, response.Message) });
                return failed;
            }

            // الفاتورة المرفوضة تبقى حلقة في السلسلة: عدّادها وتجزئتها يُحتسبان للفاتورة التالية
            chain.Counter = document.Counter;
            chain.LastHash = signed.Hash;
            chain.Version++;

            invoice.ZatcaHash = signed.Hash;
            invoice.ZatcaPih = document.PreviousHash;
            // الفاتورة الضريبية المعتمدة تعود مختومة من الهيئة: هي النسخة النظامية ورمز QR يؤخذ منها
            var cleared = response.Accepted && !string.IsNullOrEmpty(response.ClearedInvoice)
                ? Encoding.UTF8.GetString(Convert.FromBase64String(response.ClearedInvoice))
                : null;
            invoice.ZatcaUblXml = cleared ?? signed.Xml;
            if (response.Accepted) invoice.ZatcaQrCode = (cleared != null ? QrOf(cleared) : null) ?? signed.Qr;
            invoice.ZatcaStatus = !response.Accepted ? ZatcaSubmissionStatus.Rejected
                : response.Warnings.Count > 0 ? ZatcaSubmissionStatus.Warning
                : document.IsSimplified ? ZatcaSubmissionStatus.Reported : ZatcaSubmissionStatus.Cleared;
            // رسائل الهيئة مشروحة من القاموس: كل رمز يتبعه عنوان حالته وطريقة معالجتها
            var (errorLines, errorIssues) = ExplainMessages(response.Errors, warning: false);
            var (warningLines, warningIssues) = ExplainMessages(response.Warnings, warning: true);
            var notes = errorLines.Concat(warningLines).ToList();
            var issues = errorIssues.Concat(warningIssues).ToList();
            if (overdue)
            {
                notes.Add(Explain(ZatcaErrorDictionary.PreChecks["ReportingOverdue"]));
                issues.Add(IssueDto("ReportingOverdue", ZatcaErrorDictionary.PreChecks["ReportingOverdue"]));
            }
            invoice.ZatcaValidationMessages = notes.Count == 0 ? null : string.Join("\n", notes);
            await _db.SaveChangesAsync(ct);
            if (_attempt != null)
            {
                _attempt.Outcome = invoice.ZatcaStatus switch
                {
                    ZatcaSubmissionStatus.Rejected => ZatcaAttemptOutcome.Rejected,
                    ZatcaSubmissionStatus.Warning => ZatcaAttemptOutcome.AcceptedWithWarnings,
                    ZatcaSubmissionStatus.Reported => ZatcaAttemptOutcome.Reported,
                    _ => ZatcaAttemptOutcome.Cleared,
                };
                _attempt.HttpStatus = response.HttpStatus;
                _attempt.Icv = document.Counter;
                _attempt.IssueCodes = issues.Count == 0 ? null : string.Join(",", issues.Select(i => i.Code).Distinct());
                _attempt.Message = invoice.ZatcaValidationMessages;
            }

            await _audit.LogAsync(response.Accepted ? "ZATCA_SUBMITTED" : "ZATCA_REJECTED", nameof(Invoice), invoice.Id.ToString(),
                $"{invoice.InvoiceNumber}: {invoice.ZatcaStatus} (ICV {document.Counter} - {chain.UserName}, HTTP {response.HttpStatus})", ct);

            var submitted = Result(invoice, response.Accepted, !response.Accepted ? Messages.ZatcaRejected
                : response.Warnings.Count > 0 ? Messages.ZatcaAcceptedWithWarnings
                : document.IsSimplified ? Messages.ZatcaReported : Messages.ZatcaCleared);
            submitted.Issues.AddRange(issues);
            return submitted;
        }
        finally
        {
            gate.Release();
        }
    }

    // ---------------- داخلي ----------------

    private static ZatcaSubmitResultDto Result(InvoiceBase invoice, bool success, string message) => new()
    {
        Success = success, Status = invoice.ZatcaStatus, QrCode = invoice.ZatcaQrCode,
        Message = string.IsNullOrEmpty(invoice.ZatcaValidationMessages) || success && invoice.ZatcaStatus != ZatcaSubmissionStatus.Warning
            ? message
            : $"{message}\n{invoice.ZatcaValidationMessages}",
    };

    /// <summary>تعذّر الإرسال قبل حكم الهيئة: يُحفظ السبب على الفاتورة وتبقى حالتها كما هي (والسلسلة لم تُستهلك).</summary>
    private async Task<ZatcaSubmitResultDto> FailAsync(InvoiceBase invoice, string reason, CancellationToken ct, List<ZatcaIssueDto>? issues = null)
    {
        if (_attempt != null)
        {
            _attempt.Message = reason;
            _attempt.IssueCodes = issues is { Count: > 0 } ? string.Join(",", issues.Select(i => i.Code).Distinct()) : _attempt.IssueCodes;
        }
        invoice.ZatcaValidationMessages = reason;
        await _db.SaveChangesAsync(ct);
        return new ZatcaSubmitResultDto { Success = false, Status = invoice.ZatcaStatus, Message = reason, QrCode = invoice.ZatcaQrCode, Issues = issues ?? new() };
    }

    /// <summary>أقصى انتظار لقفل سلسلة الوحدة قبل إعادة المحاولة (ChainBusy).</summary>
    private static readonly TimeSpan ChainWait = TimeSpan.FromSeconds(30);

    /// <summary>انتهاء شهادة الوحدة (فارغ إن تعذّرت قراءتها؛ عندها تحكم الهيئة).</summary>
    private static DateTime? CertificateExpiry(string? pem)
    {
        if (string.IsNullOrWhiteSpace(pem)) return null;
        try
        {
            var text = pem.Contains("BEGIN CERTIFICATE", StringComparison.Ordinal) ? pem
                : $"-----BEGIN CERTIFICATE-----\n{pem.Trim()}\n-----END CERTIFICATE-----";
            using var cert = System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPem(text);
            return cert.NotAfter.ToUniversalTime();
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>وقت إصدار الفاتورة بالتوقيت العالمي (التاريخ والوقت محفوظان بتوقيت المملكة +3).</summary>
    private static DateTime IssuedAtUtc(InvoiceBase invoice)
        => invoice.IssueDate.Date.Add(TimeSpan.TryParse(invoice.IssueTime, CultureInfo.InvariantCulture, out var t) ? t : TimeSpan.Zero).AddHours(-3);

    /// <summary>المالك ومدير النظام يربطان غيرهما ويرون كل الوحدات.</summary>
    private bool IsAdmin => _user.RoleId is "owner" or "admin";

    /// <summary>عنوان واجهة الهيئة بحسب البيئة: بوابة المطورين (تجارب)، المحاكاة، أو الإنتاج.</summary>
    internal static string BaseUrl(ZatcaConfig c, ZatcaEnvironment? environment = null)
    {
        if (!string.IsNullOrWhiteSpace(c.CustomEndpointUrl)) return c.CustomEndpointUrl.Trim().TrimEnd('/');
        const string host = "https://gw-fatoora.zatca.gov.sa/e-invoicing";
        return (environment ?? c.Environment) switch
        {
            ZatcaEnvironment.Production => $"{host}/core",
            ZatcaEnvironment.Simulation => $"{host}/simulation",
            _ => $"{host}/developer-portal",
        };
    }

    private static ZatcaParty Seller(Tenant t)
        => new(t.NameAr, t.VatNumber, t.CrNumber, t.ZatcaConfig.Street, t.ZatcaConfig.BuildingNumber, t.ZatcaConfig.District, t.City, t.ZatcaConfig.PostalCode, "SA");

    private static string TypeCodeOf(InvoiceBase invoice)
        => invoice.InvoiceType == InvoiceType.DebitNote ? ZatcaInvoiceXml.DebitNote
            : invoice.Kind == InvoiceKind.SalesReturn || invoice.InvoiceType == InvoiceType.CreditNote ? ZatcaInvoiceXml.CreditNote
            : ZatcaInvoiceXml.Invoice;

    /// <summary>الإشعار يتبع نوع فاتورته الأصلية (مبسطة أو ضريبية)؛ وبلا أصل: وجود رقم ضريبي للمشتري يجعله ضريبياً.</summary>
    private async Task<bool> IsSimplifiedAsync(InvoiceBase invoice, CancellationToken ct) => invoice.InvoiceType switch
    {
        InvoiceType.Simplified => true,
        InvoiceType.TaxInvoice => false,
        _ => invoice.OriginalInvoiceId.HasValue
            ? await _db.Set<InvoiceBase>().AsNoTracking().Where(o => o.Id == invoice.OriginalInvoiceId).Select(o => (InvoiceType?)o.InvoiceType).FirstOrDefaultAsync(ct) != InvoiceType.TaxInvoice
            : string.IsNullOrWhiteSpace(invoice.PartyVatNumber),
    };

    // ---------------- قاموس الأخطاء: الفحوص المحلية وشرح ردود الهيئة ----------------

    /// <summary>
    /// فحوص البيانات قبل استدعاء الهيئة (طبقة PreChecks في <see cref="ZatcaErrorDictionary"/>): ما يُرفض حتماً لا يُرسل،
    /// فلا يُستهلك ICV. تُجمع كلها ليُصحّحها المستخدم مرة واحدة.
    /// </summary>
    private static List<string> DataChecks(InvoiceBase invoice, Tenant tenant, bool simplified)
    {
        var keys = new List<string>();
        var z = tenant.ZatcaConfig;
        if (!SaudiVat.IsValid(tenant.VatNumber)) keys.Add("SellerVatInvalid");
        if (string.IsNullOrWhiteSpace(z.Street) || !Digits(z.BuildingNumber, 4) || string.IsNullOrWhiteSpace(z.District)
            || string.IsNullOrWhiteSpace(tenant.City) || !Digits(z.PostalCode, 5))
            keys.Add("SellerAddressMissing");
        if (string.IsNullOrWhiteSpace(tenant.CrNumber)) keys.Add("SellerCrMissing");

        if (!simplified)
        {
            var domestic = string.IsNullOrWhiteSpace(invoice.PartyCountry) || invoice.PartyCountry.Trim().ToUpperInvariant() == "SA";
            if (string.IsNullOrWhiteSpace(invoice.PartyName)) keys.Add("BuyerNameMissing");
            if (domestic && !SaudiVat.IsValid(invoice.PartyVatNumber)) keys.Add("BuyerVatInvalid");
            if (string.IsNullOrWhiteSpace(invoice.PartyStreet) || string.IsNullOrWhiteSpace(invoice.PartyCity)
                || domestic && (!Digits(invoice.PartyBuildingNo, 4) || string.IsNullOrWhiteSpace(invoice.PartyDistrict) || !Digits(invoice.PartyPostalCode, 5)))
                keys.Add("BuyerAddressMissing");
        }

        if (!invoice.Lines.Any()) keys.Add("NoLines");
        else if (invoice.GrandTotal <= 0) keys.Add("ZeroOrNegativeTotal");
        if (invoice.Lines.Any(l => l.VatCategory is VatCategory.ZeroRated or VatCategory.Exempt or VatCategory.OutOfScope
                && VatExemptionReasons.Find(l.VatExemptionReasonCode) == null))
            keys.Add("ExemptionReasonMissing");

        if (TypeCodeOf(invoice) != ZatcaInvoiceXml.Invoice)
        {
            if (string.IsNullOrWhiteSpace(invoice.OriginalInvoiceNumber)) keys.Add("NoteWithoutReference");
            if (string.IsNullOrWhiteSpace(invoice.ReturnReason) && string.IsNullOrWhiteSpace(invoice.Notes)) keys.Add("NoteWithoutReason");
        }
        return keys;
    }

    private static bool Digits(string? value, int length) => value?.Trim() is { } v && v.Length == length && v.All(char.IsAsciiDigit);

    /// <summary>حالة من القاموس كما تُعرض وتُحفظ: العنوان ثم طريقة المعالجة.</summary>
    private static string Explain(ZatcaIssue issue) => $"{issue.Title} — {issue.Fix}";

    private static ZatcaIssueDto IssueDto(string code, ZatcaIssue issue, string? message = null) => new()
    {
        Code = code, Title = issue.Title, Fix = issue.Fix, Message = message,
        Kind = issue.Kind.ToString().ToLowerInvariant(), ChainConsumed = issue.Chain == ZatcaChainEffect.Consumed,
    };

    /// <summary>فشل فحص محلي (لم يُرسل شيء للهيئة): السبب بعناوين القاموس وحلولها، والسلسلة على حالها.</summary>
    private Task<ZatcaSubmitResultDto> PreCheckFailAsync(InvoiceBase invoice, IReadOnlyCollection<string> keys, CancellationToken ct, string? detail = null)
    {
        var issues = keys.Select(k => IssueDto(k, ZatcaErrorDictionary.PreChecks[k], detail)).ToList();
        var text = string.Join("\n", keys.Select(k => Explain(ZatcaErrorDictionary.PreChecks[k])));
        return FailAsync(invoice, detail == null ? text : $"{text}\n{detail}", ct, issues);
    }

    /// <summary>
    /// رسائل التحقق من الهيئة («الرمز: النص») مشروحة من القاموس: كل رسالة يتبعها عنوان حالتها وطريقة معالجتها.
    /// </summary>
    private static (List<string> Lines, List<ZatcaIssueDto> Issues) ExplainMessages(IEnumerable<string> messages, bool warning)
    {
        var lines = new List<string>();
        var issues = new List<ZatcaIssueDto>();
        foreach (var m in messages)
        {
            var colon = m.IndexOf(':');
            var code = colon > 0 && !m[..colon].Contains(' ') ? m[..colon].Trim() : null;
            var issue = ZatcaErrorDictionary.Resolve(code, warning);
            lines.Add($"{m}\n  ↳ {Explain(issue)}");
            issues.Add(IssueDto(code ?? string.Empty, issue, m));
        }
        return (lines, issues);
    }

    private async Task<ZatcaDocument> DocumentAsync(InvoiceBase invoice, Tenant tenant, long counter, string previousHash, CancellationToken ct)
    {
        var typeCode = TypeCodeOf(invoice);
        var simplified = await IsSimplifiedAsync(invoice, ct);

        var lines = invoice.Lines.OrderBy(i => i.CreatedAt).ThenBy(i => i.Id).Select(i =>
        {
            var reason = VatExemptionReasons.Find(i.VatExemptionReasonCode);
            // سعر الوحدة صافٍ دائماً في PriceAmount (الفاتورة الشاملة يُستخرج صافيها)، والخصم يطابق صافي السطر المحفوظ
            var (unitPrice, allowance) = VatPricing.ZatcaLinePrice(invoice.PricingMethod, i.Quantity, i.UnitPrice, i.VatRate, i.TotalBeforeVat);
            return new ZatcaLine(i.ItemName, i.Quantity, unitPrice, allowance,
                i.TotalBeforeVat + i.ExciseTaxAmount, i.ExciseTaxAmount, i.VatRate, i.VatAmount, i.VatCategory, reason?.Code, reason?.NameAr);
        }).ToList();

        return new ZatcaDocument
        {
            Number = invoice.InvoiceNumber, Uuid = invoice.Uuid, IssueDate = invoice.IssueDate.Date,
            IssueTime = TimeSpan.TryParse(invoice.IssueTime, CultureInfo.InvariantCulture, out var time) ? time.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) : "00:00:00",
            TypeCode = typeCode, IsSimplified = simplified, Currency = invoice.CurrencyCode, ExchangeRate = invoice.ExchangeRate,
            Seller = Seller(tenant),
            Buyer = new ZatcaParty(invoice.PartyName, invoice.PartyVatNumber, invoice.PartyCrNumber, invoice.PartyStreet, invoice.PartyBuildingNo,
                invoice.PartyDistrict, invoice.PartyCity, invoice.PartyPostalCode, Or(invoice.PartyCountry, "SA"), invoice.PartyIdType, invoice.PartyIdNumber),
            Lines = lines, Counter = counter, PreviousHash = previousHash,
            BillingReference = invoice.OriginalInvoiceNumber, NoteReason = Or(invoice.ReturnReason, invoice.Notes ?? string.Empty),
            PaymentMeansCode = invoice.PaymentMethod switch
            {
                PaymentMethod.Credit => "30",
                PaymentMethod.BankCard => "48",
                PaymentMethod.BankTransfer => "42",
                _ => "10",
            },
        };
    }

    private ZatcaOnboardingSession CreateOnboardingSession(User target, ZatcaUnit? unit, ZatcaConfig config, Tenant tenant)
    {
        var deviceSerial = unit?.DeviceSerial ?? Guid.NewGuid().ToString();
        using var key = ZatcaCsr.NewKey();
        var csr = ZatcaCsr.Create(key, new ZatcaCsrInput(
            config.Environment,
            Or(config.CsrCommonName, $"EGS-{tenant.Code}") + "-" + target.Id.ToString("N")[..8],
            Or(config.OrganizationUnit, tenant.City),
            Or(config.OrganizationName, tenant.NameAr),
            Or(config.CountryCode, "SA"),
            $"1-{Or(config.SolutionName, "ERP")}|2-{Or(config.SolutionVersion, "1.0")}|3-{deviceSerial}",
            tenant.VatNumber,
            InvoiceTypes,
            $"{config.BuildingNumber} {config.Street}, {tenant.City}",
            Or(config.BusinessCategory, "Trading")));

        return new ZatcaOnboardingSession
        {
            UserId = target.Id,
            UserName = target.Name,
            Environment = config.Environment,
            DeviceSerial = deviceSerial,
            InvoiceTypes = InvoiceTypes,
            Csr = csr,
            PrivateKeyPem = Protect(key.ExportPkcs8PrivateKeyPem()),
            Status = "preparing",
            StartedAt = DateTime.UtcNow,
        };
    }

    private async Task GenerateComplianceTestsAsync(ZatcaOnboardingSession session, ZatcaParty seller, CancellationToken ct)
    {
        // ZatcaKeys لا ImportFromPem: استيراد PKCS#8 في CNG يفشل تحت IIS بلا ملف تعريف مستخدم (سبب فشل الربط على الاستضافة)
        using var key = ZatcaKeys.ImportPrivateKey(Reveal(session.PrivateKeyPem!));
        var cases = RequiredComplianceDocuments(session.InvoiceTypes);
        if (cases.Count == 0) throw new ValidationFailedException("The CSR does not declare any supported invoice type for compliance testing.");

        var previous = ZatcaInvoiceXml.FirstPreviousHash;
        long counter = 0;
        var certificate = session.CertificatePem ?? throw new InvalidOperationException("The Compliance CSID certificate was not saved.");
        foreach (var item in cases)
        {
            var signed = ZatcaSigner.Sign(
                ComplianceSample(seller, item.DocumentType, item.IsSimplified, ++counter, previous),
                certificate, key, DateTime.UtcNow);
            previous = signed.Hash;
            _db.Add(new ZatcaComplianceTest
            {
                SessionId = session.Id,
                DocumentKey = item.Key,
                IsSimplified = item.IsSimplified,
                DocumentType = item.DocumentType,
                Status = "generated",
                Uuid = signed.Uuid,
                InvoiceHash = signed.Hash,
                SignedXml = signed.Xml,
                GeneratedAt = DateTime.UtcNow,
            });
        }
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The CSR's T/S flags declare standard and simplified invoice capabilities. Each enabled capability is tested
    /// against the three ZATCA compliance document families; disabling a capability removes its entire test group.
    /// </summary>
    internal static List<(string Key, bool IsSimplified, string DocumentType)> RequiredComplianceDocuments(string invoiceTypes)
    {
        if (invoiceTypes.Length != 4 || invoiceTypes.Any(c => c is not ('0' or '1')))
            throw new ValidationFailedException("The CSR invoice-type capability must contain four binary flags.");

        var documents = new List<(string Key, bool IsSimplified, string DocumentType)>();
        var families = new[] { (Flag: invoiceTypes[0], Simplified: false, Prefix: "standard"), (Flag: invoiceTypes[1], Simplified: true, Prefix: "simplified") };
        foreach (var family in families.Where(x => x.Flag == '1'))
        {
            documents.Add(($"{family.Prefix}_invoice", family.Simplified, ZatcaInvoiceXml.Invoice));
            documents.Add(($"{family.Prefix}_credit_note", family.Simplified, ZatcaInvoiceXml.CreditNote));
            documents.Add(($"{family.Prefix}_debit_note", family.Simplified, ZatcaInvoiceXml.DebitNote));
        }
        return documents;
    }

    private async Task SubmitComplianceTestAsync(ZatcaOnboardingSession session, ZatcaComplianceTest test, string baseUrl, CancellationToken ct)
    {
        if (test.Status == "accepted") return;
        if (test.Status is "submitted" or "processing_unknown")
            throw new ConflictException(Messages.ZatcaComplianceTestUnresolved);
        if (test.Status is not ("generated" or "rejected"))
            throw new ConflictException(Messages.ZatcaComplianceTestNotSubmittable);

        test.Status = "submitted";
        test.SubmittedAt = DateTime.UtcNow;
        test.AttemptCount++;
        test.HttpStatus = null;
        test.ResponseMessage = null;
        test.ErrorsJson = null;
        session.Status = "compliance_tests";
        session.ErrorMessage = null;
        await _db.SaveChangesAsync(ct);

        ZatcaApiResult result;
        try
        {
            result = await _api.CheckComplianceAsync(
                baseUrl,
                Reveal(session.ComplianceBinarySecurityToken!),
                Reveal(session.ComplianceSecret!),
                new ZatcaSubmission(test.InvoiceHash, test.Uuid, test.SignedXml),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            test.Status = "processing_unknown";
            test.ResponseMessage = ex.Message;
            test.CompletedAt = DateTime.UtcNow;
            session.Status = "tests_attention";
            session.ErrorMessage = ex.Message;
            await _db.SaveChangesAsync(CancellationToken.None);
            return;
        }

        test.HttpStatus = result.HttpStatus == 0 ? null : result.HttpStatus;
        test.ResponseMessage = result.Message;
        test.ErrorsJson = JsonSerializer.Serialize(result.Errors);
        test.Status = !result.Reached ? "processing_unknown" : result.Accepted ? "accepted" : "rejected";
        test.CompletedAt = DateTime.UtcNow;
        if (test.Status != "accepted")
        {
            var reasons = result.Errors.Count > 0 ? result.Errors : new List<string> { result.Message ?? $"HTTP {result.HttpStatus}" };
            session.ErrorMessage = string.Join(" | ", reasons);
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task IssueProductionCsidAsync(ZatcaOnboardingSession session, ZatcaUnit? unit, User target, ZatcaConfig config, Tenant tenant, string baseUrl, CancellationToken ct)
    {
        await ExpireStaleRequestAsync(session);
        if (session.Status == ProductionRequestingStatus)
            throw new ConflictException(Messages.ZatcaProductionInProgress);
        if (session.Status == ProductionUnknownStatus)
            throw new ConflictException(Messages.ZatcaProductionUnresolved);

        await EnsureReachableAsync(baseUrl, ct);
        session.Status = ProductionRequestingStatus;
        session.ErrorMessage = null;
        await _db.SaveChangesAsync(CancellationToken.None);
        // كما في شهادة الامتثال: الطلب لا يُلغى بانقطاع العميل، ومصيره يُحفظ دائماً
        ZatcaCsidResult production;
        try
        {
            production = await _api.RequestProductionCsidAsync(
                baseUrl, session.ComplianceRequestId!, Reveal(session.ComplianceBinarySecurityToken!), Reveal(session.ComplianceSecret!), CancellationToken.None);
        }
        catch (Exception ex)
        {
            session.Status = ProductionUnknownStatus;
            session.ErrorMessage = string.Format(Messages.ZatcaProductionCsidUnknown, $"{ex.GetType().Name}: {ex.Message}");
            await _db.SaveChangesAsync(CancellationToken.None);
            throw new ConflictException(session.ErrorMessage);
        }
        if (!production.Success)
        {
            // رفض صريح من الهيئة، أو انقطاع/انتهاء مهلة بلا رد مؤكد (مصيره مجهول فلا يُعرض رفضاً)
            session.Status = production.Reached ? "production_rejected" : ProductionUnknownStatus;
            session.ErrorMessage = production.Reached ? production.Message : string.Format(Messages.ZatcaProductionCsidUnknown, production.Message);
            await _db.SaveChangesAsync(CancellationToken.None);
            if (!production.Reached) throw new ConflictException(session.ErrorMessage!);
            throw new ValidationFailedException(string.Format(Messages.ZatcaProductionCsidFailed, production.Message));
        }

        // الهيئة أصدرت شهادة الإنتاج: ما قد يفشل محلياً (تشفير، فك ترميز) يُحسب أولاً قبل تغيير أي شيء. فشله يُسجَّل
        // على الجلسة بسببه الكامل ورقم طلب الهيئة للمراجعة في بوابة فاتورة، ولا يُعاد تلقائياً
        string token, secret, certificatePem;
        try
        {
            token = Protect(production.BinarySecurityToken!);
            secret = Protect(production.Secret!);
            certificatePem = CertificateOf(production.BinarySecurityToken!);
        }
        catch (Exception ex)
        {
            session.ProductionRequestId = production.RequestId;
            session.Status = ProductionUnknownStatus;
            session.ErrorMessage = "ZATCA issued the Production CSID but it could not be stored: " + Diagnose(ex);
            await _db.SaveChangesAsync(CancellationToken.None);
            throw new ConflictException(string.Format(Messages.ZatcaProductionCsidNotStored, ex.Message));
        }

        if (unit == null)
            _db.Add(unit = new ZatcaUnit { UserId = target.Id, DeviceSerial = session.DeviceSerial });
        unit.UserName = target.Name;
        unit.Environment = session.Environment;
        unit.ComplianceRequestId = session.ComplianceRequestId;
        unit.Csid = production.RequestId;
        unit.BinarySecurityToken = token;
        unit.CertificatePem = certificatePem;
        unit.SecretKey = secret;
        unit.PrivateKeyPem = session.PrivateKeyPem!;
        unit.IsActive = true;
        unit.OnboardedAt = DateTime.UtcNow;

        session.ProductionRequestId = production.RequestId;
        session.Status = "completed";
        session.CompletedAt = DateTime.UtcNow;
        session.ErrorMessage = null;
        config.Csid = production.RequestId;
        config.Otp = null;
        config.Phase = "phase2";
        config.IsEnabled = true;
        config.ComplianceStatus = ComplianceStatus.Compliant;
        config.OnboardedAt = DateTime.UtcNow;
        // شهادة الإنتاج صدرت: حفظها لا يُلغى بانقطاع العميل وإلا ضاعت شهادة سارية لدى الهيئة
        await _db.SaveChangesAsync(CancellationToken.None);
        await _audit.LogAsync("ZATCA_ONBOARDED", nameof(ZatcaUnit), unit.Id.ToString(),
            $"ربط المستخدم {target.Name} مع هيئة الزكاة ({session.Environment}) - وحدة {session.DeviceSerial}", ct);
    }

    private async Task<ZatcaOnboardingStatusDto> ToStatusAsync(ZatcaOnboardingSession session, CancellationToken ct)
    {
        var tests = await _db.Set<ZatcaComplianceTest>().AsNoTracking()
            .Where(t => t.SessionId == session.Id).OrderBy(t => t.GeneratedAt).ToListAsync(ct);
        return new ZatcaOnboardingStatusDto
        {
            Id = session.Id,
            UserId = session.UserId,
            UserName = session.UserName,
            Environment = session.Environment,
            Status = session.Status,
            ComplianceRequestId = session.ComplianceRequestId,
            ProductionRequestId = session.ProductionRequestId,
            ErrorMessage = session.ErrorMessage,
            StartedAt = session.StartedAt,
            CompletedAt = session.CompletedAt,
            Tests = tests.Select(t => new ZatcaComplianceTestDto
            {
                Id = t.Id,
                DocumentKey = t.DocumentKey,
                Status = t.Status,
                Uuid = t.Uuid,
                AttemptCount = t.AttemptCount,
                HttpStatus = t.HttpStatus,
                ResponseMessage = t.ResponseMessage,
                Errors = string.IsNullOrWhiteSpace(t.ErrorsJson) ? new List<string>() : JsonSerializer.Deserialize<List<string>>(t.ErrorsJson) ?? new List<string>(),
                GeneratedAt = t.GeneratedAt,
                SubmittedAt = t.SubmittedAt,
                CompletedAt = t.CompletedAt,
            }).ToList(),
        };
    }

    internal static ZatcaDocument ComplianceSample(ZatcaParty seller, string type, bool simplified, long counter, string previousHash)
    {
        var now = SaudiTime.Now;
        return new ZatcaDocument
        {
            Number = $"CMP-{counter:0000}", Uuid = Guid.NewGuid(), IssueDate = now.Date, IssueTime = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            TypeCode = type, IsSimplified = simplified, Seller = seller,
            Buyer = new ZatcaParty("عميل فحص الامتثال", SampleBuyerVat, null, "شارع الفحص", "1234", "حي الفحص", "الرياض", "12345", "SA"),
            Counter = counter, PreviousHash = previousHash,
            BillingReference = type == ZatcaInvoiceXml.Invoice ? null : "CMP-0001", NoteReason = type == ZatcaInvoiceXml.Invoice ? null : "فحص الامتثال",
            Lines = new[] { new ZatcaLine("بند فحص الامتثال", 1, 100, 0, 100, 0, 15, 15, VatCategory.Standard, null, null) },
        };
    }

    /// <summary>رمز الأمان هو الشهادة (Base64) مرمَّزة Base64 مرة أخرى.</summary>
    internal static string CertificateOf(string binarySecurityToken) => Encoding.UTF8.GetString(Convert.FromBase64String(binarySecurityToken)).Trim();

    private static string? QrOf(string xml)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("cac", "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2");
            ns.AddNamespace("cbc", "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2");
            return doc.SelectSingleNode("//cac:AdditionalDocumentReference[cbc:ID='QR']/cac:Attachment/cbc:EmbeddedDocumentBinaryObject", ns)?.InnerText.Trim();
        }
        catch (XmlException)
        {
            return null;
        }
    }

    // المفتاح الخاص وسرّ الواجهة يُحفظان مشفّرين؛ قيمة أُدخلت يدوياً (بلا البادئة) تُقرأ كما هي
    private string Protect(string plain) => ProtectedPrefix + _secrets.Protect(plain);
    private string Reveal(string stored) => stored.StartsWith(ProtectedPrefix, StringComparison.Ordinal) ? _secrets.Unprotect(stored[ProtectedPrefix.Length..]) : stored;

    private static string Or(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static bool IsDigits(string? value, int length) => value?.Trim() is { } v && v.Length == length && v.All(char.IsAsciiDigit);
}
