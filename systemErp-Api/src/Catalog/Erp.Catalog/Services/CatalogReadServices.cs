using System.Globalization;
using System.Security.Cryptography;
using Erp.BuildingBlocks.Infrastructure.Modules;
using Erp.BuildingBlocks.Infrastructure.Options;
using Erp.BuildingBlocks.Infrastructure.Security;
using Erp.BuildingBlocks.Infrastructure.Tenancy;
using Erp.Catalog.Contracts;
using Erp.Catalog.Persistence;
using Erp.SharedKernel.Errors;
using Erp.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Erp.Catalog.Services;

internal sealed class TenantDirectory(
    IDbContextFactory<CatalogDbContext> dbFactory,
    IMemoryCache cache,
    IConnectionStringProtector protector,
    ITenantConnectionFactory connections,
    IOptions<TenancyOptions> options) : ITenantDirectory
{
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(Math.Max(1, options.Value.TenantCacheSeconds));

    public async Task<TenantDescriptor?> FindAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var key = CacheKey(tenantId);
        if (cache.TryGetValue(key, out TenantDescriptor? cached) && cached is not null)
        {
            return cached;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var descriptor = Map(row);
        cache.Set(key, descriptor, _ttl);
        return descriptor;
    }

    public async Task<TenantDescriptor?> FindByCodeAsync(string code, CancellationToken cancellationToken)
    {
        var normalized = TenantCodes.Normalize(code);
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var id = await db.Tenants.AsNoTracking().Where(t => t.Code == normalized).Select(t => (Guid?)t.Id).SingleOrDefaultAsync(cancellationToken);
        return id is null ? null : await FindAsync(id.Value, cancellationToken);
    }

    public async Task<IReadOnlyList<TenantDatabase>> ListDatabasesAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var dedicated = await db.Tenants.AsNoTracking()
            .Where(t => t.TenancyMode == TenancyMode.Dedicated && t.Status != TenantStatus.Archived && t.ConnectionStringEncrypted != null)
            .OrderBy(t => t.Code)
            .ToListAsync(cancellationToken);

        var list = new List<TenantDatabase> { new("shared", connections.SharedConnectionString, TenancyMode.Shared, null) };
        list.AddRange(dedicated.Select(t => new TenantDatabase(
            t.DatabaseName ?? t.Code,
            protector.Unprotect(t.ConnectionStringEncrypted!),
            TenancyMode.Dedicated,
            t.Id)));
        return list;
    }

    public void Invalidate(Guid tenantId) => cache.Remove(CacheKey(tenantId));

    private static string CacheKey(Guid tenantId) => $"catalog:tenant:{tenantId:N}";

    private TenantDescriptor Map(CatalogTenant row) =>
        new(
            row.Id,
            row.Code,
            row.NameAr,
            row.NameEn,
            row.Status,
            row.TenancyMode,
            row.ConnectionStringEncrypted is null ? null : protector.Unprotect(row.ConnectionStringEncrypted),
            row.DatabaseName,
            row.SchemaVersion);
}

internal sealed class TenantLoginIndex(IDbContextFactory<CatalogDbContext> dbFactory, TimeProvider clock) : ITenantLoginIndex
{
    public async Task<IReadOnlyList<LoginIndexMatch>> FindAsync(string identifier, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return [];
        }

        var email = LoginIdentifier.LooksLikeEmail(identifier) ? LoginIdentifier.NormalizeEmail(identifier) : null;
        var phone = email is null ? LoginIdentifier.NormalizePhone(identifier) : null;
        if (email is null && phone is null)
        {
            return [];
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entries = db.LoginIndex.AsNoTracking().Where(e => e.IsActive);
        entries = email is not null
            ? entries.Where(e => e.NormalizedEmail == email)
            : entries.Where(e => e.NormalizedPhone == phone);

        return await Project(db, entries).ToListAsync(cancellationToken);
    }

