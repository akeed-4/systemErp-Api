using System.Linq.Expressions;
using System.Reflection;
using Erp.BuildingBlocks.Infrastructure.Security;
using Erp.BuildingBlocks.Infrastructure.Tenancy;
using Erp.SharedKernel.Domain;
using Erp.SharedKernel.Security;
using Erp.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Erp.BuildingBlocks.Infrastructure.Persistence;

/// <summary>Scoped services every module DbContext needs. Bundled so module contexts have one extra constructor parameter.</summary>
public sealed class ModuleDbContextDependencies(
    ITenantContext tenant,
    IPlatformScope platform,
    ICurrentUser currentUser,
    IUnitOfWork? unitOfWork)
{
    /// <summary>Used by design-time factories and model inspection: no tenant, no platform scope, no unit of work.</summary>
    public static ModuleDbContextDependencies DesignTime { get; } =
        new(new TenantContext(), new PlatformScope(NullLogger<PlatformScope>.Instance), new SystemCurrentUser(), null);

    public ITenantContext Tenant { get; } = tenant;

    public IPlatformScope Platform { get; } = platform;

    public ICurrentUser CurrentUser { get; } = currentUser;

    public IUnitOfWork? UnitOfWork { get; } = unitOfWork;
}

/// <summary>
/// Base for every module's DbContext. Owns one schema, joins the scope's unit of work, and applies the tenant
/// filter (TenantId == current tenant, plus !IsDeleted for soft-deletable rows) to every ITenantScoped entity.
/// With no resolved tenant the filter matches nothing; only an explicit platform scope bypasses it.
/// </summary>
public abstract class ModuleDbContext : DbContext
{
    private static readonly MethodInfo ApplyTenantFilterMethod =
        typeof(ModuleDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    protected ModuleDbContext(DbContextOptions options, ModuleDbContextDependencies dependencies)
        : base(options)
    {
        Dependencies = dependencies;
        dependencies.UnitOfWork?.Enlist(this);
    }

    public abstract string Schema { get; }

    internal ModuleDbContextDependencies Dependencies { get; }

    // Read by the query filter expression; EF parameterizes these per query.
    protected Guid CurrentTenantId => Dependencies.Tenant.IsResolved ? Dependencies.Tenant.TenantId : Guid.Empty;

    protected bool IsPlatformScope => Dependencies.Platform.IsActive;

    /// <summary>
    /// EF's automatic single-column FK indexes would not start with TenantId; they are replaced by
    /// (TenantId, fk columns) indexes in <see cref="OnModelCreating"/>.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Conventions.Remove<ForeignKeyIndexConvention>();
    }

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        ConfigureModel(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            // Ids are assigned in code (version-7 GUIDs). Declaring them "never generated" makes EF treat new children
            // reached through a navigation (invoice lines, journal lines…) as inserts instead of updates.
            if (entityType.FindPrimaryKey() is { Properties: [{ ClrType: var keyType } key] } && keyType == typeof(Guid))
            {
                key.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
            }

            if (entityType.BaseType is null && typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
                AddTenantLedForeignKeyIndexes(modelBuilder, entityType);
                ApplyTenantFilterMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
            }
        }

        modelBuilder.UseSnakeCaseEnums();
    }

    private static void AddTenantLedForeignKeyIndexes(ModelBuilder modelBuilder, IMutableEntityType entityType)
    {
        foreach (var foreignKey in entityType.GetForeignKeys().ToList())
        {
            string[] wanted = [nameof(ITenantScoped.TenantId), .. foreignKey.Properties.Select(p => p.Name)];
            var covered = entityType.GetIndexes().Any(i =>
                i.Properties.Count >= wanted.Length
                && i.Properties.Take(wanted.Length).Select(p => p.Name).SequenceEqual(wanted));
            if (!covered)
            {
                modelBuilder.Entity(entityType.ClrType).HasIndex(wanted);
            }
        }
    }

    protected abstract void ConfigureModel(ModelBuilder modelBuilder);

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantScoped
    {
        var entity = modelBuilder.Entity<TEntity>();
        entity.Property(e => e.TenantId).IsRequired();

        // Every tenant table needs at least one index led by TenantId; add one if the module did not.
        var hasTenantLedIndex = entity.Metadata.GetIndexes()
            .Any(i => i.Properties[0].Name == nameof(ITenantScoped.TenantId));
        if (!hasTenantLedIndex)
        {
            entity.HasIndex(e => e.TenantId);
        }

        Expression<Func<TEntity, bool>> filter = typeof(ISoftDeletable).IsAssignableFrom(typeof(TEntity))
            ? e => (IsPlatformScope || e.TenantId == CurrentTenantId) && !EF.Property<bool>(e, nameof(ISoftDeletable.IsDeleted))
            : e => IsPlatformScope || e.TenantId == CurrentTenantId;

        entity.HasQueryFilter(filter);
    }
}
