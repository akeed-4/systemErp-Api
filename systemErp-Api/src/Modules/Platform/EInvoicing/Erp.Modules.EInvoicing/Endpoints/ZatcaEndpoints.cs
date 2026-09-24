using System.Diagnostics;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.BuildingBlocks.Infrastructure.Security;
using Erp.BuildingBlocks.Web;
using Erp.Modules.EInvoicing.Application;
using Erp.Modules.EInvoicing.Contracts;
using Erp.Modules.EInvoicing.Domain;
using Erp.Modules.EInvoicing.Persistence;
using Erp.SharedKernel.Errors;
using Erp.SharedKernel.Security;
using Erp.SharedKernel.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.EInvoicing.Endpoints;

/// <summary>The frontend ZatcaConfig shape. Secrets are write-only: only has* flags come back.</summary>
internal sealed record ZatcaConfigDto(
    Guid DeviceId,
    bool IsEnabled,
    ZatcaEnvironment Environment,
    ZatcaPhase Phase,
    ComplianceStatus ComplianceStatus,
    string SolutionName,
    string SolutionVersion,
    string RegisteredDevice,
    bool AutoSendInvoices,
    string? CustomEndpointUrl,
    string? TaxRegistrationNumber,
    string? CsrCommonName,
    string? OrganizationUnit,
    string? OrganizationName,
    string CountryCode,
    string DeviceSn,
    string? BusinessCategory,
    bool HasCsid,
    bool HasSecret,
    bool HasCertificate,
    bool HasPrivateKey,
    long LastIcv,
    DateTimeOffset? LastTestDate,
    ConnectionTestStatus LastTestStatus,
    int? LastTestLatencyMs,
    string? LastTestMessage);

internal sealed record SaveZatcaConfigRequest(
    bool? IsEnabled,
    ZatcaEnvironment? Environment,
    ZatcaPhase? Phase,
    string? SolutionName,
    string? SolutionVersion,
    bool? AutoSendInvoices,
    string? CustomEndpointUrl,
    string? TaxRegistrationNumber,
    string? CsrCommonName,
    string? OrganizationUnit,
    string? OrganizationName,
    string? CountryCode,
    string? BusinessCategory,
    string? Csid,
    string? SecretKey,
    string? CertificatePem,
    string? PrivateKeyPem);

internal sealed record EInvoiceDocumentDto(
    Guid Id,
    Guid DeviceId,
    string SourceModule,
    string SourceDocumentType,
    Guid SourceDocumentId,
    string DocumentNumber,
    EInvoiceKind Kind,
    DateTimeOffset IssuedAt,
    decimal TotalWithVat,
    decimal VatTotal,
    string? BuyerName,
    Guid Uuid,
    long Icv,
    string InvoiceHash,
    string PreviousInvoiceHash,
    string QrCode,
    EInvoiceStatus Status,
    DateTimeOffset? SubmittedAt,
    string? ResponseMessage);

internal sealed record ConnectionTestResult(bool Success, int StatusCode, string Message, int LatencyMs, DateTimeOffset Timestamp);

/// <summary>zatca-integration: device configuration, connection test, the e-invoice register and submission.</summary>
internal static class ZatcaEndpoints
{
    public const string HttpClientName = "zatca";

    public static void Map(IEndpointRouteBuilder app)
    {
        var zatca = app.MapGroup("/api/v1/zatca").WithTags("EInvoicing");
        zatca.MapGet("config", GetConfigAsync).RequireScreen(ScreenIds.Zatca, ScreenAction.View);
        zatca.MapPut("config", SaveConfigAsync).RequireScreen(ScreenIds.Zatca, ScreenAction.Edit);
        zatca.MapPost("config/test-connection", TestConnectionAsync).RequireScreen(ScreenIds.Zatca, ScreenAction.Edit);
        zatca.MapGet("devices", ListDevicesAsync).RequireScreen(ScreenIds.Zatca, ScreenAction.View);
        zatca.MapGet("documents", ListDocumentsAsync).RequireScreen(ScreenIds.Zatca, ScreenAction.View);
        zatca.MapGet("documents/{id:guid}", GetDocumentAsync).RequireScreen(ScreenIds.Zatca, ScreenAction.View);
        zatca.MapPost("documents/{id:guid}/submit", SubmitAsync).RequireScreen(ScreenIds.Zatca, ScreenAction.Approve);
    }

    private static async Task<IResult> GetConfigAsync(EInvoicingDbContext db, CancellationToken ct) =>
        ErpResults.Ok(ToDto(await DefaultDeviceAsync(db, ct)));

    private static async Task<IResult> SaveConfigAsync(SaveZatcaConfigRequest request, EInvoicingDbContext db, ISecretProtector secrets, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var device = await DefaultDeviceAsync(db, ct);
        if (request.TaxRegistrationNumber is { Length: > 0 } vat && !SaudiIdentifiers.IsVatNumber(vat))
        {
            throw ErpException.Validation("The tax registration number must be a 15-digit VAT number.", "الرقم الضريبي يجب أن يتكون من 15 رقماً.");
        }

        device.Configure(new DeviceSettings(
            null,
            request.IsEnabled ?? device.IsEnabled,
            request.Environment ?? device.Environment,
            request.Phase ?? device.Phase,
            request.AutoSendInvoices ?? device.AutoSubmit,
            request.SolutionName,
            request.SolutionVersion,
            request.TaxRegistrationNumber ?? device.TaxRegistrationNumber,
            request.CsrCommonName ?? device.CsrCommonName,
            request.OrganizationUnit ?? device.OrganizationUnit,
            request.OrganizationName ?? device.OrganizationName,
            request.CountryCode,
            request.BusinessCategory ?? device.BusinessCategory,
            request.CustomEndpointUrl));
        device.SetSecrets(
            Protect(secrets, request.Csid),
            Protect(secrets, request.SecretKey),
            string.IsNullOrWhiteSpace(request.CertificatePem) ? null : request.CertificatePem,
            Protect(secrets, request.PrivateKeyPem));
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Ok(ToDto(device), "تم حفظ إعدادات الربط مع هيئة الزكاة");
    }