    public async Task<LoginIndexMatch?> FindByUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await Project(db, db.LoginIndex.AsNoTracking().Where(e => e.IsActive && e.UserId == userId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task UpsertAsync(LoginIndexEntry entry, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.LoginIndex.SingleOrDefaultAsync(e => e.TenantId == entry.TenantId && e.UserId == entry.UserId, cancellationToken);
        if (row is null)
        {
            row = new TenantLoginIndexEntry { TenantId = entry.TenantId, UserId = entry.UserId };
            db.LoginIndex.Add(row);
        }

        row.NormalizedEmail = LoginIdentifier.NormalizeEmail(entry.Email);
        row.NormalizedPhone = LoginIdentifier.NormalizePhone(entry.Phone);
        row.IsActive = entry.IsActive;
        row.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    private static IQueryable<LoginIndexMatch> Project(CatalogDbContext db, IQueryable<TenantLoginIndexEntry> entries) =>
        from e in entries
        join t in db.Tenants on e.TenantId equals t.Id
        where t.Status != TenantStatus.Archived
        orderby t.Code
        select new LoginIndexMatch(t.Id, t.Code, t.NameAr, t.NameEn, t.Status, e.UserId);
}

internal sealed class SubscriptionCatalog(IDbContextFactory<CatalogDbContext> dbFactory, TimeProvider clock) : ISubscriptionCatalog
{
    private static readonly string[] BillingCycles = ["monthly", "yearly"];
    private static readonly string[] PaymentMethods = ["mada", "credit_card", "bank_transfer", "apple_pay"];

    /// <remarks>No payment gateway yet: the upgrade is recorded as paid, exactly as the frontend does today.</remarks>
    public async Task<CompanySubscriptionDto> UpgradeAsync(Guid tenantId, string planCode, string billingCycle, string paymentMethod, CancellationToken cancellationToken)
    {
        if (!BillingCycles.Contains(billingCycle) || !PaymentMethods.Contains(paymentMethod))
        {
            throw ErpException.Validation("Unknown billing cycle or payment method.", "دورة الفوترة أو طريقة الدفع غير معروفة.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var plan = await db.Plans.SingleOrDefaultAsync(p => p.Code == planCode, cancellationToken)
            ?? throw ErpException.Validation("Unknown subscription plan.", "باقة الاشتراك غير معروفة.");
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var current in await db.Subscriptions.Where(s => s.TenantId == tenantId && (s.Status == "active" || s.Status == "trial")).ToListAsync(cancellationToken))
        {
            current.Status = "expired";
        }

        // Expire first so the one-active-subscription index never sees two rows.
        await db.SaveChangesAsync(cancellationToken);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var yearly = billingCycle == "yearly";
        db.Subscriptions.Add(new TenantSubscription
        {
            TenantId = tenantId,
            PlanCode = plan.Code,
            BillingCycle = billingCycle,
            StartDate = today,
            ExpiryDate = yearly ? today.AddYears(1) : today.AddMonths(1),
            Status = "active",
            PaidAmount = yearly ? plan.PriceYearly : plan.PriceMonthly,
            PaymentMethod = paymentMethod,
            TransactionReference = $"UPG-{paymentMethod.ToUpperInvariant()}-{RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture)}",
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await GetCurrentAsync(tenantId, cancellationToken))!;
    }

    private const string Unlimited = "unlimited";

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var plans = await db.Plans.AsNoTracking().Include(p => p.Features).OrderBy(p => p.SortOrder).ToListAsync(cancellationToken);
        return plans.Select(p =>
        {
            var features = p.Features.OrderBy(f => f.SortOrder).ToList();
            return new SubscriptionPlanDto(
                p.Code,
                p.NameAr,
                p.NameEn,
                p.DescriptionAr,
                p.DescriptionEn,
                p.PriceMonthly,
                p.PriceYearly,
                p.IsPopular,
                p.BadgeAr,
                p.BadgeEn,
                (object?)p.MaxUsers ?? Unlimited,
                (object?)p.MaxInvoicesPerMonth ?? Unlimited,
                (object?)p.MaxBranches ?? Unlimited,
                features.Select(f => f.FeatureAr).ToList(),
                features.Select(f => f.FeatureEn).ToList());
        }).ToList();
    }

    public async Task<CompanySubscriptionDto?> GetCurrentAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await (
            from s in db.Subscriptions.AsNoTracking()
            join p in db.Plans on s.PlanCode equals p.Code
            where s.TenantId == tenantId
            orderby (s.Status == "active" || s.Status == "trial") descending, s.StartDate descending
            select new CompanySubscriptionDto(
                p.Code, p.NameAr, p.NameEn, s.BillingCycle, s.StartDate, s.ExpiryDate, s.Status, s.PaidAmount, s.PaymentMethod, s.TransactionReference))
            .FirstOrDefaultAsync(cancellationToken);
    }
}

internal sealed class GlobalReferenceDataSource(IDbContextFactory<CatalogDbContext> dbFactory) : IGlobalReferenceDataSource
{
    public async Task<GlobalReferenceData> GetAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var roles = await db.Roles.AsNoTracking().OrderBy(r => r.SortOrder)
            .Select(r => new RoleDefinition(r.Code, r.NameAr, r.NameEn, r.SortOrder)).ToListAsync(cancellationToken);
        var screens = await db.Screens.AsNoTracking().OrderBy(s => s.SortOrder)
            .Select(s => new ScreenDefinition(s.Id, s.NameAr, s.NameEn, s.SortOrder)).ToListAsync(cancellationToken);
        return new GlobalReferenceData(roles, screens);
    }
}

internal static class TenantCodes
{
    public static string Normalize(string code) => code.Trim().ToUpperInvariant();
}
