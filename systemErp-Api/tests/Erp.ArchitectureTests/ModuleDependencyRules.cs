using System.Runtime.CompilerServices;

namespace Erp.ArchitectureTests;

/// <summary>§11.2 dependency rules plus the catalog access rule, checked on compiled assembly references.</summary>
public sealed class ModuleDependencyRules
{
    private static readonly string[] CatalogContractsAllowedModules = ["Identity", "Organization"];

    [Fact]
    public void Every_module_has_an_implementation_and_a_contracts_assembly()
    {
        var modules = SolutionAssemblies.OfKind(AssemblyKind.ModuleImplementation).Select(a => a.Module).Order().ToList();
        Assert.Superset(
            new HashSet<string?> { "Identity", "Organization", "Permissions", "Settings", "Accounting", "Banking", "Customers", "Suppliers", "Payments" },
            new HashSet<string?>(modules));
        Assert.All(modules, m => Assert.Contains(SolutionAssemblies.All, a => a.Module == m && a.Kind == AssemblyKind.ModuleContracts));
    }

    [Fact]
    public void Modules_reference_other_modules_only_through_their_contracts()
    {
        var violations = new List<string>();
        foreach (var module in SolutionAssemblies.OfKind(AssemblyKind.ModuleImplementation))
        {
            foreach (var reference in module.References.Select(SolutionAssemblies.Get))
            {
                if (reference.Kind is AssemblyKind.ModuleImplementation or AssemblyKind.Host or AssemblyKind.CatalogImplementation)
                {
                    violations.Add($"{module} -> {reference}");
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void Contracts_depend_only_on_the_shared_kernel()
    {
        var violations = SolutionAssemblies.All
            .Where(a => a.Kind is AssemblyKind.ModuleContracts or AssemblyKind.CatalogContracts)
            .SelectMany(a => a.References.Where(r => SolutionAssemblies.Get(r).Kind != AssemblyKind.SharedKernel).Select(r => $"{a} -> {r}"))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Contracts_do_not_reference_entity_framework()
    {
        var violations = SolutionAssemblies.All
            .Where(a => a.Kind is AssemblyKind.ModuleContracts or AssemblyKind.CatalogContracts or AssemblyKind.SharedKernel)
            .Where(a => a.Assembly.GetReferencedAssemblies().Any(r => r.Name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)))
            .Select(a => a.Name)
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Building_blocks_do_not_depend_on_modules_or_the_catalog_implementation()
    {
        var violations = SolutionAssemblies.All
            .Where(a => a.Kind is AssemblyKind.BuildingBlock or AssemblyKind.SharedKernel)
            .SelectMany(a => a.References
                .Where(r => SolutionAssemblies.Get(r).Kind is AssemblyKind.ModuleContracts or AssemblyKind.ModuleImplementation or AssemblyKind.CatalogImplementation or AssemblyKind.Host)
                .Select(r => $"{a} -> {r}"))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Only_building_blocks_identity_and_organization_reference_the_catalog_contracts()
    {
        var violations = SolutionAssemblies.All
            .Where(a => a.References.Contains("Erp.Catalog.Contracts"))
            .Where(a => a.Kind switch
            {
                AssemblyKind.BuildingBlock or AssemblyKind.CatalogImplementation or AssemblyKind.Host => false,
                AssemblyKind.ModuleContracts or AssemblyKind.ModuleImplementation => !CatalogContractsAllowedModules.Contains(a.Module),
                _ => true,
            })
            .Select(a => a.Name)
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Only_hosts_reference_the_catalog_implementation()
    {
        var violations = SolutionAssemblies.All
            .Where(a => a.References.Contains("Erp.Catalog") && a.Kind != AssemblyKind.Host)
            .Select(a => a.Name)
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Module_dependency_graph_has_no_cycles()
    {
        var edges = SolutionAssemblies.All
            .Where(a => a.Module is not null)
            .SelectMany(a => a.References.Select(SolutionAssemblies.Get).Where(r => r.Module is not null && r.Module != a.Module).Select(r => (From: a.Module!, To: r.Module!)))
            .Distinct()
            .ToList();

        var graph = edges.GroupBy(e => e.From).ToDictionary(g => g.Key, g => g.Select(e => e.To).ToList());
        var state = new Dictionary<string, int>();

        bool HasCycle(string node, Stack<string> path)
        {
            state[node] = 1;
            path.Push(node);
            foreach (var next in graph.GetValueOrDefault(node, []))
            {
                if (state.GetValueOrDefault(next) == 1 || (state.GetValueOrDefault(next) == 0 && HasCycle(next, path)))
                {
                    return true;
                }
            }

            path.Pop();
            state[node] = 2;
            return false;
        }

        foreach (var node in graph.Keys)
        {
            var path = new Stack<string>();
            Assert.False(state.GetValueOrDefault(node) == 0 && HasCycle(node, path), $"Module cycle: {string.Join(" -> ", path.Reverse())}");
        }
    }

    [Fact]
    public void Module_implementations_expose_only_their_registration_class()
    {
        var violations = SolutionAssemblies.OfKind(AssemblyKind.ModuleImplementation)
            .SelectMany(a => a.Assembly.GetExportedTypes()
                .Where(t => !t.IsDefined(typeof(CompilerGeneratedAttribute), false))
                .Where(t => !(t.IsAbstract && t.IsSealed && t.Name == $"{a.Module}Module"))
                .Select(t => t.FullName))
            .ToList();

        Assert.Empty(violations);
    }
}
