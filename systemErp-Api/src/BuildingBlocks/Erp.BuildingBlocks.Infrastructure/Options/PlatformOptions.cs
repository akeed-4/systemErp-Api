namespace Erp.BuildingBlocks.Infrastructure.Options;

/// <summary>Section "Tenancy" plus ConnectionStrings:Catalog and ConnectionStrings:Shared.</summary>
public sealed class TenancyOptions
{
    public const string SectionName = "Tenancy";

    public string CatalogConnectionString { get; set; } = string.Empty;

    public string SharedConnectionString { get; set; } = string.Empty;

    /// <summary>Connection string for dedicated tenant databases, with a {database} placeholder.</summary>
    public string DedicatedConnectionTemplate { get; set; } = string.Empty;

    /// <summary>Dedicated database name = prefix + tenant code (sanitized).</summary>
    public string DedicatedDatabasePrefix { get; set; } = "ErpTenant_";

    public int TenantCacheSeconds { get; set; } = 60;
}

/// <summary>Section "Jwt". Shared by the token issuer (Identity) and the validator (API host).</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "SystemErp";

    public string Audience { get; set; } = "SystemErp.Web";

    /// <summary>HMAC-SHA256 key, at least 32 bytes. Must come from secrets/Key Vault outside development.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 60;

    public int RefreshTokenDays { get; set; } = 14;
}

/// <summary>Section "Outbox".</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public bool Enabled { get; set; } = true;

    public int PollSeconds { get; set; } = 5;

    public int BatchSize { get; set; } = 100;

    public int MaxAttempts { get; set; } = 10;
}
