using System.Globalization;
using Erp.Catalog.Contracts;
using Erp.Catalog.Services;
using Erp.Composition;
using Erp.SharedKernel.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Erp.Migrator — the only component that changes database schemas.
//   migrate                  catalog → shared → every dedicated DB (default)
//   provision --mode ...     create a tenant (shared or dedicated) end to end
//   list-databases           show the shared DB and every dedicated DB from the catalog
var command = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal) ? args[0] : "migrate";
var options = ParseOptions(args);

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    ContentRootPath = AppContext.BaseDirectory,
    EnvironmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environments.Development,
});
builder.Configuration.AddEnvironmentVariables(prefix: "ERP_");
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddErpPlatform(builder.Configuration);

using var host = builder.Build();
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

return command switch
{
    "migrate" => await MigrateAsync(host.Services, cts.Token),
    "provision" => await ProvisionAsync(host.Services, options, cts.Token),
    "list-databases" => await ListDatabasesAsync(host.Services, cts.Token),
    _ => Usage(),
};

static async Task<int> MigrateAsync(IServiceProvider services, CancellationToken ct)
{
    var report = await services.GetRequiredService<IDatabaseMigrationOrchestrator>().RunAsync(ct);
    Console.WriteLine($"Schema version: {report.SchemaVersion}");
    Console.WriteLine();
    Console.WriteLine($"{"Database",-40} {"Kind",-28} {"Result",-8} {"Applied",7} {"Time",8}");
    foreach (var d in report.Databases)
    {
        Console.WriteLine($"{d.Database,-40} {d.Kind,-28} {(d.Succeeded ? "OK" : "FAILED"),-8} {d.AppliedMigrations,7} {d.Duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture),7}s");
        if (d.Error is not null)
        {
            Console.WriteLine($"    error: {d.Error}");
        }
    }

    Console.WriteLine();
    Console.WriteLine(report.Succeeded ? "All databases are up to date." : "Some databases failed; re-run after fixing them (the migrator is resumable).");
    return report.Succeeded ? 0 : 1;
}

static async Task<int> ProvisionAsync(IServiceProvider services, Dictionary<string, string> o, CancellationToken ct)
{
    string Get(string key, string fallback = "") => o.TryGetValue(key, out var v) ? v : fallback;

    var mode = Get("mode", "shared").Equals("dedicated", StringComparison.OrdinalIgnoreCase) ? TenancyMode.Dedicated : TenancyMode.Shared;
    var request = new ProvisionTenantRequest(
        Code: o.GetValueOrDefault("code"),
        mode,
        new CompanyInfo(
            Get("name-ar", Get("name-en")),
            Get("name-en", Get("name-ar")),
            Get("vat", "300000000000003"),
            Get("cr"),
            Get("city", "الرياض"),
            Get("address"),
            Get("phone"),
            Get("email", Get("owner-email")),
            o.GetValueOrDefault("industry")),
        new OwnerInfo(Get("owner-name", "Owner"), Get("owner-email"), Get("owner-phone"), Get("owner-password")),
        new SubscriptionRequest(Get("plan", mode == TenancyMode.Dedicated ? "enterprise" : "starter"), Get("billing", "monthly"), Get("payment", "bank_transfer")));

    var result = await services.GetRequiredService<ITenantProvisioningService>().ProvisionAsync(request, ct);
    Console.WriteLine($"Tenant provisioned: code={result.TenantCode} id={result.TenantId} mode={result.Mode.ToString().ToLowerInvariant()} database={result.DatabaseName ?? "(shared)"}");
    Console.WriteLine($"Owner user id: {result.OwnerUserId}. Sign in with the owner email/password (tenantCode={result.TenantCode}).");
    return 0;
}

static async Task<int> ListDatabasesAsync(IServiceProvider services, CancellationToken ct)
{
    foreach (var db in await services.GetRequiredService<ITenantDirectory>().ListDatabasesAsync(ct))
    {
        Console.WriteLine($"{db.Mode.ToString().ToLowerInvariant(),-10} {db.Name,-40} {db.DedicatedTenantId?.ToString() ?? string.Empty}");
    }

    return 0;
}

static int Usage()
{
    Console.WriteLine("""
        Usage:
          Erp.Migrator migrate
          Erp.Migrator provision --mode shared|dedicated --name-ar <..> --name-en <..> --owner-email <..> --owner-password <..>
                                 [--code <CODE>] [--owner-name <..>] [--owner-phone <..>] [--vat <15 digits>] [--cr <..>] [--city <..>] [--plan starter|professional|enterprise]
          Erp.Migrator list-databases
        """);
    return 2;
}

static Dictionary<string, string> ParseOptions(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Length)
        {
            result[args[i][2..]] = args[++i];
        }
    }

    return result;
}
