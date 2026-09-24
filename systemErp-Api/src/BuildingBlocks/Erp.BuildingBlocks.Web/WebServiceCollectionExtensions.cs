using System.Text.Json;
using System.Text.Json.Serialization;
using Erp.SharedKernel.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Erp.BuildingBlocks.Web;

public static class WebServiceCollectionExtensions
{
    /// <summary>JSON conventions (camelCase, snake_case enums), error handling, current user, screen-permission policies.</summary>
    public static IServiceCollection AddErpWeb(this IServiceCollection services)
    {
        services.Configure<JsonOptions>(o => ConfigureJson(o.SerializerOptions));
        services.AddHttpContextAccessor();
        services.AddExceptionHandler<ErpExceptionHandler>();
        services.AddProblemDetails();

        services.RemoveAll<ICurrentUser>();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, ScreenPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, ScreenPermissionHandler>();
        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, ErpAuthorizationResultHandler>();
        return services;
    }

    public static void ConfigureJson(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
    }

    /// <summary>Must run after UseAuthentication and before UseAuthorization.</summary>
    public static IApplicationBuilder UseErpTenantResolution(this IApplicationBuilder app) =>
        app.UseMiddleware<TenantResolutionMiddleware>();

    /// <summary>Root group for module endpoints: business errors become ApiResponse + RFC 7807 without error logs.</summary>
    public static RouteGroupBuilder MapErpApi(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGroup(string.Empty).AddEndpointFilter<ErpExceptionEndpointFilter>();
}
