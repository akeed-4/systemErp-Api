using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using ERP.Core.Contracts.Accounting;
using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Accounting.Sales;
using Microsoft.Extensions.Logging;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// تشخيص محلي مؤقت يعيد تنفيذ ما يحدث داخل الخادم بعد صدور شهادة الامتثال، بالمكوّنات نفسها التي يستخدمها مسار الربط
/// (<see cref="ZatcaCsr"/>، <see cref="ZatcaService.CertificateOf"/>، <see cref="ISecretProtector"/>،
/// <see cref="ZatcaService.ComplianceSample"/>، <see cref="ZatcaSigner"/>)، لكن بمفتاح تجريبي وشهادة محلية تصدرها جهة
/// إصدار تجريبية داخل الذاكرة وتحمل <see cref="TestMarker"/> — ليست شهادة من الهيئة ولا تُرسل لأحد.
/// <para>
/// العزل بنيوي: الصنف لا يستقبل قاعدة البيانات ولا عميل HTTP ولا عميل الهيئة، فلا يستطيع الكتابة في القاعدة ولا الاتصال
/// بالخارج. الملف المؤقت الوحيد (فحص صلاحية مجلد Temp) يُحذف في finally.
/// </para>
/// </summary>
public sealed class ZatcaLocalSelfTest : IZatcaLocalSelfTest
{
    /// <summary>علامة كل بيانات الاختبار (الشهادة وجهة إصدارها والبائع) حتى لا تُخلط بشهادة أو منشأة حقيقية.</summary>
    public const string TestMarker = "LOCAL DIAGNOSTIC - NOT ZATCA";
    /// <summary>رقم ضريبي تجريبي بصيغة صحيحة لا يخص منشأة.</summary>
    public const string TestVat = "399999999900003";

    public const string StagePrivateKey = "private_key";
    /// <summary>الإصلاح: تحميل المفتاح بـ <see cref="ZatcaKeys"/> (فك مُدار + ImportParameters) كما يفعل مسار الربط الآن.</summary>
    public const string StageKeyImport = "key_import";
    /// <summary>معلوماتية: الطريقة القديمة (ImportFromPem → استيراد PKCS#8 في CNG) لإثبات سبب الفشل على الاستضافة.</summary>
    public const string StageLegacyPkcs8Import = "legacy_pkcs8_import";
    public const string StageLocalCertificate = "local_certificate";
    public const string StageCertificateParsing = "certificate_parsing";
    public const string StageEncryption = "encryption";
    public const string StageComplianceXml = "compliance_xml";
    public const string StageXmlSignature = "xml_signature";
    public const string StageSignatureVerification = "signature_verification";
    public const string StageEnvironmentCrypto = "environment_crypto";

    /// <summary>تشغيل واحد في العملية كلها: الاختبار يستهلك المعالج، فلا يُسمح بتشغيلات متزامنة.</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(60);

    private readonly ISecretProtector _secrets;
    private readonly ILogger<ZatcaLocalSelfTest> _log;

    public ZatcaLocalSelfTest(ISecretProtector secrets, ILogger<ZatcaLocalSelfTest> log)
    {
        _secrets = secrets; _log = log;
    }

