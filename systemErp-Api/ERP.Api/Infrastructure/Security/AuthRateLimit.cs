namespace ERP.Api.Infrastructure;

/// <summary>سياسة حدّ المحاولات لنقاط الهوية العامة (تُضبط في Program.cs من Auth:RateLimitPerMinute).</summary>
public static class AuthRateLimit
{
    public const string Policy = "auth";
}
