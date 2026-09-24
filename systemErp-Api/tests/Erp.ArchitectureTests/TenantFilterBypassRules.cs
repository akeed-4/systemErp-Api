using System.Reflection;

namespace Erp.ArchitectureTests;

/// <summary>
/// IgnoreQueryFilters() would also drop the tenant filter. The only allowed bypass is the audited IPlatformScope
/// (building blocks), so no module and no catalog code may call it.
/// </summary>
public sealed class TenantFilterBypassRules
{
    private const BindingFlags AllMembers = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    [Fact]
    public void Modules_never_call_IgnoreQueryFilters()
    {
        var violations = SolutionAssemblies.All
            .Where(a => a.Kind is AssemblyKind.ModuleImplementation or AssemblyKind.ModuleContracts)
            .SelectMany(a => a.Assembly.GetTypes())
            .SelectMany(t => t.GetMethods(AllMembers).Cast<MethodBase>().Concat(t.GetConstructors(AllMembers)))
            .Where(CallsIgnoreQueryFilters)
            .Select(m => $"{m.DeclaringType?.FullName}.{m.Name}")
            .ToList();

        Assert.Empty(violations);
    }

    private static bool CallsIgnoreQueryFilters(MethodBase method)
    {
        byte[]? il;
        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray();
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        if (il is null)
        {
            return false;
        }

        // call (0x28) / callvirt (0x6F) followed by a 4-byte method token.
        for (var i = 0; i < il.Length - 4; i++)
        {
            if (il[i] is not (0x28 or 0x6F))
            {
                continue;
            }

            var token = BitConverter.ToInt32(il, i + 1);
            try
            {
                var target = method.Module.ResolveMethod(
                    token,
                    method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null,
                    method.IsGenericMethod ? method.GetGenericArguments() : null);
                if (target?.Name == "IgnoreQueryFilters")
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                // Not a method token at this offset.
            }
            catch (BadImageFormatException)
            {
            }
        }

        return false;
    }
}
