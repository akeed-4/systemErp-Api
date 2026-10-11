using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ERP.Service.Services.Accounting;

/// <summary>بيانات طلب شهادة ختم التشفير (CSR) لوحدة إصدار الفواتير، كما تحددها هيئة الزكاة.</summary>
public sealed record ZatcaCsrInput(
    ZatcaEnvironment Environment,
    string CommonName,
    string OrganizationUnit,
    string OrganizationName,
    string CountryCode,
    /// <summary>الرقم التسلسلي للوحدة بصيغة 1-الحل|2-الإصدار|3-الرقم.</summary>
    string SerialNumber,
    string VatNumber,
    /// <summary>أنواع الفواتير المدعومة (TSCZ): 1100 = ضريبية ومبسطة.</summary>
    string InvoiceTypes,
    string RegisteredAddress,
    string BusinessCategory);

/// <summary>
/// ينشئ مفتاح الوحدة الخاص (ECDSA على المنحنى secp256k1) وطلب الشهادة بالحقول التي تشترطها الهيئة: قالب الشهادة
/// بحسب البيئة، والاسم البديل (dirName) حاملاً الرقم التسلسلي والرقم الضريبي وأنواع الفواتير والعنوان والنشاط.
/// </summary>
public static class ZatcaCsr
{
    private const string TemplateNameOid = "1.3.6.1.4.1.311.20.2";
    private const string SubjectAltNameOid = "2.5.29.17";
    private const string SurnameOid = "2.5.4.4";
    private const string UserIdOid = "0.9.2342.19200300.100.1.1";
    private const string TitleOid = "2.5.4.12";
    private const string RegisteredAddressOid = "2.5.4.26";
    private const string BusinessCategoryOid = "2.5.4.15";

    public static ECDsa NewKey() => ECDsa.Create(ECCurve.CreateFromFriendlyName("secp256k1"));

    /// <summary>طلب الشهادة كما ترسله الواجهة البرمجية للهيئة: نص PEM مرمَّزاً Base64.</summary>
    public static string Create(ECDsa key, ZatcaCsrInput input)
    {
        var subject = new X500DistinguishedNameBuilder();
        subject.AddCommonName(input.CommonName);
        subject.AddOrganizationalUnitName(input.OrganizationUnit);
        subject.AddOrganizationName(input.OrganizationName);
        subject.AddCountryOrRegion(input.CountryCode);

        var request = new CertificateRequest(subject.Build(), key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509Extension(TemplateNameOid, TemplateName(input.Environment), critical: false));
        request.CertificateExtensions.Add(new X509Extension(SubjectAltNameOid, DirectoryName(input), critical: false));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(request.CreateSigningRequestPem()));
    }

    /// <summary>قالب الشهادة: الإنتاج ZATCA، المحاكاة PREZATCA، وبوابة المطورين TSTZATCA.</summary>
    private static byte[] TemplateName(ZatcaEnvironment environment)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.WriteCharacterString(UniversalTagNumber.UTF8String, environment switch
        {
            ZatcaEnvironment.Production => "ZATCA-Code-Signing",
            ZatcaEnvironment.Simulation => "PREZATCA-Code-Signing",
            _ => "TSTZATCA-Code-Signing",
        });
        return writer.Encode();
    }

    /// <summary>subjectAltName = dirName: SN, UID, title, registeredAddress, businessCategory.</summary>
    private static byte[] DirectoryName(ZatcaCsrInput input)
    {
        var name = new X500DistinguishedNameBuilder();
        name.Add(SurnameOid, input.SerialNumber, UniversalTagNumber.UTF8String);
        name.Add(UserIdOid, input.VatNumber, UniversalTagNumber.UTF8String);
        name.Add(TitleOid, input.InvoiceTypes, UniversalTagNumber.UTF8String);
        name.Add(RegisteredAddressOid, input.RegisteredAddress, UniversalTagNumber.UTF8String);
        name.Add(BusinessCategoryOid, input.BusinessCategory, UniversalTagNumber.UTF8String);

        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 4))) // directoryName [4] EXPLICIT Name
            writer.WriteEncodedValue(name.Build().RawData);
        return writer.Encode();
    }
}
