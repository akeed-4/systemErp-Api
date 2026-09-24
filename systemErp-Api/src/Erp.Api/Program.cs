using System.Text;
using Erp.BuildingBlocks.Infrastructure;
using Erp.BuildingBlocks.Infrastructure.Options;
using Erp.BuildingBlocks.Web;
using Erp.Composition;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{TenantCode}] {Message:lj}{NewLine}{Exception}"));

builder.Services.AddErpPlatform(builder.Configuration);
builder.Services.AddErpOutboxDispatcher();
builder.Services.AddErpWeb();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwt) =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Value.Issuer,
            ValidAudience = jwt.Value.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                string.IsNullOrEmpty(jwt.Value.SigningKey) ? throw new InvalidOperationException("Jwt:SigningKey is not configured.") : jwt.Value.SigningKey)),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = ErpClaims.Name,
            RoleClaimType = ErpClaims.Role,
        };
    });

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:3000"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseExceptionHandler();
app.UseSerilogRequestLogging();
app.UseCors();
app.UseAuthentication();
app.UseErpTenantResolution();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapErpApi().MapErpModules();

// The API never migrates. Run Erp.Migrator (CI/deploy) before starting it.
await app.RunAsync();

/// <summary>Entry point marker for WebApplicationFactory in integration tests.</summary>
public partial class Program;
