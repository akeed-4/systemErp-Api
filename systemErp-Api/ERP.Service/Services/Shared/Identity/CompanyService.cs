using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

public class CompanyService : ICompanyService
{
    private const string ProtectedPrefix = "enc:";
    private readonly ErpDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ISecretProtector _secrets;

    public CompanyService(ErpDbContext db, ITenantContext tenant, ISecretProtector secrets)
    {
        _db = db; _tenant = tenant; _secrets = secrets;
    }

    private async Task<Tenant> LoadAsync(CancellationToken ct)
    {
        var id = _tenant.TenantId ?? throw new UnauthorizedAppException();
        return await _db.Set<Tenant>().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException(Messages.CompanyNotFound);
    }

    public async Task<TenantDto> GetCurrentAsync(CancellationToken ct = default) => Mapper.Map<TenantDto>(await LoadAsync(ct));

    public async Task<TenantDto> UpdateAsync(UpdateTenantDto r, CancellationToken ct = default)
    {
        var t = await LoadAsync(ct);
        if (string.IsNullOrWhiteSpace(r.NameAr)) throw new ValidationFailedException(Messages.CompanyArabicNameRequired);
        // فارغ = تبقى المنطقة الحالية (الواجهات القديمة لا ترسلها)
        if (r.TimeZoneId != null && !TenantTimeZone.IsValid(r.TimeZoneId)) throw new ValidationFailedException(Messages.CompanyTimeZoneInvalid);
        if (r.VatNumber != t.VatNumber)
        {
            if (!SaudiVat.IsValid(r.VatNumber))
                throw new ValidationFailedException(Messages.SaudiVatNumberFormat);
            if (await _db.Catalog.Set<Tenant>().IgnoreQueryFilters().AnyAsync(x => x.VatNumber == r.VatNumber && x.Id != t.Id, ct))
                throw new ConflictException(Messages.VatNumberAlreadyRegistered);
        }

        t.NameAr = r.NameAr.Trim(); t.NameEn = r.NameEn; t.VatNumber = r.VatNumber; t.CrNumber = r.CrNumber;
        t.Address = r.Address; t.City = r.City; t.Country = r.Country; t.Phone = r.Phone; t.Email = r.Email;
        t.LogoUrl = r.LogoUrl; t.FinancialYearStart = r.FinancialYearStart; t.FinancialYearEnd = r.FinancialYearEnd;
        if (r.TimeZoneId != null) t.TimeZoneId = r.TimeZoneId.Trim();
        // العملة الأساسية والرمز لا يتغيّران بعد التهيئة لأن القيود مسجَّلة بها.
        await _db.SaveChangesAsync(ct);
        return Mapper.Map<TenantDto>(t);
    }

    public async Task<TenantDto> UpdateZatcaConfigAsync(UpdateZatcaConfigDto r, CancellationToken ct = default)
    {
        var t = await LoadAsync(ct);
        var c = t.ZatcaConfig;
        var environmentChanged = c.Environment != r.Environment;
        if (environmentChanged && await _db.Set<ZatcaUnit>().AnyAsync(u => u.IsActive, ct))
            throw new ConflictException("Disable active ZATCA units before changing the integration environment.");

        c.IsEnabled = r.IsEnabled; c.Environment = r.Environment;
        c.Phase = r.Phase is "phase1" or "phase2" ? r.Phase : c.Phase;

        // null = إبقاء القيمة، والنص الفارغ = مسحها؛ رمز OTP لا يُحفظ مطلقاً.
        c.Csid = environmentChanged ? null : Keep(r.Csid, c.Csid);
        c.BinarySecurityToken = environmentChanged ? null : KeepSecret(r.BinarySecurityToken, c.BinarySecurityToken);
        c.SecretKey = environmentChanged ? null : KeepSecret(r.SecretKey, c.SecretKey);
        c.ApiKey = environmentChanged ? null : KeepSecret(r.ApiKey, c.ApiKey);
        c.ApiSecret = environmentChanged ? null : KeepSecret(r.ApiSecret, c.ApiSecret);
        c.CertificatePem = environmentChanged ? null : KeepSecret(r.CertificatePem, c.CertificatePem);
        c.PrivateKeyPem = environmentChanged ? null : KeepSecret(r.PrivateKeyPem, c.PrivateKeyPem);
        c.Otp = null;

        c.SolutionName = r.SolutionName ?? c.SolutionName; c.SolutionVersion = r.SolutionVersion ?? c.SolutionVersion;
        c.RegisteredDevice = r.RegisteredDevice; c.AutoSendInvoices = r.AutoSendInvoices;
        c.CustomEndpointUrl = environmentChanged ? null : r.CustomEndpointUrl; c.TaxRegistrationNumber = r.TaxRegistrationNumber;
        c.CsrCommonName = r.CsrCommonName; c.OrganizationUnit = r.OrganizationUnit; c.OrganizationName = r.OrganizationName;
        c.CountryCode = r.CountryCode ?? c.CountryCode; c.DeviceSn = r.DeviceSn; c.BusinessCategory = r.BusinessCategory;
        c.AutoSubmitOnInvoiceSave = r.AutoSubmitOnInvoiceSave;
        c.Street = r.Street; c.BuildingNumber = r.BuildingNumber; c.District = r.District; c.PostalCode = r.PostalCode;

        await _db.SaveChangesAsync(ct);
        return Mapper.Map<TenantDto>(t);
    }

    private static string? Keep(string? incoming, string? current) => incoming == null ? current : (incoming.Length == 0 ? null : incoming);
    private string? KeepSecret(string? incoming, string? current)
    {
        if (incoming is { Length: 0 }) return null;
        if (!string.IsNullOrWhiteSpace(incoming)) return ProtectedPrefix + _secrets.Protect(incoming.Trim());
        if (string.IsNullOrWhiteSpace(current) || current.StartsWith(ProtectedPrefix, StringComparison.Ordinal)) return current;
        return ProtectedPrefix + _secrets.Protect(current);
    }

    /// <summary>
    /// يتحقق من اكتمال بيانات الربط: رقم ضريبي صحيح ومستخدم واحد مربوط على الأقل (يحفظه الربط برمز التحقق
    /// (<see cref="ERP.Core.Contracts.Accounting.IZatcaService.OnboardAsync"/>). لا يُجرى اتصال بالهيئة هنا.
    /// </summary>
    public async Task<ZatcaConnectionTestResultDto> TestZatcaConnectionAsync(CancellationToken ct = default)
    {
        var t = await LoadAsync(ct);
        var c = t.ZatcaConfig;
        var missing = new List<string>();
        if (!SaudiVat.IsValid(t.VatNumber)) missing.Add("VatNumber");
        // الشهادة والمفتاح يُحفظان لكل مستخدم مربوط (ZatcaUnit) لا على المنشأة
        if (!await _db.Set<ZatcaUnit>().AnyAsync(u => u.IsActive, ct)) missing.Add("ZatcaUnit");

        var ok = missing.Count == 0;
        c.LastTestDate = DateTime.UtcNow;
        c.LastTestStatus = ok ? ZatcaSubmissionStatus.Cleared : ZatcaSubmissionStatus.Rejected;
        c.LastTestLatencyMs = 0;
        c.LastTestMessage = ok
            ? Messages.ZatcaConnected
            : Messages.ZatcaTestMissingPrefix + string.Join(", ", missing);
        await _db.SaveChangesAsync(ct);

        return new ZatcaConnectionTestResultDto { Success = ok, Message = c.LastTestMessage, MissingFields = missing };
    }
}
