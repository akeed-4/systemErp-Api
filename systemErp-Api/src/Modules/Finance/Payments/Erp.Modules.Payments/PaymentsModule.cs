using Erp.BuildingBlocks.Infrastructure.Modules;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.Payments.Application;
using Erp.Modules.Payments.Contracts;
using Erp.Modules.Payments.Endpoints;
using Erp.Modules.Payments.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Modules.Payments;

/// <summary>The only public type of the module: registration and endpoint mapping.</summary>
public static class PaymentsModule
{
    public const string Key = "payments";

    public static IServiceCollection AddPaymentsModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<PaymentsDbContext>(Key, PaymentsDbContext.SchemaName, order: 90);
        services.AddScoped<IPaymentMethodDirectory, PaymentMethodDirectory>();
        services.AddScoped<VoucherService>();
        services.AddScoped<IVoucherService>(sp => sp.GetRequiredService<VoucherService>());
        services.AddScoped<IModuleSeeder, PaymentsSeeder>();
        return services;
    }

    public static IEndpointRouteBuilder MapPaymentsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        PaymentEndpoints.Map(endpoints);
        return endpoints;
    }
}
