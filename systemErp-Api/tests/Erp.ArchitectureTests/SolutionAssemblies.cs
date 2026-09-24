using System.Reflection;

namespace Erp.ArchitectureTests;

internal enum AssemblyKind
{
    SharedKernel,
    BuildingBlock,
    CatalogContracts,
    CatalogImplementation,
    ModuleContracts,
    ModuleImplementation,
    Host,
}

internal sealed record SolutionAssembly(Assembly Assembly, string Name, AssemblyKind Kind, string? Module)
{
    public IReadOnlyList<string> References { get; } =
        Assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("Erp.", StringComparison.Ordinal)).ToList();

    public override string ToString() => Name;
}

/// <summary>Every Erp.* assembly of the solution, loaded from the test output folder and classified.</summary>
internal static class SolutionAssemblies
{
    private static readonly Lazy<IReadOnlyList<SolutionAssembly>> Loaded = new(Load);

    public static IReadOnlyList<SolutionAssembly> All => Loaded.Value;

    public static IEnumerable<SolutionAssembly> OfKind(AssemblyKind kind) => All.Where(a => a.Kind == kind);

    public static SolutionAssembly Get(string name) => All.Single(a => a.Name == name);

    private static List<SolutionAssembly> Load()
    {
        var result = new List<SolutionAssembly>();
        foreach (var file in Directory.GetFiles(AppContext.BaseDirectory, "Erp.*.dll"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name.EndsWith("Tests", StringComparison.Ordinal))
            {
                continue;
            }

            var assembly = Assembly.Load(new AssemblyName(name));
            result.Add(Classify(assembly, name));
        }

        return result;
    }

    private static SolutionAssembly Classify(Assembly assembly, string name)
    {
        if (name == "Erp.SharedKernel")
        {
            return new(assembly, name, AssemblyKind.SharedKernel, null);
        }

        if (name.StartsWith("Erp.BuildingBlocks.", StringComparison.Ordinal))
        {
            return new(assembly, name, AssemblyKind.BuildingBlock, null);
        }

        if (name == "Erp.Catalog.Contracts")
        {
            return new(assembly, name, AssemblyKind.CatalogContracts, "Catalog");
        }

        if (name == "Erp.Catalog")
        {
            return new(assembly, name, AssemblyKind.CatalogImplementation, "Catalog");
        }

        if (name.StartsWith("Erp.Modules.", StringComparison.Ordinal))
        {
            var parts = name.Split('.');
            var module = parts[2];
            var kind = parts.Length > 3 && parts[3] == "Contracts" ? AssemblyKind.ModuleContracts : AssemblyKind.ModuleImplementation;
            return new(assembly, name, kind, module);
        }

        return new(assembly, name, AssemblyKind.Host, null);
    }
}
