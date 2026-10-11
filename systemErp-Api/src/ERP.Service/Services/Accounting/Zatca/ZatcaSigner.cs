using System.Formats.Asn1;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;

namespace ERP.Service.Services.Accounting;

/// <summary>الفاتورة بعد توقيعها: الوثيقة النهائية وتجزئتها (التي تصير «السابقة» للفاتورة التالية) ورمز QR.</summary>
public sealed record ZatcaSignedInvoice(string Xml, string Hash, string Qr, Guid Uuid);

/// <summary>
/// ختم التشفير للفاتورة وفق معايير الهيئة الأمنية: تجزئة الوثيقة (SHA-256 بعد التقنين C14N)، توقيعها بمفتاح الوحدة
/// (ECDSA)، كتلة XAdES في UBLExtensions، ورمز QR بوسومه التسعة.
/// </summary>
public static class ZatcaSigner
{
    private const string XadesNs = "http://uri.etsi.org/01903/v1.3.2#";
    private const string DsNs = "http://www.w3.org/2000/09/xmldsig#";
    private const string Sha256Uri = "http://www.w3.org/2001/04/xmlenc#sha256";
    private const string C14N11 = "http://www.w3.org/2006/12/xml-c14n11";
    private const string XPath = "http://www.w3.org/TR/1999/REC-xpath-19991116";

    /// <param name="certificateBase64">شهادة الوحدة (DER مرمَّزة Base64 بلا ترويسة PEM).</param>
    /// <param name="signedAt">وقت التوقيع بالتوقيت العالمي.</param>
    public static ZatcaSignedInvoice Sign(ZatcaDocument document, string certificateBase64, ECDsa key, DateTime signedAt)
    {
        var hash = Hash(ZatcaInvoiceXml.Build(document));
        var hashBase64 = Convert.ToBase64String(hash);
        var signature = Convert.ToBase64String(key.SignData(hash, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));

        var certificateText = certificateBase64.Trim();
        using var certificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(certificateText));
        var properties = new SignedProperties(
            signedAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            HexDigestBase64(certificateText),
            certificate.Issuer,
            new BigInteger(certificate.SerialNumberBytes.Span, isUnsigned: true, isBigEndian: true).ToString(CultureInfo.InvariantCulture));

        var net = document.Lines.Sum(l => l.Net);
        var vat = document.Lines.Sum(l => l.VatAmount);
        var qr = Qr(document.Seller.Name, document.Seller.VatNumber ?? string.Empty,
            $"{document.IssueDate:yyyy-MM-dd}T{document.IssueTime}", Money(net + vat), Money(vat),
            hashBase64, signature, certificate.PublicKey.ExportSubjectPublicKeyInfo(),
            document.IsSimplified ? CertificateSignature(certificate.RawData) : null);