    public async Task<ZatcaSelfTestReportDto> RunAsync(CancellationToken ct = default)
    {
        if (!await Gate.WaitAsync(0, ct))
            throw new ConflictException("A ZATCA local self-test is already running. Try again when it finishes.");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(RunTimeout);
            // عمل حسابي متزامن: يُنفَّذ خارج خيط الطلب
            return await Task.Run(() => Run(timeout.Token), CancellationToken.None);
        }
        finally { Gate.Release(); }
    }

    private ZatcaSelfTestReportDto Run(CancellationToken ct)
    {
        var report = new ZatcaSelfTestReportDto { RunId = Guid.NewGuid(), StartedAt = DateTime.UtcNow };
        using var scope = _log.BeginScope(new Dictionary<string, object> { ["ZatcaSelfTestRunId"] = report.RunId });
        var total = Stopwatch.StartNew();
        report.Environment = DescribeEnvironment();
        _log.LogInformation("ZATCA local self-test {RunId} started. Runtime={Runtime} OS={Os} Arch={Arch} Culture={Culture} TimeZone={TimeZone}",
            report.RunId, report.Environment.GetValueOrDefault("dotnet.runtime"), report.Environment.GetValueOrDefault("os"),
            report.Environment.GetValueOrDefault("process.architecture"), report.Environment.GetValueOrDefault("culture"), report.Environment.GetValueOrDefault("timeZone"));

        // بيانات الاختبار كلها في الذاكرة وتُتلف في finally؛ نص المفتاح لا يُسجَّل ولا يُعاد
        ECDsa? key = null, signingKey = null, ca = null;
        X509Certificate2? certificate = null;
        string? keyPem = null, token = null, certificateText = null;
        List<(string Key, bool IsSimplified, string DocumentType)>? cases = null;
        var signed = new List<(string Key, bool IsSimplified, ZatcaSignedInvoice Invoice)>();
        var seller = new ZatcaParty($"{TestMarker} SELLER", TestVat, "1010010000", "شارع التشخيص", "1234", "حي التشخيص", "الرياض", "12345", "SA");
        var passed = new HashSet<string>();

        try
        {
            // 1) مفتاح الوحدة: secp256k1 كما في ZatcaCsr.NewKey، ثم تصديره PKCS#8 كما يُحفظ في الجلسة
            Stage(report, passed, StagePrivateKey, Array.Empty<string>(), ct, () =>
            {
                key = ZatcaCsr.NewKey();
                keyPem = key.ExportPkcs8PrivateKeyPem();
                var curve = key.ExportParameters(false).Curve;
                return $"{key.GetType().Name}, curve {curve.Oid?.FriendlyName ?? curve.Oid?.Value}, {key.KeySize} bits; PKCS#8 PEM export OK";
            });

            // 1-أ) الإصلاح: تحميله بـ ZatcaKeys (المستخدم الآن في توليد مستندات الامتثال وإرسال الفواتير)، ثم إثبات أنه المفتاح نفسه
            Stage(report, passed, StageKeyImport, new[] { StagePrivateKey }, ct, () =>
            {
                signingKey = ZatcaKeys.ImportPrivateKey(keyPem!);
                if (!signingKey.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(key!.ExportSubjectPublicKeyInfo()))
                    throw new CryptographicException("The imported key's public key differs from the generated key.");
                var probe = RandomNumberGenerator.GetBytes(32);
                var signature = signingKey.SignData(probe, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
                if (!key.VerifyData(probe, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
                    throw new CryptographicException("A signature made with the imported key does not verify with the generated key.");
                return $"ZatcaKeys (managed PKCS#8 decode + ImportParameters) → {signingKey.GetType().Name}; same public key; sign/verify OK";
            });

            // 1-ب) معلوماتية: الطريقة القديمة. فشلها هنا بـ «temporary profile» يثبت سبب فشل الربط، ولا يعني فشل المسار الحالي
            Stage(report, passed, StageLegacyPkcs8Import, new[] { StagePrivateKey }, ct, () =>
            {
                using var legacy = ECDsa.Create();
                legacy.ImportFromPem(keyPem!);
                return "ECDsa.ImportFromPem (PKCS#8 into CNG) works in this environment";
            }, informational: true);

            // 2) طلب الشهادة بـ ZatcaCsr.Create نفسه، وشهادة محلية من جهة إصدار تجريبية بصيغة رمز الهيئة (Base64 مزدوج)
            Stage(report, passed, StageLocalCertificate, new[] { StagePrivateKey }, ct, () =>
            {
                var csr = ZatcaCsr.Create(key!, new ZatcaCsrInput(ZatcaEnvironment.Sandbox, "LOCAL-DIAGNOSTIC-NOT-ZATCA", "Diagnostics",
                    "Local self-test (not a customer)", "SA", $"1-ERP|2-selftest|3-{Guid.NewGuid()}", TestVat, "1100", "1234 Diagnostic St, Riyadh", "Diagnostics"));
                var csrPem = Encoding.UTF8.GetString(Convert.FromBase64String(csr));
                var request = CertificateRequest.LoadSigningRequestPem(csrPem, HashAlgorithmName.SHA256, CertificateRequestLoadOptions.UnsafeLoadCertificateExtensions);
                ca = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                var serial = RandomNumberGenerator.GetBytes(8);
                serial[0] &= 0x7F;
                using var issued = request.Create(new X500DistinguishedName($"CN={TestMarker} CA, O=Local self-test"),
                    X509SignatureGenerator.CreateForECDsa(ca), DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1), serial);
                token = Convert.ToBase64String(Encoding.UTF8.GetBytes(Convert.ToBase64String(issued.RawData)));
                return $"CSR {csrPem.Length} chars with {request.CertificateExtensions.Count} extensions; local test CA issued a 1-hour certificate; token in ZATCA binarySecurityToken shape";
            });

            // 3) تحليلها بـ ZatcaService.CertificateOf نفسه ثم X509CertificateLoader كما في ZatcaSigner، والتحقق البنيوي
            Stage(report, passed, StageCertificateParsing, new[] { StageLocalCertificate }, ct, () =>
            {
                certificateText = ZatcaService.CertificateOf(token!);
                certificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(certificateText));
                using var publicKey = certificate.GetECDsaPublicKey() ?? throw new CryptographicException("The certificate carries no ECDSA public key.");
                if (!publicKey.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(key!.ExportSubjectPublicKeyInfo()))
                    throw new CryptographicException("The certificate public key does not match the private key.");
                var now = DateTime.UtcNow;
                if (now < certificate.NotBefore.ToUniversalTime() || now > certificate.NotAfter.ToUniversalTime())
                    throw new CryptographicException($"The certificate is outside its validity window (server UTC {now:O}).");
                if (!certificate.Issuer.Contains(TestMarker, StringComparison.Ordinal))
                    throw new InvalidOperationException("Refusing to continue: the certificate is not the local diagnostic certificate.");
                return $"Subject '{certificate.Subject}'; issuer '{certificate.Issuer}'; {certificate.SignatureAlgorithm.FriendlyName}; "
                    + $"{certificate.Extensions.Count} extensions; public key matches; valid {certificate.NotBefore.ToUniversalTime():u} → {certificate.NotAfter.ToUniversalTime():u}";
            });

            // 4) التشفير وفكّه بـ ISecretProtector (Data Protection) كما يُحفظ المفتاح والرمز والسر، ثم استيراد المفتاح المفكوك
            Stage(report, passed, StageEncryption, new[] { StagePrivateKey }, ct, () =>
            {
                var probe = "selftest-" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
                var protectedProbe = _secrets.Protect(probe);
                if (_secrets.Unprotect(protectedProbe) != probe) throw new CryptographicException("Protect/Unprotect round-trip mismatch for a secret string.");
                var protectedPem = _secrets.Protect(keyPem!);
                var revealed = _secrets.Unprotect(protectedPem);
                if (revealed != keyPem) throw new CryptographicException("Protect/Unprotect round-trip mismatch for the private key PEM.");
                using var reimported = ZatcaKeys.ImportPrivateKey(revealed);
                return $"Round-trip OK ({probe.Length}-char secret → {protectedProbe.Length} chars; key PEM → {protectedPem.Length} chars); key re-imported from the decrypted PEM";
            });

            // 5) مستندات الامتثال الست بـ RequiredComplianceDocuments و ComplianceSample نفسيهما (بائع تجريبي)
            Stage(report, passed, StageComplianceXml, Array.Empty<string>(), ct, () =>
            {
                cases = ZatcaService.RequiredComplianceDocuments("1100");
                var sizes = new List<string>();
                long counter = 0;
                foreach (var item in cases)
                {
                    var xml = ZatcaInvoiceXml.Build(ZatcaService.ComplianceSample(seller, item.DocumentType, item.IsSimplified, ++counter, ZatcaInvoiceXml.FirstPreviousHash));
                    new XmlDocument { PreserveWhitespace = true }.LoadXml(xml);
                    sizes.Add($"{item.Key}={xml.Length}");
                }
                return $"{cases.Count} well-formed UBL documents: {string.Join(", ", sizes)}";
            });

            // 6) التوقيع: الحلقة نفسها في GenerateComplianceTestsAsync (سلسلة التجزئة السابقة، ZatcaSigner.Sign)
            Stage(report, passed, StageXmlSignature, new[] { StageKeyImport, StageCertificateParsing, StageComplianceXml }, ct, () =>
            {
                var previous = ZatcaInvoiceXml.FirstPreviousHash;
                long counter = 0;
                foreach (var item in cases!)
                {
                    var invoice = ZatcaSigner.Sign(ZatcaService.ComplianceSample(seller, item.DocumentType, item.IsSimplified, ++counter, previous),
                        certificateText!, signingKey!, DateTime.UtcNow);
                    previous = invoice.Hash;
                    signed.Add((item.Key, item.IsSimplified, invoice));
                }
                return $"{signed.Count} documents signed (XAdES + ECDSA-SHA256 + QR)";
            });

            // 7) التحقق كما تفعل الهيئة: التجزئة تطابق الوثيقة، التوقيع صحيح بمفتاح الشهادة، الشهادة المضمّنة هي نفسها، وسوم QR
            Stage(report, passed, StageSignatureVerification, new[] { StageXmlSignature }, ct, () =>
            {
                using var publicKey = certificate!.GetECDsaPublicKey()!;
                foreach (var (name, simplified, invoice) in signed)
                {
                    if (Convert.ToBase64String(ZatcaSigner.Hash(invoice.Xml)) != invoice.Hash)
                        throw new CryptographicException($"{name}: the recomputed invoice hash does not match.");
                    var signature = Between(invoice.Xml, "<ds:SignatureValue>", "</ds:SignatureValue>");
                    if (!publicKey.VerifyData(Convert.FromBase64String(invoice.Hash), Convert.FromBase64String(signature), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
                        throw new CryptographicException($"{name}: the ECDSA signature does not verify with the certificate key.");
                    if (Between(invoice.Xml, "<ds:X509Certificate>", "</ds:X509Certificate>").Trim() != certificateText)
                        throw new CryptographicException($"{name}: the embedded certificate differs from the signing certificate.");
                    var tags = QrTags(Convert.FromBase64String(invoice.Qr));
                    var expected = simplified ? 9 : 8;
                    if (tags != expected) throw new InvalidOperationException($"{name}: QR has {tags} TLV tags, expected {expected}.");
                }
                return $"{signed.Count} documents: hash, signature, embedded certificate and QR tags verified";
            });

            // 8) توافق مكتبات التشفير في البيئة (مستقل عن المراحل السابقة)
            Stage(report, passed, StageEnvironmentCrypto, Array.Empty<string>(), ct, EnvironmentCryptoChecks);
        }
        finally
        {
            key?.Dispose(); signingKey?.Dispose(); ca?.Dispose(); certificate?.Dispose();
            keyPem = null; token = null; certificateText = null;
        }

        report.TotalMs = total.ElapsedMilliseconds;
        report.FirstFailedStage = report.Stages.FirstOrDefault(s => !s.Informational && s.Result == "failed")?.Name;
        report.AllPassed = report.Stages.Where(s => !s.Informational).All(s => s.Result == "passed");
        _log.LogInformation("ZATCA local self-test {RunId} finished in {Ms} ms. AllPassed={AllPassed} FirstFailedStage={Stage}",
            report.RunId, report.TotalMs, report.AllPassed, report.FirstFailedStage ?? "-");
        return report;
    }

    /// <summary>
    /// مرحلة مستقلة: تُتخطّى إن فشلت مرحلة تعتمد عليها. الفشل يُسجَّل في ILogger مع Stack Trace ومعلومات البيئة، والاستجابة
    /// تحمل النوع والرسالة والأسباب الداخلية فقط.
    /// </summary>
    private void Stage(ZatcaSelfTestReportDto report, HashSet<string> passed, string name, string[] dependsOn, CancellationToken ct, Func<string> body,
        bool informational = false)
    {
        var stage = new ZatcaSelfTestStageDto { Name = name, Informational = informational };
        report.Stages.Add(stage);
        var missing = dependsOn.FirstOrDefault(d => !passed.Contains(d));
        if (ct.IsCancellationRequested || missing != null)
        {
            stage.Result = "skipped";
            stage.Detail = ct.IsCancellationRequested ? $"Skipped: the run exceeded {RunTimeout.TotalSeconds:0}s or was cancelled." : $"Skipped: depends on '{missing}', which did not pass.";
            _log.LogWarning("ZATCA self-test {RunId} stage {Stage} skipped: {Reason}", report.RunId, name, stage.Detail);
            return;
        }

        var watch = Stopwatch.StartNew();
        try
        {
            stage.Detail = body();
            stage.Result = "passed";
            stage.ElapsedMs = watch.ElapsedMilliseconds;
            passed.Add(name);
            _log.LogInformation("ZATCA self-test {RunId} stage {Stage} passed in {Ms} ms: {Detail}", report.RunId, name, stage.ElapsedMs, stage.Detail);
        }
        catch (Exception ex)
        {
            stage.Result = "failed";
            stage.ElapsedMs = watch.ElapsedMilliseconds;
            stage.ExceptionType = ex.GetType().FullName;
            stage.ExceptionMessage = ex.Message;
            for (var inner = ex.InnerException; inner != null && stage.InnerExceptions.Count < 8; inner = inner.InnerException)
                stage.InnerExceptions.Add($"{inner.GetType().FullName}: {inner.Message}");
            if (informational)
            {
                stage.Detail = "Informational comparison stage: this failure is not counted in AllPassed.";
                _log.LogWarning(ex, "ZATCA self-test {RunId} informational stage {Stage} failed after {Ms} ms (expected under IIS without a loaded user profile). UserProfileLoaded={Profile}",
                    report.RunId, name, stage.ElapsedMs, report.Environment.GetValueOrDefault("userProfileLoaded"));
                return;
            }
            _log.LogError(ex, "ZATCA self-test {RunId} stage {Stage} FAILED after {Ms} ms. Runtime={Runtime} OS={Os} Arch={Arch} EcdsaImpl={Ecdsa} Culture={Culture} UserProfileLoaded={Profile}",
                report.RunId, name, stage.ElapsedMs, report.Environment.GetValueOrDefault("dotnet.runtime"), report.Environment.GetValueOrDefault("os"),
                report.Environment.GetValueOrDefault("process.architecture"), report.Environment.GetValueOrDefault("crypto.ecdsaImplementation"),
                report.Environment.GetValueOrDefault("culture"), report.Environment.GetValueOrDefault("userProfileLoaded"));
        }
    }

    /// <summary>خوارزميات ومكتبات يعتمد عليها الربط، كل فحص مستقل بنتيجته؛ يفشل الطور إن فشل أي منها.</summary>
    private static string EnvironmentCryptoChecks()
    {
        var results = new List<string>();
        Exception? first = null;
        void Check(string name, Action action)
        {
            var watch = Stopwatch.StartNew();
            try { action(); results.Add($"{name}=ok ({watch.ElapsedMilliseconds} ms)"); }
            catch (Exception ex) { first ??= ex; results.Add($"{name}=FAILED {ex.GetType().Name}: {ex.Message}"); }
        }

        var data = Encoding.UTF8.GetBytes("zatca-selftest");
        Check("sha256", () =>
        {
            if (Convert.ToHexString(SHA256.HashData("abc"u8)) != "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")
                throw new CryptographicException("SHA-256 test vector mismatch.");
        });
        Check("ecdsa_secp256k1", () =>
        {
            using var k = ECDsa.Create(ECCurve.CreateFromFriendlyName("secp256k1"));
            if (!k.VerifyData(data, k.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
                throw new CryptographicException("secp256k1 sign/verify failed.");
        });
        Check("ecdsa_p256", () =>
        {
            using var k = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            if (!k.VerifyData(data, k.SignData(data, HashAlgorithmName.SHA256), HashAlgorithmName.SHA256)) throw new CryptographicException("P-256 sign/verify failed.");
        });
        Check("rsa_2048_sha256", () =>
        {
            using var k = RSA.Create(2048);
            if (!k.VerifyData(data, k.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                throw new CryptographicException("RSA sign/verify failed.");
        });
        Check("xmldsig_signedxml", () =>
        {
            using var k = RSA.Create(2048);
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.LoadXml("<root><item>selftest</item></root>");
            var signedXml = new SignedXml(doc) { SigningKey = k };
            var reference = new Reference("");
            reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
            reference.AddTransform(new XmlDsigC14NTransform());
            reference.DigestMethod = SignedXml.XmlDsigSHA256Url;
            signedXml.AddReference(reference);
            signedXml.SignedInfo!.SignatureMethod = SignedXml.XmlDsigRSASHA256Url;
            signedXml.ComputeSignature();
            doc.DocumentElement!.AppendChild(doc.ImportNode(signedXml.GetXml(), true));
            var verify = new SignedXml(doc);
            verify.LoadXml((XmlElement)doc.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl)[0]!);
            if (!verify.CheckSignature(k)) throw new CryptographicException("SignedXml signature did not verify.");
        });
        Check("temp_folder_rw", () =>
        {
            var path = Path.Combine(Path.GetTempPath(), $"erp-zatca-selftest-{Guid.NewGuid():N}.tmp");
            try
            {
                var bytes = RandomNumberGenerator.GetBytes(32);
                File.WriteAllBytes(path, bytes);
                if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) throw new IOException("Temp file read-back mismatch.");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        });

        var detail = string.Join("; ", results);
        if (first != null) throw new CryptographicException($"Environment crypto checks failed: {detail}", first);
        return detail;
    }

    /// <summary>معلومات بيئة آمنة فقط: إصدارات وأنواع وإعدادات، بلا أسماء أجهزة أو مسارات أو متغيرات حساسة.</summary>
    private static Dictionary<string, string> DescribeEnvironment()
    {
        var env = new Dictionary<string, string>();
        void Add(string name, Func<string?> value)
        {
            try { env[name] = value() ?? "(null)"; }
            catch (Exception ex) { env[name] = $"unavailable ({ex.GetType().Name})"; }
        }

        Add("dotnet.runtime", () => RuntimeInformation.FrameworkDescription);
        Add("dotnet.runtimeIdentifier", () => RuntimeInformation.RuntimeIdentifier);
        Add("aspnetcore", () => Version(typeof(Microsoft.AspNetCore.Http.HttpContext).Assembly));
        Add("aspnetcore.environment", () => System.Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "(unset = Production)");
        Add("os", () => RuntimeInformation.OSDescription);
        Add("os.architecture", () => RuntimeInformation.OSArchitecture.ToString());
        Add("process.architecture", () => RuntimeInformation.ProcessArchitecture.ToString());
        Add("process.name", () => Process.GetCurrentProcess().ProcessName);
        Add("processorCount", () => System.Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture));
        Add("memory.availableToProcessMB", () => (GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024)).ToString(CultureInfo.InvariantCulture));
        Add("culture", () => CultureInfo.CurrentCulture.Name);
        Add("uiCulture", () => CultureInfo.CurrentUICulture.Name);
        Add("timeZone", () => $"{TimeZoneInfo.Local.Id} (UTC{TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow):hh\\:mm})");
        Add("utcNow", () => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        Add("userProfileLoaded", () =>
        {
            var profile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            return (!string.IsNullOrEmpty(profile) && Directory.Exists(profile)).ToString();
        });
        Add("crypto.ecdsaImplementation", () => { using var k = ECDsa.Create(); return k.GetType().Name; });
        Add("crypto.rsaImplementation", () => { using var k = RSA.Create(); return k.GetType().Name; });
        Add("crypto.System.Security.Cryptography", () => Version(typeof(ECDsa).Assembly));
        Add("crypto.System.Security.Cryptography.Xml", () => Version(typeof(SignedXml).Assembly));
        Add("crypto.System.Formats.Asn1", () => Version(typeof(System.Formats.Asn1.AsnReader).Assembly));
        Add("crypto.DataProtection", () => Version(typeof(Microsoft.AspNetCore.DataProtection.IDataProtector).Assembly));
        if (OperatingSystem.IsWindows())
            Add("crypto.windowsFipsPolicy", () => Microsoft.Win32.Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Lsa\FipsAlgorithmPolicy", "Enabled", null)?.ToString() ?? "(not set)");
        return env;
    }

    private static string Version(Assembly assembly)
        => assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
           ?? assembly.GetName().Version?.ToString() ?? "unknown";

    private static int QrTags(byte[] tlv)
    {
        var count = 0;
        for (var i = 0; i + 1 < tlv.Length; i += 2 + tlv[i + 1]) count++;
        return count;
    }

    private static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        if (from < 0) throw new InvalidOperationException($"'{start}' not found in the signed document.");
        from += start.Length;
        var to = text.IndexOf(end, from, StringComparison.Ordinal);
        if (to < 0) throw new InvalidOperationException($"'{end}' not found in the signed document.");
        return text[from..to];
    }
}
