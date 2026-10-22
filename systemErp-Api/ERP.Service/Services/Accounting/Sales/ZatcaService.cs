using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
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

    /// <summary>إرسال واحد في كل مرة للوحدة: العدّاد والتجزئة السابقة يؤخذان ويُحدَّثان بالتسلسل.</summary>
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Gates = new();

    private readonly ErpDbContext _db;
    private readonly IZatcaApiClient _api;
    private readonly ISecretProtector _secrets;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _user;

    public ZatcaService(ErpDbContext db, IZatcaApiClient api, ISecretProtector secrets, IAuditService audit, ICurrentUser user)
    {
        _db = db; _api = api; _secrets = secrets; _audit = audit; _user = user;
    }

    public string GenerateTlvQr(string sellerName, string vatNumber, DateTime timestamp, decimal total, decimal vat)
        => ZatcaTlv.Encode(sellerName, vatNumber, timestamp, total, vat);

    // ---------------- الربط ----------------

    public async Task<TenantDto> OnboardAsync(ZatcaOnboardRequestDto request, CancellationToken ct = default)
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

        var missing = new List<string>();
        if (otp.Length == 0) missing.Add(Messages.ZatcaFieldOtp);
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
        // إعادة ربط المستخدم نفسه تجدّد شهادة وحدته: رقمها التسلسلي وسلسلة فواتيرها يبقيان
        var deviceSerial = unit?.DeviceSerial ?? Guid.NewGuid().ToString();
        using var key = ZatcaCsr.NewKey();
        var csr = ZatcaCsr.Create(key, new ZatcaCsrInput(
            config.Environment,
            Or(config.CsrCommonName, $"EGS-{tenant.Code}") + "-" + targetId.ToString("N")[..8],
            Or(config.OrganizationUnit, tenant.City),
            Or(config.OrganizationName, tenant.NameAr),
            Or(config.CountryCode, "SA"),
            $"1-{Or(config.SolutionName, "ERP")}|2-{Or(config.SolutionVersion, "1.0")}|3-{deviceSerial}",
            tenant.VatNumber,
            InvoiceTypes,
            $"{config.BuildingNumber} {config.Street}, {tenant.City}",
            Or(config.BusinessCategory, "Trading")));

        // 1) شهادة الامتثال برمز التحقق
        var compliance = await _api.RequestComplianceCsidAsync(baseUrl, csr, otp, ct);
        if (!compliance.Success) throw new ValidationFailedException(string.Format(Messages.ZatcaComplianceCsidFailed, compliance.Message));
        var complianceCertificate = CertificateOf(compliance.BinarySecurityToken!);

        // 2) فحص الامتثال: فاتورة وإشعار دائن وإشعار مدين، مبسطة وضريبية (كل ما تغطيه الشهادة)
        var previous = ZatcaInvoiceXml.FirstPreviousHash;
        long counter = 0;
        foreach (var simplified in new[] { true, false })
            foreach (var type in new[] { ZatcaInvoiceXml.Invoice, ZatcaInvoiceXml.CreditNote, ZatcaInvoiceXml.DebitNote })
            {
                var signed = ZatcaSigner.Sign(ComplianceSample(seller, type, simplified, ++counter, previous), complianceCertificate, key, DateTime.UtcNow);
                previous = signed.Hash;
                var check = await _api.CheckComplianceAsync(baseUrl, compliance.BinarySecurityToken!, compliance.Secret!, new ZatcaSubmission(signed.Hash, signed.Uuid, signed.Xml), ct);
                if (!check.Accepted)
                {
                    var reasons = check.Errors.Count > 0 ? check.Errors : new List<string> { check.Message ?? $"HTTP {check.HttpStatus}" };
                    var message = string.Format(Messages.ZatcaComplianceCheckFailed, string.Join(" | ", reasons));
                    throw new ValidationFailedException(message, reasons.Prepend(message));
                }
            }

        // 3) شهادة الإنتاج
        var production = await _api.RequestProductionCsidAsync(baseUrl, compliance.RequestId!, compliance.BinarySecurityToken!, compliance.Secret!, ct);
        if (!production.Success) throw new ValidationFailedException(string.Format(Messages.ZatcaProductionCsidFailed, production.Message));

        if (unit == null) _db.Add(unit = new ZatcaUnit { UserId = targetId, DeviceSerial = deviceSerial });
        unit.UserName = target.Name;
        unit.Environment = config.Environment;
        unit.ComplianceRequestId = compliance.RequestId;
        unit.Csid = production.RequestId;
        unit.BinarySecurityToken = production.BinarySecurityToken!;
        unit.CertificatePem = CertificateOf(production.BinarySecurityToken!);
        unit.SecretKey = Protect(production.Secret!);
        unit.PrivateKeyPem = Protect(key.ExportPkcs8PrivateKeyPem());
        unit.IsActive = true;
        unit.OnboardedAt = DateTime.UtcNow;

        // حالة المنشأة: مفعَّلة ما دام فيها مستخدم مربوط؛ الأسرار على وحدة المستخدم لا على المنشأة
        config.Csid = production.RequestId;
        config.Otp = null; // رمز التحقق لمرة واحدة
        config.Phase = "phase2";
        config.IsEnabled = true;
        config.ComplianceStatus = ComplianceStatus.Compliant;
        config.OnboardedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync("ZATCA_ONBOARDED", nameof(ZatcaUnit), unit.Id.ToString(), $"ربط المستخدم {target.Name} مع هيئة الزكاة ({config.Environment}) - وحدة {deviceSerial}", ct);
        return Mapper.Map<TenantDto>(tenant);
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

    public async Task<ZatcaSubmitResultDto> SubmitInvoiceAsync(Guid invoiceId, CancellationToken ct = default)
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

        var tenant = await _db.Set<Tenant>().AsNoTracking().FirstAsync(ct);
        var config = tenant.ZatcaConfig;
        if (!config.IsEnabled) return await FailAsync(invoice, Messages.ZatcaNotEnabled, ct);

        // الفاتورة تُرسل بوحدة المستخدم الذي يرسلها: مربوطة، مفعّلة، على البيئة الحالية، وشهادتها سارية
        var senderId = _user.UserId;
        var units = await _db.Set<ZatcaUnit>().AsNoTracking().Where(u => u.UserId == senderId)
            .Select(u => new { u.Id, u.IsActive, u.Environment, u.CertificatePem }).ToListAsync(ct);
        var unit = units.FirstOrDefault(u => u.IsActive);
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

            ZatcaDocument document;
            ZatcaSignedInvoice signed;
            ZatcaApiResult response;
            try
            {
                document = await DocumentAsync(invoice, tenant, chain.Counter + 1, chain.LastHash ?? ZatcaInvoiceXml.FirstPreviousHash, ct);
                using (var key = ECDsa.Create())
                {
                    key.ImportFromPem(Reveal(chain.PrivateKeyPem));
                    signed = ZatcaSigner.Sign(document, chain.CertificatePem, key, DateTime.UtcNow);
                }

                var submission = new ZatcaSubmission(signed.Hash, signed.Uuid, signed.Xml);
                var secret = Reveal(chain.SecretKey);
                var baseUrl = BaseUrl(config, chain.Environment); // الوحدة ترسل للبيئة التي صدرت شهادتها منها
                response = document.IsSimplified
                    ? await _api.ReportAsync(baseUrl, chain.BinarySecurityToken, secret, submission, ct)
                    : await _api.ClearAsync(baseUrl, chain.BinarySecurityToken, secret, submission, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // تعذّر تجهيز الفاتورة أو توقيعها أو إرسالها قبل أي حكم (بيانات ناقصة، شهادة تالفة…): السلسلة لم تتغيّر،
                // ويُحفظ السبب على الفاتورة ليظهر للمستخدم بدل رسالة عامة
                return await FailAsync(invoice, string.Format(Messages.ZatcaSubmitFailed, ex.Message), ct);
            }

            // لا حكم من الهيئة (مصادقة، رابط، ضغط، خادم، انقطاع): السلسلة لا تتقدّم، ويُعاد الإرسال لاحقاً بالعدّاد نفسه.
            // الحالة وطريقة معالجتها من طبقة HTTP في القاموس (انقطاع الاتصال بلا رمز = الخدمة غير متاحة)
            if (!response.Reached)
            {
                var status = response.HttpStatus == 0 ? 503 : response.HttpStatus;
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
    private static string BaseUrl(ZatcaConfig c, ZatcaEnvironment? environment = null)
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
            return new ZatcaLine(i.ItemName, i.Quantity, i.UnitPrice,
                Math.Max(0, DocumentPricing.Round(i.Quantity * i.UnitPrice) - i.TotalBeforeVat),
                i.TotalBeforeVat, i.VatRate, i.VatAmount, i.VatCategory, reason?.Code, reason?.NameAr);
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

    private static ZatcaDocument ComplianceSample(ZatcaParty seller, string type, bool simplified, long counter, string previousHash)
    {
        var now = SaudiTime.Now;
        return new ZatcaDocument
        {
            Number = $"CMP-{counter:0000}", Uuid = Guid.NewGuid(), IssueDate = now.Date, IssueTime = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            TypeCode = type, IsSimplified = simplified, Seller = seller,
            Buyer = new ZatcaParty("عميل فحص الامتثال", SampleBuyerVat, null, "شارع الفحص", "1234", "حي الفحص", "الرياض", "12345", "SA"),
            Counter = counter, PreviousHash = previousHash,
            BillingReference = type == ZatcaInvoiceXml.Invoice ? null : "CMP-0001", NoteReason = type == ZatcaInvoiceXml.Invoice ? null : "فحص الامتثال",
            Lines = new[] { new ZatcaLine("بند فحص الامتثال", 1, 100, 0, 100, 15, 15, VatCategory.Standard, null, null) },
        };
    }

    /// <summary>رمز الأمان هو الشهادة (Base64) مرمَّزة Base64 مرة أخرى.</summary>
    private static string CertificateOf(string binarySecurityToken) => Encoding.UTF8.GetString(Convert.FromBase64String(binarySecurityToken)).Trim();

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
