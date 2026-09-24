using ERP.Core.Contracts.Shared;
using ERP.Core.DTOs.Shared;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

public class CompanyService : ICompanyService
{
    private readonly ErpDbContext _db;
    private readonly ITenantContext _tenant;

    public CompanyService(ErpDbContext db, ITenantContext tenant)
    {
        _db = db; _tenant = tenant;
    }

    private async Task<Tenant> LoadAsync(CancellationToken ct)
    {
        var id = _tenant.TenantId ?? throw new UnauthorizedAppException();
        return await _db.Set<Tenant>().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw new NotFoundException("المنشأة غير موجودة");
    }

    public async Task<TenantDto> GetCurrentAsync(CancellationToken ct = default) => Mapper.Map<TenantDto>(await LoadAsync(ct));

    public async Task<TenantDto> UpdateAsync(UpdateTenantDto r, CancellationToken ct = default)
    {
        var t = await LoadAsync(ct);
        if (string.IsNullOrWhiteSpace(r.NameAr)) throw new ValidationFailedException("اسم المنشأة بالعربية مطلوب.");
        if (r.VatNumber != t.VatNumber)
        {
            if (!SaudiVat.IsValid(r.VatNumber))
                throw new ValidationFailedException("الرقم الضريبي السعودي يجب أن يتكون من 15 خانة ويبدأ وينتهي بالرقم 3.");
            if (await _db.Set<Tenant>().IgnoreQueryFilters().AnyAsync(x => x.VatNumber == r.VatNumber && x.Id != t.Id, ct))
                throw new ConflictException("هذا الرقم الضريبي مسجّل مسبقاً.");
        }

        t.NameAr = r.NameAr.Trim(); t.NameEn = r.NameEn; t.VatNumber = r.VatNumber; t.CrNumber = r.CrNumber;
        t.Address = r.Address; t.City = r.City; t.Country = r.Country; t.Phone = r.Phone; t.Email = r.Email;
        t.LogoUrl = r.LogoUrl; t.FinancialYearStart = r.FinancialYearStart; t.FinancialYearEnd = r.FinancialYearEnd;
        // العملة الأساسية والرمز لا يتغيّران بعد التهيئة لأن القيود مسجَّلة بها.
        await _db.SaveChangesAsync(ct);
        return Mapper.Map<TenantDto>(t);
    }

    public async Task<TenantDto> UpdateZatcaConfigAsync(UpdateZatcaConfigDto r, CancellationToken ct = default)
    {
        var t = await LoadAsync(ct);
        var c = t.ZatcaConfig;
        c.IsEnabled = r.IsEnabled; c.Environment = r.Environment;
        c.Phase = r.Phase is "phase1" or "phase2" ? r.Phase : c.Phase;

        // الأسرار: null = أبقِ القيمة الحالية، نص فارغ = امسحها (لأن القراءة لا تُعيدها أبداً).
        c.Csid = Keep(r.Csid, c.Csid); c.BinarySecurityToken = Keep(r.BinarySecurityToken, c.BinarySecurityToken);
        c.SecretKey = Keep(r.SecretKey, c.SecretKey); c.ApiKey = Keep(r.ApiKey, c.ApiKey);
        c.ApiSecret = Keep(r.ApiSecret, c.ApiSecret); c.CertificatePem = Keep(r.CertificatePem, c.CertificatePem);
        c.PrivateKeyPem = Keep(r.PrivateKeyPem, c.PrivateKeyPem); c.Otp = Keep(r.Otp, c.Otp);

        c.SolutionName = r.SolutionName ?? c.SolutionName; c.SolutionVersion = r.SolutionVersion ?? c.SolutionVersion;
        c.RegisteredDevice = r.RegisteredDevice; c.AutoSendInvoices = r.AutoSendInvoices;
        c.CustomEndpointUrl = r.CustomEndpointUrl; c.TaxRegistrationNumber = r.TaxRegistrationNumber;
        c.CsrCommonName = r.CsrCommonName; c.OrganizationUnit = r.OrganizationUnit; c.OrganizationName = r.OrganizationName;
        c.CountryCode = r.CountryCode ?? c.CountryCode; c.DeviceSn = r.DeviceSn; c.BusinessCategory = r.BusinessCategory;
        c.AutoSubmitOnInvoiceSave = r.AutoSubmitOnInvoiceSave;

        await _db.SaveChangesAsync(ct);
        return Mapper.Map<TenantDto>(t);
    }

    private static string? Keep(string? incoming, string? current) => incoming == null ? current : (incoming.Length == 0 ? null : incoming);

    /// <summary>
    /// يتحقق من اكتمال بيانات الربط فقط. لا يُجرى اتصال فعلي بهيئة الزكاة قبل توفّر بيانات التسجيل
    /// (CSID/شهادة) الحقيقية للمنشأة، لذلك لا تُعاد نتيجة نجاح مصطنعة.
    /// </summary>
    public async Task<ZatcaConnectionTestResultDto> TestZatcaConnectionAsync(CancellationToken ct = default)
    {
        var t = await LoadAsync(ct);
        var c = t.ZatcaConfig;
        var missing = new List<string>();
        if (!SaudiVat.IsValid(t.VatNumber)) missing.Add("VatNumber");
        if (string.IsNullOrWhiteSpace(c.Csid)) missing.Add("Csid");
        if (string.IsNullOrWhiteSpace(c.BinarySecurityToken)) missing.Add("BinarySecurityToken");
        if (string.IsNullOrWhiteSpace(c.SecretKey)) missing.Add("SecretKey");

        var ok = missing.Count == 0;
        c.LastTestDate = DateTime.UtcNow;
        c.LastTestStatus = ok ? ZatcaSubmissionStatus.Warning : ZatcaSubmissionStatus.Rejected;
        c.LastTestLatencyMs = 0;
        c.LastTestMessage = ok
            ? "بيانات الربط مكتملة، لكن الاتصال الفعلي بهيئة الزكاة غير مُفعَّل في هذه النسخة."
            : "بيانات الربط ناقصة: " + string.Join(", ", missing);
        await _db.SaveChangesAsync(ct);

        return new ZatcaConnectionTestResultDto { Success = ok, Message = c.LastTestMessage, MissingFields = missing };
    }
}