        var extensions = UblExtensions(hashBase64, HexDigestBase64(properties.ForHashing()), signature, certificateText, properties.Embedded());
        return new ZatcaSignedInvoice(ZatcaInvoiceXml.Build(document, new ZatcaSignatureParts(extensions, qr)), hashBase64, qr, document.Uuid);
    }

    /// <summary>
    /// تجزئة الفاتورة: تُحذف عناصر التوقيع (UBLExtensions و cac:Signature ومرجع QR) ثم تُقنَّن الوثيقة وتُجزَّأ.
    /// تعمل على الوثيقة الموقّعة وغير الموقّعة، فتُستخدم أيضاً للتحقق من فاتورة محفوظة.
    /// </summary>
    public static byte[] Hash(string xml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("i", "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2");
        ns.AddNamespace("cac", "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2");
        ns.AddNamespace("cbc", "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2");
        ns.AddNamespace("ext", "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2");
        foreach (var path in new[] { "/i:Invoice/ext:UBLExtensions", "/i:Invoice/cac:Signature", "/i:Invoice/cac:AdditionalDocumentReference[cbc:ID='QR']" })
            foreach (var node in doc.SelectNodes(path, ns)!.Cast<XmlNode>().ToList())
                node.ParentNode!.RemoveChild(node);

        var transform = new XmlDsigC14NTransform();
        transform.LoadInput(doc);
        using var canonical = (Stream)transform.GetOutput(typeof(Stream));
        return SHA256.HashData(canonical);
    }

    /// <summary>وسوم QR للمرحلة الثانية: 1–5 بيانات الفاتورة، 6 تجزئتها، 7 التوقيع، 8 المفتاح العام، 9 توقيع الشهادة (للمبسطة).</summary>
    private static string Qr(string seller, string vat, string timestamp, string total, string vatTotal, string hash, string signature, byte[] publicKey, byte[]? certificateSignature)
    {
        var bytes = new List<byte>();
        void Add(int tag, byte[] value)
        {
            if (value.Length > 255) throw new InvalidOperationException($"QR tag {tag} exceeds 255 bytes.");
            bytes.Add((byte)tag);
            bytes.Add((byte)value.Length);
            bytes.AddRange(value);
        }
        var sellerBytes = Encoding.UTF8.GetBytes(seller);
        Add(1, sellerBytes.Length > 255 ? Encoding.UTF8.GetBytes(TrimToBytes(seller, 255)) : sellerBytes);
        Add(2, Encoding.UTF8.GetBytes(vat));
        Add(3, Encoding.UTF8.GetBytes(timestamp));
        Add(4, Encoding.UTF8.GetBytes(total));
        Add(5, Encoding.UTF8.GetBytes(vatTotal));
        Add(6, Encoding.UTF8.GetBytes(hash));
        Add(7, Encoding.UTF8.GetBytes(signature));
        Add(8, publicKey);
        if (certificateSignature != null) Add(9, certificateSignature);
        return Convert.ToBase64String(bytes.ToArray());
    }

    private static string TrimToBytes(string value, int maxBytes)
    {
        while (Encoding.UTF8.GetByteCount(value) > maxBytes) value = value[..^1];
        return value;
    }

    /// <summary>توقيع جهة الإصدار على الشهادة نفسها: العنصر الثالث في بنية Certificate (BIT STRING).</summary>
    private static byte[] CertificateSignature(byte[] der)
    {
        var certificate = new AsnReader(der, AsnEncodingRules.DER).ReadSequence();
        certificate.ReadEncodedValue(); // tbsCertificate
        certificate.ReadEncodedValue(); // signatureAlgorithm
        return certificate.ReadBitString(out _);
    }

    /// <summary>صيغة الهيئة للتجزئات داخل XAdES: SHA-256 مكتوبة ست عشرية ثم مرمَّزة Base64.</summary>
    private static string HexDigestBase64(string text)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant()));

    private static string Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    private static string Escape(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;

    /// <summary>
    /// خصائص التوقيع (SignedProperties). مدقِّق الهيئة يجزّئها بتنسيق مسافات محدد وبإعلان xmlns:ds على كل عنصر ds،
    /// فالنص المجزَّأ هنا يطابق ذلك التنسيق حرفياً، والنص المضمَّن في الوثيقة يحمل المسافات نفسها.
    /// </summary>
    private sealed record SignedProperties(string SigningTime, string CertificateDigest, string Issuer, string SerialNumber)
    {
        public string ForHashing() => Render($" xmlns:xades=\"{XadesNs}\"", $" xmlns:ds=\"{DsNs}\"");
        public string Embedded() => Render(string.Empty, string.Empty);

        private string Render(string xades, string ds)
        {
            var p = "\n" + new string(' ', 36);
            return $"<xades:SignedProperties{xades} Id=\"xadesSignedProperties\">"
                + $"{p}<xades:SignedSignatureProperties>"
                + $"{p}    <xades:SigningTime>{SigningTime}</xades:SigningTime>"
                + $"{p}    <xades:SigningCertificate>"
                + $"{p}        <xades:Cert>"
                + $"{p}            <xades:CertDigest>"
                + $"{p}                <ds:DigestMethod{ds} Algorithm=\"{Sha256Uri}\"/>"
                + $"{p}                <ds:DigestValue{ds}>{CertificateDigest}</ds:DigestValue>"
                + $"{p}            </xades:CertDigest>"
                + $"{p}            <xades:IssuerSerial>"
                + $"{p}                <ds:X509IssuerName{ds}>{Escape(Issuer)}</ds:X509IssuerName>"
                + $"{p}                <ds:X509SerialNumber{ds}>{SerialNumber}</ds:X509SerialNumber>"
                + $"{p}            </xades:IssuerSerial>"
                + $"{p}        </xades:Cert>"
                + $"{p}    </xades:SigningCertificate>"
                + $"{p}</xades:SignedSignatureProperties>"
                + "\n" + new string(' ', 32) + "</xades:SignedProperties>";
        }
    }

    private static string UblExtensions(string invoiceHash, string propertiesHash, string signature, string certificate, string signedProperties)
        => "<ext:UBLExtensions><ext:UBLExtension>"
            + "<ext:ExtensionURI>urn:oasis:names:specification:ubl:dsig:enveloped:xades</ext:ExtensionURI>"
            + "<ext:ExtensionContent>"
            + "<sig:UBLDocumentSignatures xmlns:sig=\"urn:oasis:names:specification:ubl:schema:xsd:CommonSignatureComponents-2\""
            + " xmlns:sac=\"urn:oasis:names:specification:ubl:schema:xsd:SignatureAggregateComponents-2\""
            + " xmlns:sbc=\"urn:oasis:names:specification:ubl:schema:xsd:SignatureBasicComponents-2\">"
            + "<sac:SignatureInformation>"
            + "<cbc:ID>urn:oasis:names:specification:ubl:signature:1</cbc:ID>"
            + "<sbc:ReferencedSignatureID>urn:oasis:names:specification:ubl:signature:Invoice</sbc:ReferencedSignatureID>"
            + $"<ds:Signature xmlns:ds=\"{DsNs}\" Id=\"signature\">"
            + "<ds:SignedInfo>"
            + $"<ds:CanonicalizationMethod Algorithm=\"{C14N11}\"/>"
            + "<ds:SignatureMethod Algorithm=\"http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256\"/>"
            + "<ds:Reference Id=\"invoiceSignedData\" URI=\"\">"
            + "<ds:Transforms>"
            + $"<ds:Transform Algorithm=\"{XPath}\"><ds:XPath>not(//ancestor-or-self::ext:UBLExtensions)</ds:XPath></ds:Transform>"
            + $"<ds:Transform Algorithm=\"{XPath}\"><ds:XPath>not(//ancestor-or-self::cac:Signature)</ds:XPath></ds:Transform>"
            + $"<ds:Transform Algorithm=\"{XPath}\"><ds:XPath>not(//ancestor-or-self::cac:AdditionalDocumentReference[cbc:ID='QR'])</ds:XPath></ds:Transform>"
            + $"<ds:Transform Algorithm=\"{C14N11}\"/>"
            + "</ds:Transforms>"
            + $"<ds:DigestMethod Algorithm=\"{Sha256Uri}\"/>"
            + $"<ds:DigestValue>{invoiceHash}</ds:DigestValue>"
            + "</ds:Reference>"
            + "<ds:Reference Type=\"http://www.w3.org/2000/09/xmldsig#SignatureProperties\" URI=\"#xadesSignedProperties\">"
            + $"<ds:DigestMethod Algorithm=\"{Sha256Uri}\"/>"
            + $"<ds:DigestValue>{propertiesHash}</ds:DigestValue>"
            + "</ds:Reference>"
            + "</ds:SignedInfo>"
            + $"<ds:SignatureValue>{signature}</ds:SignatureValue>"
            + $"<ds:KeyInfo><ds:X509Data><ds:X509Certificate>{certificate}</ds:X509Certificate></ds:X509Data></ds:KeyInfo>"
            + "<ds:Object>"
            + $"<xades:QualifyingProperties xmlns:xades=\"{XadesNs}\" Target=\"signature\">"
            + "\n" + new string(' ', 32) + signedProperties // 32 مسافة قبل العنصر كما يتوقعها المدقِّق
            + "\n" + new string(' ', 28) + "</xades:QualifyingProperties>"
            + "</ds:Object>"
            + "</ds:Signature>"
            + "</sac:SignatureInformation>"
            + "</sig:UBLDocumentSignatures>"
            + "</ext:ExtensionContent>"
            + "</ext:UBLExtension></ext:UBLExtensions>";
}