    /// <summary>A real reachability check of the ZATCA gateway for the configured environment (no invoice is sent).</summary>
    private static async Task<IResult> TestConnectionAsync(EInvoicingDbContext db, IHttpClientFactory httpClients, IUnitOfWork unitOfWork, TimeProvider clock, CancellationToken ct)
    {
        var device = await DefaultDeviceAsync(db, ct);
        var url = device.CustomEndpointUrl ?? device.Environment switch
        {
            ZatcaEnvironment.Production => "https://gw-fatoora.zatca.gov.sa/e-invoicing/core",
            ZatcaEnvironment.Simulation => "https://gw-fatoora.zatca.gov.sa/e-invoicing/simulation",
            _ => "https://gw-fatoora.zatca.gov.sa/e-invoicing/developer-portal",
        };

        var stopwatch = Stopwatch.StartNew();
        ConnectionTestResult result;
        try
        {
            using var response = await httpClients.CreateClient(HttpClientName).GetAsync(new Uri(url), ct);
            result = new ConnectionTestResult(true, (int)response.StatusCode, $"تم الوصول إلى بوابة فاتورة ({device.Environment}) - HTTP {(int)response.StatusCode}", (int)stopwatch.ElapsedMilliseconds, clock.GetUtcNow());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            result = new ConnectionTestResult(false, 0, $"تعذر الوصول إلى بوابة فاتورة: {ex.Message}", (int)stopwatch.ElapsedMilliseconds, clock.GetUtcNow());
        }

        device.RecordTest(result.Timestamp, result.Success, result.LatencyMs, result.Message);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Ok(result);
    }

    private static async Task<IResult> ListDevicesAsync(EInvoicingDbContext db, CancellationToken ct) =>
        ErpResults.Ok((await db.Devices.AsNoTracking().OrderByDescending(d => d.IsDefault).ThenBy(d => d.SerialNumber).ToListAsync(ct)).Select(ToDto).ToList());

    private static async Task<IResult> ListDocumentsAsync([AsParameters] PaginationParams paging, EInvoiceStatus? status, EInvoicingDbContext db, CancellationToken ct)
    {
        var query = db.Documents.AsNoTracking();
        if (status is { } s)
        {
            query = query.Where(d => d.Status == s);
        }

        if (!string.IsNullOrWhiteSpace(paging.SearchTerm))
        {
            var term = paging.SearchTerm.Trim();
            query = query.Where(d => d.DocumentNumber.Contains(term) || (d.BuyerName != null && d.BuyerName.Contains(term)));
        }

        return ErpResults.Ok(await query.OrderByDescending(d => d.IssuedAt).Select(d => ToDto(d)).ToPagedResultAsync(paging, ct));
    }

    private static async Task<IResult> GetDocumentAsync(Guid id, EInvoicingDbContext db, CancellationToken ct) =>
        ErpResults.Ok(ToDto(await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct) ?? throw ErpException.NotFound("E-invoice", "الفاتورة الإلكترونية")));

    private static async Task<IResult> SubmitAsync(Guid id, EInvoicingDbContext db, IZatcaGateway gateway, IUnitOfWork unitOfWork, TimeProvider clock, CancellationToken ct)
    {
        var document = await db.Documents.SingleOrDefaultAsync(d => d.Id == id, ct) ?? throw ErpException.NotFound("E-invoice", "الفاتورة الإلكترونية");
        var device = await db.Devices.SingleAsync(d => d.Id == document.DeviceId, ct);
        var (status, message) = await gateway.SubmitAsync(device, document, ct);
        document.RecordSubmission(status, clock.GetUtcNow(), message);
        await unitOfWork.SaveChangesAsync(ct);
        return ErpResults.Ok(ToDto(document));
    }

    private static async Task<EInvoicingDevice> DefaultDeviceAsync(EInvoicingDbContext db, CancellationToken ct) =>
        await db.Devices.SingleOrDefaultAsync(d => d.IsDefault, ct)
        ?? throw ErpException.NotFound("E-invoicing device", "جهاز الفوترة الإلكترونية");

    private static string? Protect(ISecretProtector secrets, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : secrets.Protect(value.Trim());

    private static ZatcaConfigDto ToDto(EInvoicingDevice d) =>
        new(d.Id, d.IsEnabled, d.Environment, d.Phase, d.ComplianceStatus, d.SolutionName, d.SolutionVersion, d.SerialNumber, d.AutoSubmit, d.CustomEndpointUrl,
            d.TaxRegistrationNumber, d.CsrCommonName, d.OrganizationUnit, d.OrganizationName, d.CountryCode, d.SerialNumber, d.BusinessCategory,
            d.CsidEncrypted is not null, d.SecretEncrypted is not null, d.CertificatePem is not null, d.PrivateKeyEncrypted is not null, d.LastIcv,
            d.LastTestAt, d.LastTestStatus, d.LastTestLatencyMs, d.LastTestMessage);

    private static EInvoiceDocumentDto ToDto(EInvoiceDocument d) =>
        new(d.Id, d.DeviceId, d.SourceModule, d.SourceDocumentType, d.SourceDocumentId, d.DocumentNumber, d.Kind, d.IssuedAt, d.TotalWithVat, d.VatTotal,
            d.BuyerName, d.Uuid, d.Icv, d.InvoiceHash, d.PreviousInvoiceHash, d.QrCode, d.Status, d.SubmittedAt, d.ResponseMessage);
}
