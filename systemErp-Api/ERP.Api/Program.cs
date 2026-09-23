using ERP.Application.Interfaces;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using Microsoft.OpenApi.Models;
using ERP.Infrastructure.MultiTenancy;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Rayah ERP API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] { }
        }
    });
});

// Add Database
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseInMemoryDatabase("ErpDb")); // Use SQL Server/PostgreSQL in production

builder.Services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

// Add MediatR
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(IApplicationDbContext).Assembly));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantService, ERP.Infrastructure.MultiTenancy.TenantService>();
builder.Services.AddScoped<IAccountingService, ERP.Application.Accounting.AccountingService>();
builder.Services.AddScoped<IInventoryService, ERP.Application.Inventory.InventoryService>();
builder.Services.AddScoped<IAccountSuggestionService, ERP.Infrastructure.Services.AccountSuggestionService>();
builder.Services.AddScoped<ICarShowroomService, ERP.Infrastructure.Services.CarShowroomService>();
builder.Services.AddScoped<IReportsService, ERP.Application.Reports.ReportsService>();

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");

app.UseMiddleware<TenantResolverMiddleware>();

app.UseAuthorization();

app.MapControllers();

app.Run();
