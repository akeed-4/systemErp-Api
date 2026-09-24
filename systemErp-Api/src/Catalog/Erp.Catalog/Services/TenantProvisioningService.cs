using System.Net.Mail;
using System.Security.Cryptography;
using Erp.BuildingBlocks.Infrastructure.Schema;
using Erp.BuildingBlocks.Infrastructure.Modules;
using Erp.BuildingBlocks.Infrastructure.Options;
using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.BuildingBlocks.Infrastructure.Security;
using Erp.BuildingBlocks.Infrastructure.Tenancy;
using Erp.Catalog.Contracts;
using Erp.Catalog.Persistence;
using Erp.SharedKernel.Errors;
using Erp.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Erp.Catalog.Services;

/// <summary>
/// Provisioning steps (each idempotent, so a failed run can be retried with the same code):
/// 1. catalog row, status provisioning (dedicated: database name + encrypted connection string);
/// 2. dedicated: create + migrate the database; shared: verify the shared database is migrated;
/// 3. module seeders inside a scope bound to the new tenant (company profile, head office, settings, owner, permissions);
/// 4. subscription, owner login-index row, schema version, status active.
/// </summary>
internal sealed class TenantProvisioningService(
    IDbContextFactory<CatalogDbContext> catalogFactory,
    ITenantDirectory directory,
    ITenantScopeFactory scopes,
    ITenantConnectionFactory connections,
    IConnectionStringProtector protector,
    TenantDatabaseMigrator migrator,
    ISchemaVersionProvider schemaVersions,
    ITenantLoginIndex loginIndex,
    IOptions<TenancyOptions> options,
    TimeProvider clock,
    ILogger<TenantProvisioningService> logger) : ITenantProvisioningService
{
    private const string BaseCurrency = "SAR";

    public async Task<ProvisionTenantResult> ProvisionAsync(ProvisionTenantRequest request, CancellationToken cancellationToken)
    {
        Validate(request);

        var tenant = await CreateOrResumeCatalogRowAsync(request, cancellationToken);
        var descriptor = await directory.FindAsync(tenant.Id, cancellationToken)
            ?? throw new InvalidOperationException("The catalog row was not found after creation.");

        var connectionString = connections.GetConnectionString(descriptor);
        if (descriptor.Mode == TenancyMode.Dedicated)
        {
            logger.LogInformation("Provisioning dedicated database {Database} for tenant {TenantCode}", descriptor.DatabaseName, descriptor.Code);
            await migrator.MigrateAsync(connectionString, cancellationToken);
        }
        else if (!await migrator.IsUpToDateAsync(connectionString, cancellationToken))
        {
            throw new ErpException(
                "shared_database_not_migrated",
                503,
                "The shared database is not on the current schema. Run Erp.Migrator first.",
                "قاعدة البيانات المشتركة غير محدّثة، يرجى تشغيل أداة الترحيل أولاً.");
        }

        await SeedAsync(descriptor, tenant.OwnerUserId!.Value, request, cancellationToken);
        await FinalizeAsync(tenant.Id, tenant.OwnerUserId.Value, request, cancellationToken);

        return new ProvisionTenantResult(tenant.Id, tenant.Code, tenant.OwnerUserId.Value, descriptor.Mode, descriptor.DatabaseName);
    }

    private async Task<CatalogTenant> CreateOrResumeCatalogRowAsync(ProvisionTenantRequest request, CancellationToken cancellationToken)
    {
        await using var db = await catalogFactory.CreateDbContextAsync(cancellationToken);

        var code = request.Code is { Length: > 0 } requested ? TenantCodes.Normalize(requested) : await GenerateCodeAsync(db, request.Company.NameEn, cancellationToken);
        if (!IsValidCode(code))
        {
            throw ErpException.Validation(
                "The company code may contain letters, digits and '-' only (3-30 characters).",
                "رمز المنشأة يجب أن يتكون من حروف وأرقام و '-' فقط (3-30 حرفاً).");
        }

        var tenant = await db.Tenants.SingleOrDefaultAsync(t => t.Code == code, cancellationToken);
        if (tenant is not null)
        {
            if (tenant.Status != TenantStatus.Provisioning || tenant.TenancyMode != request.Mode)
            {
                throw ErpException.Conflict("tenant_code_taken", $"The company code {code} is already in use.", $"رمز المنشأة {code} مستخدم مسبقاً.");
            }

            logger.LogInformation("Resuming provisioning of tenant {TenantCode}", code);
            return tenant;
        }

        tenant = new CatalogTenant
        {
            Id = Guid.CreateVersion7(),
            Code = code,
            NameAr = request.Company.NameAr.Trim(),
            NameEn = request.Company.NameEn.Trim(),
            Status = TenantStatus.Provisioning,
            TenancyMode = request.Mode,
            OwnerUserId = Guid.CreateVersion7(),
            CreatedAt = clock.GetUtcNow(),
        };

        if (request.Mode == TenancyMode.Dedicated)
        {
            var databaseName = options.Value.DedicatedDatabasePrefix + code.Replace('-', '_');
            if (!SqlServerDatabases.IsSafeName(databaseName))
            {
                throw new InvalidOperationException($"Generated database name '{databaseName}' is not valid.");
            }

            tenant.DatabaseName = databaseName;
            tenant.ConnectionStringEncrypted = protector.Protect(connections.BuildDedicatedConnectionString(databaseName));
        }

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(cancellationToken);
        return tenant;
    }

    private async Task SeedAsync(TenantDescriptor tenant, Guid ownerUserId, ProvisionTenantRequest request, CancellationToken cancellationToken)
    {
        var company = request.Company;
        var context = new TenantSeedContext(
            tenant.Id,
            tenant.Code,
            new CompanySeed(
                company.NameAr.Trim(),
                company.NameEn.Trim(),
                company.VatNumber.Trim(),
                company.CrNumber.Trim(),
                company.City.Trim(),
                company.Address.Trim(),
                "المملكة العربية السعودية",
                company.Phone.Trim(),
                company.Email.Trim(),
                company.Industry),
            new OwnerSeed(ownerUserId, request.Owner.Name.Trim(), request.Owner.Email.Trim(), request.Owner.Phone.Trim(), request.Owner.Password),
            BaseCurrency);

        await using var scope = scopes.CreateForTenant(tenant);
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await unitOfWork.ExecuteAsync(
            async ct =>
            {
                foreach (var seeder in scope.ServiceProvider.GetServices<IModuleSeeder>().OrderBy(s => s.Order))
                {
                    await seeder.SeedTenantAsync(context, ct);
                    await unitOfWork.SaveChangesAsync(ct);
                }
            },
            cancellationToken);
    }

    private async Task FinalizeAsync(Guid tenantId, Guid ownerUserId, ProvisionTenantRequest request, CancellationToken cancellationToken)
    {
        await using var db = await catalogFactory.CreateDbContextAsync(cancellationToken);

        if (!await db.Subscriptions.AnyAsync(s => s.TenantId == tenantId, cancellationToken))
        {
            var plan = await db.Plans.SingleOrDefaultAsync(p => p.Code == request.Subscription.PlanCode, cancellationToken)
                ?? await db.Plans.OrderBy(p => p.SortOrder).FirstAsync(cancellationToken);
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

            // No payment gateway yet: every new company starts on a 14-day trial of the chosen plan.
            db.Subscriptions.Add(new TenantSubscription
            {
                TenantId = tenantId,
                PlanCode = plan.Code,
                BillingCycle = request.Subscription.BillingCycle is "yearly" ? "yearly" : "monthly",
                StartDate = today,
                ExpiryDate = today.AddDays(14),
                Status = "trial",
                PaidAmount = 0,
                PaymentMethod = request.Subscription.PaymentMethod,
                TransactionReference = "TRIAL",
            });
        }

        await loginIndex.UpsertAsync(new LoginIndexEntry(tenantId, ownerUserId, request.Owner.Email, request.Owner.Phone, true), cancellationToken);

        var tenant = await db.Tenants.SingleAsync(t => t.Id == tenantId, cancellationToken);
        tenant.SchemaVersion = schemaVersions.ExpectedVersion;
        tenant.Status = TenantStatus.Active;
        tenant.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        directory.Invalidate(tenantId);
    }

    private static async Task<string> GenerateCodeAsync(CatalogDbContext db, string nameEn, CancellationToken cancellationToken)
    {
        var stem = new string(nameEn.ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).Take(12).ToArray());
        if (stem.Length < 3)
        {
            stem = "ERP" + stem;
        }

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var candidate = $"{stem}-{RandomSuffix()}";
            if (!await db.Tenants.AnyAsync(t => t.Code == candidate, cancellationToken))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not generate a unique company code.");
    }

    private static string RandomSuffix()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return string.Create(4, alphabet, (span, chars) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
            }
        });
    }

    private static bool IsValidCode(string code) =>
        code.Length is >= 3 and <= 30 && code.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '-');

    private static void Validate(ProvisionTenantRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Company.NameAr) && string.IsNullOrWhiteSpace(request.Company.NameEn))
        {
            errors["companyNameAr"] = ["Company name is required."];
        }

        if (!MailAddress.TryCreate(request.Owner.Email?.Trim(), out _))
        {
            errors["adminEmail"] = ["A valid email is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Owner.Password) || request.Owner.Password.Length < 8)
        {
            errors["password"] = ["The password must be at least 8 characters."];
        }

        if (string.IsNullOrWhiteSpace(request.Owner.Name))
        {
            errors["adminName"] = ["The administrator name is required."];
        }

        if (errors.Count > 0)
        {
            throw ErpException.Validation("The company registration is not valid.", "بيانات تسجيل المنشأة غير مكتملة أو غير صحيحة.", errors);
        }
    }
}
