using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Erp.BuildingBlocks.Infrastructure.Modules;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.EInvoicing.Contracts;
using Erp.Modules.EInvoicing.Domain;
using Erp.Modules.EInvoicing.Persistence;
using Erp.Modules.Organization.Contracts;
using Erp.SharedKernel.Errors;
using Erp.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.EInvoicing.Application;

internal sealed class EInvoicingSeeder(EInvoicingDbContext db) : IModuleSeeder
{
    public int Order => 110;

    public async Task SeedTenantAsync(TenantSeedContext context, CancellationToken cancellationToken)
    {
        if (await db.Devices.AnyAsync(cancellationToken))
        {
            return;
        }

        var device = new EInvoicingDevice(EInvoicingDevice.DefaultSerialNumber, "الفوترة الرئيسية (المكتب الخلفي)", isDefault: true);
        device.Configure(new DeviceSettings(
            null, true, ZatcaEnvironment.Sandbox, ZatcaPhase.Phase1, false, null, null,
            context.Company.VatNumber, context.TenantCode, "الإدارة العامة", context.Company.NameAr, "SA", context.Company.Industry, null));
        db.Devices.Add(device);
    }
}

/// <summary>
/// Chains every invoice of a device: next ICV, previous hash, invoice hash, QR. The device row is locked (UPDLOCK) for
/// the caller's transaction, so ICVs are strictly sequential even under concurrent checkouts.
/// Phase 1 hash: SHA-256 over a canonical summary of the document; Phase 2 (§17.4 phase 15) replaces it with the
/// canonical UBL XML hash and adds the cryptographic stamp.
/// </summary>
internal sealed class EInvoicingService(
    EInvoicingDbContext db,
    IUnitOfWork unitOfWork,
    ICompanyProfileReader companies,
    ITenantContext tenant) : IEInvoicingService
{
    public async Task<EInvoiceResult> RegisterAsync(EInvoiceRequest request, CancellationToken cancellationToken)
    {
        var existing = await FindBySourceAsync(request.SourceModule, request.SourceDocumentType, request.SourceDocumentId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        await unitOfWork.EnsureTransactionAsync(cancellationToken);
        var device = await LockDeviceAsync(request.DeviceId, cancellationToken);
        if (!device.IsEnabled)
        {
            throw ErpException.Conflict("einvoicing_disabled", "E-invoicing is disabled for this device.", "الفوترة الإلكترونية معطلة لهذا الجهاز.");
        }

        var company = await companies.GetCurrentAsync(cancellationToken)
            ?? throw ErpException.Conflict("company_profile_missing", "The company profile is missing.", "ملف المنشأة غير موجود.");
        var sellerVat = device.TaxRegistrationNumber ?? company.VatNumber;

        var (icv, previousHash) = device.NextInChain();
        var hash = Hash(request, icv, previousHash, sellerVat);
        var qr = ZatcaTlv.Encode(company.NameAr, sellerVat, request.IssuedAt, request.TotalWithVat, request.VatTotal,
            device.Phase == ZatcaPhase.Phase2 ? hash : null);

        var document = new EInvoiceDocument(device.Id, request, icv, previousHash, hash, qr);
        db.Documents.Add(document);
        device.Advance(icv, hash);
        return ToResult(document);
    }

    public async Task<EInvoiceResult?> FindBySourceAsync(string sourceModule, string sourceDocumentType, Guid sourceDocumentId, CancellationToken cancellationToken)
    {
        var document = db.Documents.Local.SingleOrDefault(d => d.SourceModule == sourceModule && d.SourceDocumentType == sourceDocumentType && d.SourceDocumentId == sourceDocumentId)
            ?? await db.Documents.AsNoTracking().SingleOrDefaultAsync(
                d => d.SourceModule == sourceModule && d.SourceDocumentType == sourceDocumentType && d.SourceDocumentId == sourceDocumentId,
                cancellationToken);
        return document is null ? null : ToResult(document);
    }

    private async Task<EInvoicingDevice> LockDeviceAsync(Guid? deviceId, CancellationToken ct)
    {
        var tenantId = tenant.TenantId;
        var query = deviceId is { } id
            ? db.Devices.FromSqlInterpolated($"SELECT * FROM [einvoicing].[EInvoicingDevices] WITH (UPDLOCK, ROWLOCK) WHERE [TenantId] = {tenantId} AND [Id] = {id}")
            : db.Devices.FromSqlInterpolated($"SELECT * FROM [einvoicing].[EInvoicingDevices] WITH (UPDLOCK, ROWLOCK) WHERE [TenantId] = {tenantId} AND [IsDefault] = 1");
        return await query.SingleOrDefaultAsync(ct)
            ?? throw ErpException.Conflict("einvoicing_device_missing", "No e-invoicing device is configured.", "لا يوجد جهاز فوترة إلكترونية مُعد.");
    }

    private static string Hash(EInvoiceRequest request, long icv, string previousHash, string sellerVat)
    {
        var canonical = string.Join(
            '|',
            icv.ToString(CultureInfo.InvariantCulture),
            request.DocumentNumber,
            request.Kind.ToString(),
            request.IssuedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            sellerVat,
            request.BuyerVatNumber ?? string.Empty,
            request.TotalWithVat.ToString("0.00", CultureInfo.InvariantCulture),
            request.VatTotal.ToString("0.00", CultureInfo.InvariantCulture),
            previousHash);
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static EInvoiceResult ToResult(EInvoiceDocument d) =>
        new(d.Id, d.Uuid, d.Icv, d.InvoiceHash, d.PreviousInvoiceHash, d.QrCode, d.Status);
}

/// <summary>The ZATCA Fatoora API client. Swapped for the real one in phase 15 (needs the tenant's CSID and private key).</summary>
internal interface IZatcaGateway
{
    Task<(EInvoiceStatus Status, string Message)> SubmitAsync(EInvoicingDevice device, EInvoiceDocument document, CancellationToken cancellationToken);
}

/// <summary>Refuses clearly instead of pretending: no document is reported or cleared without real onboarding.</summary>
internal sealed class NotConfiguredZatcaGateway : IZatcaGateway
{
    public Task<(EInvoiceStatus Status, string Message)> SubmitAsync(EInvoicingDevice device, EInvoiceDocument document, CancellationToken cancellationToken) =>
        throw new ErpException(
            "zatca_onboarding_required",
            409,
            "Submission to ZATCA requires Phase 2 onboarding (CSR, compliance and production CSID). The document is registered locally with its QR code.",
            "الإرسال إلى هيئة الزكاة يتطلب إكمال الربط للمرحلة الثانية (CSID). الفاتورة مسجلة محلياً مع رمز QR.");
}
