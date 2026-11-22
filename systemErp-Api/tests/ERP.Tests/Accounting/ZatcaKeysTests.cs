using System.Security.Cryptography;
using ERP.Service.Services.Accounting;

namespace ERP.Tests;

/// <summary>
/// تحميل مفتاح الوحدة بـ <see cref="ZatcaKeys"/> بدل <c>ImportFromPem</c> (الذي يستورد PKCS#8 في CNG فيفشل تحت IIS
/// بلا ملف تعريف مستخدم): المفتاح المحمَّل هو نفسه، يوقّع توقيعاً يتحقق بالمفتاح الأصلي، ويرفض ما ليس مفتاح ECDSA.
/// </summary>
public class ZatcaKeysTests
{
    private static readonly byte[] Data = "zatca-key-import"u8.ToArray();

    private static void AssertSameKey(ECDsa original, ECDsa imported)
    {
        Assert.Equal(original.ExportSubjectPublicKeyInfo(), imported.ExportSubjectPublicKeyInfo());
        var signature = imported.SignData(Data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        Assert.True(original.VerifyData(Data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
    }

    [Fact]
    public void A_unit_key_saved_as_pkcs8_loads_as_the_same_secp256k1_key()
    {
        using var original = ZatcaCsr.NewKey(); // كما يُنشأ مفتاح الوحدة ويُحفظ في الجلسة
        using var imported = ZatcaKeys.ImportPrivateKey(original.ExportPkcs8PrivateKeyPem());
        AssertSameKey(original, imported);
        Assert.Equal(256, imported.KeySize);
    }

    [Fact]
    public void Sec1_and_p256_keys_are_also_supported()
    {
        using var k1 = ZatcaCsr.NewKey();
        using (var imported = ZatcaKeys.ImportPrivateKey(k1.ExportECPrivateKeyPem())) AssertSameKey(k1, imported);

        using var p256 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using (var imported = ZatcaKeys.ImportPrivateKey(p256.ExportPkcs8PrivateKeyPem())) AssertSameKey(p256, imported);
    }

    [Fact]
    public void A_key_loaded_by_ZatcaKeys_signs_documents_that_verify_like_the_old_import()
    {
        using var original = ZatcaCsr.NewKey();
        var pem = original.ExportPkcs8PrivateKeyPem();
        using var legacy = ECDsa.Create();
        legacy.ImportFromPem(pem); // الطريقة القديمة (تعمل محلياً حيث ملف التعريف محمّل)
        using var imported = ZatcaKeys.ImportPrivateKey(pem);
        Assert.Equal(legacy.ExportParameters(true).D, imported.ExportParameters(true).D);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a pem")]
    [InlineData("-----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY-----")]
    [InlineData("-----BEGIN CERTIFICATE-----\nAAAA\n-----END CERTIFICATE-----")]
    public void Anything_but_a_valid_EC_private_key_is_rejected(string pem)
        => Assert.ThrowsAny<CryptographicException>(() => ZatcaKeys.ImportPrivateKey(pem).Dispose());

    [Fact]
    public void An_rsa_key_is_rejected()
    {
        using var rsa = RSA.Create(2048);
        var ex = Assert.Throws<CryptographicException>(() => ZatcaKeys.ImportPrivateKey(rsa.ExportPkcs8PrivateKeyPem()).Dispose());
        Assert.Contains("not ECDSA", ex.Message);
    }
}
