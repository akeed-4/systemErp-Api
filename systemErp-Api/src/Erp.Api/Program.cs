using System.Text;
using ERP.Api.Infrastructure;
using ERP.Service;
using ERP.Service.Data;
using ERP.Service.Services.Shared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// كل نقاط النهاية تتطلب مصادقة افتراضياً؛ [AllowAnonymous] فقط على الدخول وتسجيل المنشأة واستعادة كلمة المرور.
builder.Services.AddControllers(o =>
    {
        o.ModelBinderProviders.Insert(0, new DataSourceLoadOptionsBinderProvider()); // خيارات DevExtreme من الـ query string
        o.Filters.Add(new AuthorizeFilter(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()));
    })
    .AddJsonOptions(o => JsonConfiguration.Apply(o.JsonSerializerOptions))
    .ConfigureApiBehaviorOptions(o =>
        o.InvalidModelStateResponseFactory = ctx =>
        {
            var errors = ctx.ModelState.Where(e => e.Value?.Errors.Count > 0)
                .SelectMany(e => e.Value!.Errors.Select(x => string.IsNullOrEmpty(x.ErrorMessage) ? $"قيمة غير صالحة في الحقل {e.Key}" : x.ErrorMessage)).ToList();
            return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(ApiResponse<object>.Fail(400, "بيانات الطلب غير صالحة.", errors));
        });
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o => JsonConfiguration.Apply(o.SerializerOptions));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "ERP API", Version = "v1" });
    c.CustomSchemaIds(t => t.FullName!.Replace("ERP.Core.", "").Replace('+', '.'));
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme.",
        Name = "Authorization", In = ParameterLocation.Header, Type = SecuritySchemeType.Http, Scheme = "bearer",
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() },
    });
});

// الخدمات وقاعدة البيانات (SQL Server)
builder.Services.AddErpServices(builder.Configuration);

// JWT
var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwt.Key) || jwt.Key.Length < 32)
    throw new InvalidOperationException("Jwt:Key غير مضبوط (32 حرفاً على الأقل). استخدم user-secrets أو متغير البيئة Jwt__Key.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = jwt.Issuer,
            ValidateAudience = true, ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateLifetime = true, ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = System.Security.Claims.ClaimTypes.Name,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
        };
        o.MapInboundClaims = false; // نبقي أسماء المطالبات كما أصدرناها
        o.Events = new JwtBearerEvents
        {
            OnChallenge = async ctx =>
            {
                ctx.HandleResponse();
                ctx.Response.StatusCode = 401;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                await ctx.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(401, "يلزم تسجيل الدخول."));
            },
        };
    });
builder.Services.AddAuthorization();

// CORS: أصول الواجهة المسموحة من الإعدادات فقط (Cors:Origins)
var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (origins.Length > 0) p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
}));

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseMiddleware<TenantMiddleware>();
app.UseAuthorization();
app.MapControllers();

// ترحيل قاعدة البيانات تلقائياً فقط عند التفعيل الصريح (Database:AutoMigrate=true)؛ افتراضياً يدوي عبر dotnet ef.
if (app.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<ErpDbContext>().Database.Migrate();
}

app.Run();

public partial class Program { } // للاختبارات التكاملية
