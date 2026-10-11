using ERP.Core.Contracts.Accounting;
using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

internal static class PartyValidation
{
    public const string Saudi = "SA";

    /// <summary>رمز الدولة بحرفين كبيرين؛ الفارغ = السعودية.</summary>
    public static string NormalizeCountry(string? country)
        => string.IsNullOrWhiteSpace(country) ? Saudi : country.Trim().ToUpperInvariant();

    public static void Validate(string nameAr, string? vat, string? email, decimal opening, List<string> extra, string? country = null)
    {
        var errors = new List<string>(extra);
        if (string.IsNullOrWhiteSpace(nameAr)) errors.Add(Messages.ArabicNameRequired);
        // صيغة الرقم الضريبي السعودي تخص الطرف المحلي؛ الطرف الأجنبي رقمه بصيغة دولته
        if (NormalizeCountry(country) == Saudi && !string.IsNullOrWhiteSpace(vat) && !SaudiVat.IsValid(vat))
            errors.Add(Messages.VatNumberFormat);
        if (!string.IsNullOrWhiteSpace(email) && !email.Contains('@')) errors.Add(Messages.EmailInvalid);
        if (errors.Count > 0) throw new ValidationFailedException(errors[0], errors);
    }

    /// <summary>
    /// العنوان الوطني كما تطلبه الهيئة: رمز الدولة حرفان، وللعنوان السعودي رقم المبنى 4 أرقام والرمز البريدي 5 والرقم الإضافي 4.
    /// الحقول اختيارية في بطاقة الطرف (تُلزَم عند إصدار الفاتورة الضريبية)، لكن ما أُدخل منها يجب أن يكون بصيغته.
    /// </summary>
    public static void AddAddressErrors(List<string> errors, string? country, string? buildingNo, string? postalCode, string? additionalNo)
    {
        var code = NormalizeCountry(country);
        if (code.Length != 2 || !code.All(char.IsAsciiLetter)) { errors.Add(Messages.AddressCountryCodeInvalid); return; }
        if (code != Saudi) return;
        if (!IsDigitsOrEmpty(buildingNo, 4)) errors.Add(Messages.AddressBuildingNoInvalid);
        if (!IsDigitsOrEmpty(postalCode, 5)) errors.Add(Messages.AddressPostalCodeInvalid);
        if (!IsDigitsOrEmpty(additionalNo, 4)) errors.Add(Messages.AddressAdditionalNoInvalid);
    }

    private static bool IsDigitsOrEmpty(string? value, int length)
        => string.IsNullOrWhiteSpace(value) || (value.Trim().Length == length && value.Trim().All(char.IsAsciiDigit));
}
