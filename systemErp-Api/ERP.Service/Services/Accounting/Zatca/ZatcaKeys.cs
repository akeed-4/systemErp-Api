using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;

namespace ERP.Service.Services.Accounting;

/// <summary>
/// تحميل مفتاح الوحدة الخاص (ECDSA) من نص PEM دون استيراد PKCS#8 في CNG.
/// <para>
/// على ويندوز يمرّر <c>ECDsa.ImportFromPem</c> / <c>ImportPkcs8PrivateKey</c> الكتلة إلى مزوّد تخزين المفاتيح
/// (<c>CngKey.Import</c> بصيغة PKCS#8)، وهو يتطلب ملف تعريف مستخدم محمّلاً. تحت IIS بلا «Load User Profile»
/// (الاستضافة المشتركة) يفشل بـ «The profile for the user is a temporary profile» بعد أن تكون الهيئة قد أصدرت الشهادة.
/// هنا تُفك البنية بشيفرة مُدارة (<see cref="Pkcs8PrivateKeyInfo"/> و <see cref="AsnReader"/>) ثم يُنشأ مفتاح
/// مؤقت في الذاكرة بـ <see cref="ECAlgorithm.ImportParameters"/>، كما يُنشأ المفتاح عند التوليد وهو يعمل على الخادم.
/// </para>
/// </summary>
public static class ZatcaKeys
{
    private const string EcPublicKeyOid = "1.2.840.10045.2.1";
    private const string Secp256k1Oid = "1.3.132.0.10";
    private const string NistP256Oid = "1.2.840.10045.3.1.7";

    /// <summary>
    /// يقبل «PRIVATE KEY» (PKCS#8، كما يُحفظ مفتاح الوحدة) و«EC PRIVATE KEY» (SEC1). المفتاح الناتج على عاتق المستدعي
    /// (<c>using</c>). يرمي <see cref="CryptographicException"/> لأي نص غير مفتاح ECDSA صالح.
    /// </summary>
    public static ECDsa ImportPrivateKey(string pem)
    {
        if (string.IsNullOrWhiteSpace(pem) || !PemEncoding.TryFind(pem, out var fields))
            throw new CryptographicException("The private key is not a PEM document.");

        var label = pem[fields.Label];
        var der = Convert.FromBase64String(pem[fields.Base64Data]);
        try
        {
            return label switch
            {
                "PRIVATE KEY" => FromPkcs8(der),
                "EC PRIVATE KEY" => FromEcPrivateKey(der, curveOid: null),
                _ => throw new CryptographicException($"Unsupported private key PEM label '{label}'."),
            };
        }
        finally { CryptographicOperations.ZeroMemory(der); }
    }

    private static ECDsa FromPkcs8(byte[] der)
    {
        Pkcs8PrivateKeyInfo info;
        try { info = Pkcs8PrivateKeyInfo.Decode(der, out _, skipCopy: true); }
        catch (Exception ex) when (ex is AsnContentException or CryptographicException)
        { throw new CryptographicException("The private key is not a valid PKCS#8 structure.", ex); }

        if (info.AlgorithmId.Value != EcPublicKeyOid)
            throw new CryptographicException($"The private key algorithm '{info.AlgorithmId.Value}' is not ECDSA.");

        string? curveOid = null;
        if (info.AlgorithmParameters is { Length: > 0 } parameters)
            curveOid = new AsnReader(parameters, AsnEncodingRules.DER).ReadObjectIdentifier();
        return FromEcPrivateKey(info.PrivateKeyBytes, curveOid);
    }

    /// <summary>ECPrivateKey (RFC 5915): الإصدار 1، القيمة السرية، ثم المنحنى [0] والمفتاح العام [1] اختياريان.</summary>
    private static ECDsa FromEcPrivateKey(ReadOnlyMemory<byte> der, string? curveOid)
    {
        byte[] d;
        byte[]? publicPoint = null;
        try
        {
            var outer = new AsnReader(der, AsnEncodingRules.DER);
            var sequence = outer.ReadSequence();
            outer.ThrowIfNotEmpty();
            if (!sequence.TryReadInt32(out var version) || version != 1)
                throw new CryptographicException("Unsupported ECPrivateKey version.");
            d = sequence.ReadOctetString();

            var curveTag = new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true);
            if (sequence.HasData && sequence.PeekTag().HasSameClassAndValue(curveTag))
            {
                var embedded = sequence.ReadSequence(curveTag).ReadObjectIdentifier();
                if (curveOid != null && curveOid != embedded) throw new CryptographicException("The private key curve parameters are inconsistent.");
                curveOid = embedded;
            }
            var publicTag = new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true);
            if (sequence.HasData && sequence.PeekTag().HasSameClassAndValue(publicTag))
                publicPoint = sequence.ReadSequence(publicTag).ReadBitString(out _);
        }
        catch (AsnContentException ex) { throw new CryptographicException("The private key is not a valid ECPrivateKey structure.", ex); }

        if (curveOid == null) throw new CryptographicException("The private key does not name its curve.");
        var curve = curveOid switch
        {
            Secp256k1Oid => ECCurve.CreateFromFriendlyName("secp256k1"),
            NistP256Oid => ECCurve.NamedCurves.nistP256,
            _ => ECCurve.CreateFromValue(curveOid),
        };

        var size = d.Length;
        var parameters = new ECParameters { Curve = curve, D = d };
        // النقطة العامة غير مضغوطة: 04 || X || Y. إن غابت تشتقها المكتبة من القيمة السرية
        if (publicPoint is { Length: > 1 } && publicPoint[0] == 0x04 && (publicPoint.Length - 1) % 2 == 0)
        {
            var half = (publicPoint.Length - 1) / 2;
            if (half != size) throw new CryptographicException("The private key public point does not match its size.");
            parameters.Q = new ECPoint { X = publicPoint[1..(1 + half)], Y = publicPoint[(1 + half)..] };
        }
        else if (publicPoint != null)
            throw new CryptographicException("Only uncompressed EC public points are supported.");

        var key = ECDsa.Create();
        try
        {
            key.ImportParameters(parameters);
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
        finally { CryptographicOperations.ZeroMemory(d); }
    }
}
