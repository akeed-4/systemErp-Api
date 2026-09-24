using System.Reflection;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Erp.ArchitectureTests;

/// <summary>
/// Isolation rules checked on the EF models of every tenant-database context (no database needed):
/// every entity is tenant-scoped or global reference data, tenant tables are filtered, every index starts with
/// TenantId and every unique constraint includes it, and each context stays inside its own schema.
/// </summary>
public sealed class TenantModelRules
{
    public static TheoryData<string> Contexts()
    {
        var data = new TheoryData<string>();
        foreach (var type in ContextTypes())
        {
            data.Add(type.FullName!);
        }

        return data;
    }

    [Fact]
    public void Every_module_implementation_has_exactly_one_tenant_database_context()
    {
        var owners = ContextTypes().Select(t => t.Assembly.GetName().Name).ToList();
        Assert.Contains("Erp.BuildingBlocks.Infrastructure", owners);
        Assert.All(
            SolutionAssemblies.OfKind(AssemblyKind.ModuleImplementation),
            module => Assert.Single(owners, o => o == module.Name));
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Every_guid_key_is_assigned_by_the_application(string contextName)
    {
        using var context = Create(contextName);
        var violations = context.Model.GetEntityTypes()
            .Select(e => e.FindPrimaryKey()!)
            .Where(k => k.Properties is [{ ClrType: var t }] && t == typeof(Guid) && k.Properties[0].ValueGenerated != ValueGenerated.Never)
            .Select(k => k.DeclaringEntityType.ClrType.Name)
            .ToList();

        Assert.Empty(violations);
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Every_entity_is_tenant_scoped_or_global_reference_data(string contextName)
    {
        using var context = Create(contextName);
        var violations = context.Model.GetEntityTypes()
            .Where(e => !typeof(ITenantScoped).IsAssignableFrom(e.ClrType) && !e.ClrType.IsDefined(typeof(GlobalReferenceDataAttribute), false))
            .Select(e => e.ClrType.Name)
            .ToList();

        Assert.Empty(violations);
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Tenant_entities_have_a_tenant_query_filter(string contextName)
    {
        using var context = Create(contextName);
        var violations = TenantEntities(context).Where(e => e.GetQueryFilter() is null).Select(e => e.ClrType.Name).ToList();
        Assert.Empty(violations);
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Every_index_of_a_tenant_table_starts_with_TenantId(string contextName)
    {
        using var context = Create(contextName);
        var violations = TenantEntities(context)
            .SelectMany(e => e.GetIndexes().Where(i => i.Properties[0].Name != nameof(ITenantScoped.TenantId)).Select(i => $"{e.ClrType.Name}({string.Join(",", i.Properties.Select(p => p.Name))})"))
            .ToList();

        Assert.Empty(violations);
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Every_unique_constraint_of_a_tenant_table_includes_TenantId(string contextName)
    {
        using var context = Create(contextName);
        var violations = TenantEntities(context)
            .SelectMany(e => e.GetIndexes().Where(i => i.IsUnique).Select(i => (Entity: e, Properties: i.Properties))
                .Concat(e.GetKeys().Where(k => !k.IsPrimaryKey()).Select(k => (Entity: e, Properties: k.Properties))))
            .Where(x => x.Properties.All(p => p.Name != nameof(ITenantScoped.TenantId)))
            .Select(x => $"{x.Entity.ClrType.Name}({string.Join(",", x.Properties.Select(p => p.Name))})")
            .ToList();

        Assert.Empty(violations);
    }

    [Theory]
    [MemberData(nameof(Contexts))]
    public void Every_table_lives_in_the_context_schema(string contextName)
    {
        using var context = Create(contextName);
        var schema = ((ModuleDbContext)context).Schema;
        var violations = context.Model.GetEntityTypes().Where(e => e.GetSchema() != schema).Select(e => $"{e.ClrType.Name} in {e.GetSchema()}").ToList();
        Assert.Empty(violations);
    }

    private static IEnumerable<IEntityType> TenantEntities(DbContext context) =>
        context.Model.GetEntityTypes().Where(e => typeof(ITenantScoped).IsAssignableFrom(e.ClrType));

    private static IEnumerable<Type> ContextTypes() =>
        SolutionAssemblies.All
            .SelectMany(a => a.Assembly.GetTypes())
            .Where(t => !t.IsAbstract && typeof(ModuleDbContext).IsAssignableFrom(t));

    private static DbContext Create(string contextName)
    {
        var type = ContextTypes().Single(t => t.FullName == contextName);
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(typeof(DbContextOptionsBuilder<>).MakeGenericType(type))!;
        builder.UseSqlServer(DesignTimeOptions.ConnectionString);
        return (DbContext)Activator.CreateInstance(
            type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [builder.Options, ModuleDbContextDependencies.DesignTime],
            culture: null)!;
    }
}
