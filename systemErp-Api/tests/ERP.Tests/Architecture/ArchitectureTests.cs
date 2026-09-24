using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ERP.Core.Models.Shared;
using ERP.Service.Data;

namespace ERP.Tests;

/// <summary>قواعد المعمارية ذات المشاريع الثلاثة: Api → Service → Core، والقيود المشتركة.</summary>
public class ArchitectureTests
{
    private static readonly Assembly Core = typeof(BaseEntity).Assembly;
    private static readonly Assembly Service = typeof(ErpDbContext).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;

    private static IEnumerable<string> References(Assembly a) => a.GetReferencedAssemblies().Select(r => r.Name!);

    [Fact]
    public void Core_depends_on_neither_Service_nor_Api_nor_EF()
    {
        var refs = References(Core).ToList();
        Assert.DoesNotContain("ERP.Service", refs);
        Assert.DoesNotContain("ERP.Api", refs);
        Assert.DoesNotContain(refs, r => r.StartsWith("Microsoft.EntityFrameworkCore"));
    }

    [Fact]
    public void Service_does_not_depend_on_Api()
    {
        Assert.DoesNotContain("ERP.Api", References(Service));
        Assert.Contains("ERP.Core", References(Service));
    }

    [Fact]
    public void Solution_has_exactly_three_application_assemblies()
    {
        var erp = new[] { Core, Service, Api }.Select(a => a.GetName().Name).ToList();
        Assert.Equal(new[] { "ERP.Core", "ERP.Service", "ERP.Api" }, erp);
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name!).Where(n => n.StartsWith("ERP.") && n != "ERP.Tests");
        Assert.All(loaded, n => Assert.Contains(n, erp));
    }

    [Fact]
    public void Controllers_do_not_touch_the_database_context()
    {
        var offenders = Api.GetTypes().Where(t => typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(t))
            .Where(t => t.GetConstructors().SelectMany(c => c.GetParameters()).Any(p => typeof(Microsoft.EntityFrameworkCore.DbContext).IsAssignableFrom(p.ParameterType)))
            .Select(t => t.Name).ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void Every_tenant_owned_entity_has_a_tenant_filter_and_a_tenant_index()
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseSqlServer("Server=.;Database=x;Trusted_Connection=True;TrustServerCertificate=True;").Options;
        using var ctx = new ErpDbContext(options, new ERP.Service.Services.Shared.TenantContext());
        var entities = ctx.Model.GetEntityTypes().Where(e => typeof(BaseEntity).IsAssignableFrom(e.ClrType) && !e.IsOwned()).ToList();
        Assert.NotEmpty(entities);
        Assert.All(entities, e =>
        {
            Assert.True(e.GetQueryFilter() != null, $"{e.ClrType.Name} بلا مرشّح منشأة");
            Assert.Contains(e.GetIndexes(), i => i.Properties.First().Name == nameof(BaseEntity.TenantId));
        });
    }

    [Fact]
    public void Shared_entities_are_not_duplicated_per_domain()
    {
        var names = Core.GetTypes().Where(t => typeof(BaseEntity).IsAssignableFrom(t) && !t.IsAbstract).Select(t => t.Name).ToList();
        foreach (var shared in new[] { "Customer", "Supplier", "BankEntity", "PaymentMethodItem" })
        {
            Assert.Single(names, n => n == shared);
            Assert.DoesNotContain(names, n => n.EndsWith(shared) && n != shared && (n.StartsWith("Pos") || n.StartsWith("Car") || n.StartsWith("Accounting")));
        }
        Assert.DoesNotContain(names, n => n.Contains("Product") && n.StartsWith("Vehicle")); // السيارة ليست صنفاً
    }
}
