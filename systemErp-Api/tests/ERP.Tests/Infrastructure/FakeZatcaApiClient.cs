using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ERP.Core.Contracts.Accounting;
using ERP.Service.Services.Accounting;

namespace ERP.Tests.Infrastructure;

/// <summary>
/// بديل هيئة الزكاة في الاختبارات (لا اتصال بالإنترنت): يصدر شهادة حقيقية من طلب الشهادة (فتحمل مفتاح الوحدة العام)،
/// ويتحقق من كل فاتورة كما تفعل الهيئة: التجزئة المرسلة تطابق تجزئة الوثيقة، والتوقيع صحيح بمفتاح الشهادة.
/// </summary>
public sealed class FakeZatcaApiClient : IZatcaApiClient
{
    public static readonly FakeZatcaApiClient Instance = new();
    public const string Otp = "123456";
    /// <summary>اسم مشترٍ يجعل «الهيئة» ترفض الفاتورة.</summary>
    public const string RejectedBuyer = "عميل مرفوض";
    /// <summary>اسم مشترٍ يجعل «الهيئة» تقبل الفاتورة بتحذير.</summary>
    public const string WarnedBuyer = "عميل بتحذير";

    private readonly ECDsa _ca = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private long _requests;

    /// <summary>true = الهيئة غير متاحة (انقطاع اتصال).</summary>
    public bool Down { get; set; }
    public System.Collections.Concurrent.ConcurrentQueue<ZatcaSubmission> Received { get; } = new();

    public Task<ZatcaCsidResult> RequestComplianceCsidAsync(string baseUrl, string csrBase64, string otp, CancellationToken ct = default)
    {
        if (otp != Otp) return Task.FromResult(new ZatcaCsidResult(false, null, null, null, "400: Invalid OTP"));
        var request = CertificateRequest.LoadSigningRequestPem(Encoding.UTF8.GetString(Convert.FromBase64String(csrBase64)), HashAlgorithmName.SHA256,
            CertificateRequestLoadOptions.UnsafeLoadCertificateExtensions);
        using var certificate = request.Create(new X500DistinguishedName("CN=Fake ZATCA CA"), X509SignatureGenerator.CreateForECDsa(_ca),
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1), RandomNumberGenerator.GetBytes(8));
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(Convert.ToBase64String(certificate.RawData)));
        return Task.FromResult(new ZatcaCsidResult(true, Interlocked.Increment(ref _requests).ToString(), token, "compliance-secret", "ISSUED"));
    }

    /// <summary>شهادة الإنتاج هي شهادة الامتثال نفسها (تحمل مفتاح الوحدة)، بسرّ جديد.</summary>
    public Task<ZatcaCsidResult> RequestProductionCsidAsync(string baseUrl, string complianceRequestId, string token, string secret, CancellationToken ct = default)
        => Task.FromResult(secret == "compliance-secret"
            ? new ZatcaCsidResult(true, "P" + complianceRequestId, token, "production-secret", "ISSUED")
            : new ZatcaCsidResult(false, null, null, null, "401: Unauthorized"));

    public Task<ZatcaApiResult> CheckComplianceAsync(string baseUrl, string token, string secret, ZatcaSubmission invoice, CancellationToken ct = default)
        => Task.FromResult(Validate(token, invoice, cleared: false));

    public Task<ZatcaApiResult> ReportAsync(string baseUrl, string token, string secret, ZatcaSubmission invoice, CancellationToken ct = default)
        => Task.FromResult(Submit(token, secret, invoice, cleared: false, expectedType: "0200000"));

    public Task<ZatcaApiResult> ClearAsync(string baseUrl, string token, string secret, ZatcaSubmission invoice, CancellationToken ct = default)
        => Task.FromResult(Submit(token, secret, invoice, cleared: true, expectedType: "0100000"));

    private ZatcaApiResult Submit(string token, string secret, ZatcaSubmission invoice, bool cleared, string expectedType)
    {
        if (Down) return new ZatcaApiResult { Reached = false, Message = "connection refused" };
        if (secret != "production-secret") return new ZatcaApiResult { Reached = false, HttpStatus = 401, Message = "401: Unauthorized" };
        if (!invoice.Xml.Contains($"name=\"{expectedType}\"")) return Rejected("wrong-endpoint: invoice type does not match the endpoint");
        Received.Enqueue(invoice);
        return Validate(token, invoice, cleared);
    }

    private static ZatcaApiResult Validate(string token, ZatcaSubmission invoice, bool cleared)
    {
        if (Convert.ToBase64String(ZatcaSigner.Hash(invoice.Xml)) != invoice.InvoiceHash)
            return Rejected("invalid-invoice-hash: The invoice hash API body does not match the (calculated) Hash of the XML");

        using var certificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(Encoding.UTF8.GetString(Convert.FromBase64String(token))));
        using var publicKey = certificate.GetECDsaPublicKey()!;
        var signature = Between(invoice.Xml, "<ds:SignatureValue>", "</ds:SignatureValue>");
        if (!publicKey.VerifyData(Convert.FromBase64String(invoice.InvoiceHash), Convert.FromBase64String(signature), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
            return Rejected("invalid-signature: The invoice signature does not match the certificate");

        if (invoice.Xml.Contains(RejectedBuyer)) return Rejected("BR-KSA-TEST: buyer is not acceptable");
        return new ZatcaApiResult
        {
            Reached = true, HttpStatus = invoice.Xml.Contains(WarnedBuyer) ? 202 : 200, Accepted = true,
            Warnings = invoice.Xml.Contains(WarnedBuyer) ? new List<string> { "BR-KSA-WARN: buyer needs attention" } : new List<string>(),
            ClearedInvoice = cleared ? Convert.ToBase64String(Encoding.UTF8.GetBytes(invoice.Xml)) : null,
        };
    }

    private static ZatcaApiResult Rejected(string error) => new() { Reached = true, HttpStatus = 400, Accepted = false, Errors = new List<string> { error } };

    public static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal) + start.Length;
        return text[from..text.IndexOf(end, from, StringComparison.Ordinal)];
    }
}
