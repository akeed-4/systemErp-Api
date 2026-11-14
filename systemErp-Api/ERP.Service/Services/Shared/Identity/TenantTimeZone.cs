using ERP.Service.Data;

namespace ERP.Service.Services.Shared;

/// <summary>المنطقة الزمنية للمنشأة (<see cref="Tenant.TimeZoneId"/>)، ومنها اليوم والوقت المحليان.</summary>
public static class TenantTimeZone
{
    public const string Default = "Asia/Riyadh";

    /// <summary>معرّف IANA (أو Windows) معروف على الخادم.</summary>
    public static bool IsValid(string? id) => !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id.Trim(), out _);

    public static TimeZoneInfo Find(string? id)
        => TimeZoneInfo.TryFindSystemTimeZoneById(string.IsNullOrWhiteSpace(id) ? Default : id.Trim(), out var zone) ? zone
            : TimeZoneInfo.TryFindSystemTimeZoneById(Default, out var fallback) ? fallback : TimeZoneInfo.Utc;

    public static async Task<TimeZoneInfo> ForCurrentTenantAsync(ErpDbContext db, CancellationToken ct)
        => Find(await db.Set<Tenant>().AsNoTracking().Where(t => t.Id == db.CurrentTenantId).Select(t => t.TimeZoneId).FirstOrDefaultAsync(ct));

    public static DateTime ToLocal(DateTime utc, TimeZoneInfo zone) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
}
