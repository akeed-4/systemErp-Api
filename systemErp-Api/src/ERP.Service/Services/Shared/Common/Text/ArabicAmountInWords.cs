using System.Text;

namespace ERP.Service.Services.Shared.Text;

/// <summary>
/// Saudi riyal amount in Arabic words (tafqeet), the same wording as the frontend's tafqeet.util.ts
/// ("فقط … ريالاً سعودياً و … هللة لا غير"). Millions and billions are spelled out instead of falling back to digits.
/// </summary>
public static class ArabicAmountInWords
{
    private static readonly string[] Ones =
    [
        string.Empty, "واحد", "اثنان", "ثلاثة", "أربعة", "خمسة", "ستة", "سبعة", "ثمانية", "تسعة", "عشرة",
        "أحد عشر", "اثنا عشر", "ثلاثة عشر", "أربعة عشر", "خمسة عشر", "ستة عشر", "سبعة عشر", "ثمانية عشر", "تسعة عشر",
    ];

    private static readonly string[] Tens = [string.Empty, string.Empty, "عشرون", "ثلاثون", "أربعون", "خمسون", "ستون", "سبعون", "ثمانون", "تسعون"];

    private static readonly string[] Hundreds = [string.Empty, "مائة", "مئتان", "ثلاثمائة", "أربعمائة", "خمسمائة", "ستمائة", "سبعمائة", "ثمانمائة", "تسعمائة"];

    // (one, two, plural 3-10, singular 11+)
    private static readonly (long Size, string One, string Two, string Plural, string Singular)[] Scales =
    [
        (1_000_000_000, "مليار", "ملياران", "مليارات", "مليار"),
        (1_000_000, "مليون", "مليونان", "ملايين", "مليون"),
        (1_000, "ألف", "ألفان", "آلاف", "ألف"),
    ];

    public static string Riyals(decimal amount)
    {
        if (amount == 0)
        {
            return "صفر ريال سعودي لا غير";
        }

        var negative = amount < 0;
        var absolute = Math.Abs(Math.Round(amount, 2, MidpointRounding.AwayFromZero));
        var riyals = (long)Math.Floor(absolute);
        var halalas = (int)((absolute - riyals) * 100);

        var sb = new StringBuilder();
        if (riyals > 0)
        {
            sb.Append("فقط ").Append(Integer(riyals)).Append(" ريالاً سعودياً");
        }

        if (halalas > 0)
        {
            sb.Append(sb.Length > 0 ? " و " : "فقط ").Append(Group(halalas)).Append(" هللة");
        }

        sb.Append(" لا غير");
        return (negative ? "سالب " : string.Empty) + sb;
    }

    private static string Integer(long value)
    {
        var parts = new List<string>();
        var rest = value;
        foreach (var (size, one, two, plural, singular) in Scales)
        {
            var count = rest / size;
            rest %= size;
            if (count == 0)
            {
                continue;
            }

            parts.Add(count switch
            {
                1 => one,
                2 => two,
                >= 3 and <= 10 => $"{Group((int)count)} {plural}",
                _ => $"{Integer(count)} {singular}",
            });
        }

        if (rest > 0)
        {
            parts.Add(Group((int)rest));
        }

        return string.Join(" و ", parts);
    }

    private static string Group(int n)
    {
        var output = new StringBuilder();
        var h = n / 100;
        var remainder = n % 100;
        if (h > 0)
        {
            output.Append(Hundreds[h]);
        }

        if (remainder > 0)
        {
            if (output.Length > 0)
            {
                output.Append(" و ");
            }

            if (remainder < 20)
            {
                output.Append(Ones[remainder]);
            }
            else
            {
                var o = remainder % 10;
                if (o > 0)
                {
                    output.Append(Ones[o]).Append(" و ");
                }

                output.Append(Tens[remainder / 10]);
            }
        }

        return output.ToString();
    }
}
