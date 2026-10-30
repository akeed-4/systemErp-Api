namespace ERP.Service.Services.Shared;

/// <summary>
/// رقم المستلم بصيغة E.164 بلا + كما يطلبها واتساب. الرقم المحلي (05XXXXXXXX أو 5XXXXXXXX) يُكمَّل برمز المملكة، والرقم الدولي
/// يُقبل بـ + أو 00. الجوال السعودي يُتحقق من شكله؛ وغيره يُقبل بطول E.164 (8–15 رقماً). وجود الرقم على واتساب لا يُعرف إلا عند الإرسال.
/// </summary>
public static class WhatsAppPhone
{
    public const string DefaultCountryCode = "966";

    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        var international = s.StartsWith('+') || s.StartsWith("00");
        var digits = new string(s.Where(char.IsAsciiDigit).ToArray());
        if (s.Any(ch => !char.IsAsciiDigit(ch) && ch is not ('+' or ' ' or '-' or '(' or ')' or '.'))) return null;
        if (digits.StartsWith("00")) digits = digits[2..];

        if (!international)
        {
            if (digits.Length == 10 && digits.StartsWith("05")) digits = DefaultCountryCode + digits[1..];
            else if (digits.Length == 9 && digits.StartsWith('5')) digits = DefaultCountryCode + digits;
        }

        if (digits.StartsWith(DefaultCountryCode + "0")) digits = DefaultCountryCode + digits[4..]; // 9660 5...
        if (digits.StartsWith(DefaultCountryCode))
            return digits.Length == 12 && digits[3] == '5' ? digits : null;
        return digits.Length is >= 8 and <= 15 && digits[0] != '0' ? digits : null;
    }
}
