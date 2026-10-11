namespace ERP.Service.Services.Shared;

/// <summary>
/// توقيت المملكة (UTC+3 بلا توقيت صيفي): تاريخ ووقت إصدار الفاتورة يُحفظان ويُطبعان به،
/// ويُحوَّلان إلى التوقيت العالمي عند ترميزهما في رمز QR.
/// </summary>
public static class SaudiTime
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(3);

    public static DateTime Now => DateTime.SpecifyKind(DateTime.UtcNow + Offset, DateTimeKind.Unspecified);

    public static DateTime ToUtc(DateTime saudiLocal) => DateTime.SpecifyKind(saudiLocal - Offset, DateTimeKind.Utc);
}
