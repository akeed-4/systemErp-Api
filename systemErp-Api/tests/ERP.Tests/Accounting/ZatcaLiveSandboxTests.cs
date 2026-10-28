using System.Security.Cryptography;
using ERP.Core.Contracts.Accounting;
using ERP.Core.Models.Shared;
using ERP.Service.Services.Accounting;
using Xunit.Abstractions;

namespace ERP.Tests;

/// <summary>
/// اختبار حيّ على بوابة المطورين لدى هيئة الزكاة (بيانات وهمية): إصدار شهادة امتثال، ثم فحص فواتير موقّعة.
/// لا يعمل إلا بضبط ZATCA_LIVE=1 لأنه يتصل بالإنترنت؛ بقية الاختبارات تستخدم بديلاً محلياً للهيئة.
/// </summary>
public class ZatcaLiveSandboxTests
{
    private const string Sandbox = "https://gw-fatoora.zatca.gov.sa/e-invoicing/developer-portal";
    private const string SandboxOtp = "123345";
    private const string Vat = "399999999900003";

    private readonly ITestOutputHelper _out;
    public ZatcaLiveSandboxTests(ITestOutputHelper output) => _out = output;

    private sealed class Factory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new() { Timeout = TimeSpan.FromSeconds(60) };
    }

    private static ZatcaParty Seller => new("شركة اختبار الفوترة", Vat, "1010010000", "طريق الملك فهد", "1234", "العليا", "الرياض", "12345", "SA");
    private static ZatcaParty Buyer => new("شركة المشتري", "399999999800003", "1010020000", "شارع التحلية", "4321", "السليمانية", "الرياض", "54321", "SA");

    private static ZatcaDocument Document(string type, bool simplified, long counter, string previousHash) => new()
    {
        Number = $"SME-{counter:0000}", Uuid = Guid.NewGuid(), IssueDate = DateTime.UtcNow.AddHours(3).Date, IssueTime = DateTime.UtcNow.AddHours(3).ToString("HH:mm:ss"),
        TypeCode = type, IsSimplified = simplified, Seller = Seller, Buyer = Buyer, Counter = counter, PreviousHash = previousHash,
        BillingReference = type == ZatcaInvoiceXml.Invoice ? null : "SME-0001", NoteReason = type == ZatcaInvoiceXml.Invoice ? null : "إرجاع بضاعة",
        Lines = new[]
        {
            new ZatcaLine("صنف أول", 2, 50.33m, 0.66m, 100.00m, 0, 15, 15.00m, VatCategory.Standard, null, null),
            new ZatcaLine("صنف ثانٍ", 3, 33.33m, 0, 99.99m, 0, 15, 15.00m, VatCategory.Standard, null, null),
            new ZatcaLine("صنف صفري", 1, 10, 0, 10.00m, 0, 0, 0, VatCategory.ZeroRated, "VATEX-SA-35", "الأدوية والمعدات الطبية المؤهلة"),
        },
    };

    [Fact]
    public async Task Compliance_csid_is_issued_and_signed_invoices_pass_the_compliance_checks()
    {
        if (Environment.GetEnvironmentVariable("ZATCA_LIVE") != "1") return;

        var api = new ZatcaApiClient(new Factory());
        using var key = ZatcaCsr.NewKey();
        var csr = ZatcaCsr.Create(key, new ZatcaCsrInput(ZatcaEnvironment.Sandbox, "TST-886431145-399999999900003", "الفرع الرئيسي", "شركة اختبار الفوترة", "SA",
            "1-ERP|2-1.0|3-" + Guid.NewGuid(), Vat, "1100", "RRRD2929", "Trading"));

        var csid = await api.RequestComplianceCsidAsync(Sandbox, csr, SandboxOtp);
        _out.WriteLine($"compliance csid: success={csid.Success} request={csid.RequestId} message={csid.Message}");
        Assert.True(csid.Success, csid.Message);
        var certificate = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(csid.BinarySecurityToken!));

        var previous = ZatcaInvoiceXml.FirstPreviousHash;
        long counter = 0;
        var failures = new List<string>();
        foreach (var simplified in new[] { true, false })
            foreach (var type in new[] { ZatcaInvoiceXml.Invoice, ZatcaInvoiceXml.CreditNote, ZatcaInvoiceXml.DebitNote })
            {
                var signed = ZatcaSigner.Sign(Document(type, simplified, ++counter, previous), certificate, key, DateTime.UtcNow);
                previous = signed.Hash;
                var result = await api.CheckComplianceAsync(Sandbox, csid.BinarySecurityToken!, csid.Secret!, new ZatcaSubmission(signed.Hash, signed.Uuid, signed.Xml));
                _out.WriteLine($"{(simplified ? "simplified" : "standard")} {type}: http={result.HttpStatus} accepted={result.Accepted} {result.Message}");
                foreach (var e in result.Errors) _out.WriteLine("   ERROR " + e);
                foreach (var w in result.Warnings) _out.WriteLine("   WARN  " + w);
                if (!result.Accepted) failures.Add($"{simplified}/{type}: {string.Join(" | ", result.Errors)} {result.Message}");
                if (counter == 1) _out.WriteLine(signed.Xml);
            }
        Assert.Empty(failures);

        var production = await api.RequestProductionCsidAsync(Sandbox, csid.RequestId!, csid.BinarySecurityToken!, csid.Secret!);
        _out.WriteLine($"production csid: success={production.Success} message={production.Message}");
        Assert.True(production.Success, production.Message);
    }
}
