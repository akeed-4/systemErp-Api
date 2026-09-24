using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.BuildingBlocks.Infrastructure.Outbox;

/// <summary>One outbox row. Carries TenantId so the dispatcher can restore the tenant context before handling it.</summary>
public sealed class OutboxMessage : ITenantScoped
{
    private OutboxMessage()
    {
    }

    public OutboxMessage(string type, string payload, DateTimeOffset occurredAt)
    {
        Type = type;
        Payload = payload;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; } = Guid.CreateVersion7();

    public Guid TenantId { get; private set; }

    public string Type { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    void ITenantScoped.StampTenant(Guid tenantId) => TenantId = tenantId;
}

/// <summary>Platform tables that live in every tenant database (schema "platform"): the outbox.</summary>
internal sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options, ModuleDbContextDependencies dependencies)
    : ModuleDbContext(options, dependencies)
{
    public const string SchemaName = "platform";

    public override string Schema => SchemaName;

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("OutboxMessages");
            b.HasKey(m => m.Id);
            b.Property(m => m.Type).HasMaxLength(200).IsUnicode(false);
            b.Property(m => m.LastError).HasMaxLength(2000);
            b.HasIndex(m => new { m.TenantId, m.OccurredAt }).HasFilter("[ProcessedAt] IS NULL");
        });
    }
}

internal sealed class PlatformDbContextDesignTimeFactory : ModuleDesignTimeFactory<PlatformDbContext>
{
    protected override string Schema => PlatformDbContext.SchemaName;

    protected override PlatformDbContext Create(DbContextOptions<PlatformDbContext> options, ModuleDbContextDependencies dependencies) =>
        new(options, dependencies);
}
