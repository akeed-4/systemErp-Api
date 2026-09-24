using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Permissions.Persistence;

/// <summary>authz.Screens: read-only copy of catalog.Screens.</summary>
[GlobalReferenceData]
internal sealed class Screen
{
    public string Id { get; set; } = string.Empty;

    public string NameAr { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}

internal abstract class ScreenGrant : TenantEntity
{
    public string ScreenId { get; protected set; } = string.Empty;

    public bool CanView { get; private set; }

    public bool CanCreate { get; private set; }

    public bool CanEdit { get; private set; }

    public bool CanDelete { get; private set; }

    public bool CanApprove { get; private set; }

    public void Set(bool view, bool create, bool edit, bool delete, bool approve)
    {
        CanView = view;
        CanCreate = create;
        CanEdit = edit;
        CanDelete = delete;
        CanApprove = approve;
    }
}

internal sealed class RoleScreenPermission : ScreenGrant
{
    private RoleScreenPermission()
    {
    }

    public RoleScreenPermission(string roleCode, string screenId)
    {
        RoleCode = roleCode;
        ScreenId = screenId;
    }

    public string RoleCode { get; private set; } = string.Empty;
}

/// <summary>A per-user override: when present for a screen, it replaces the role's permission for that screen.</summary>
internal sealed class UserScreenPermission : ScreenGrant
{
    private UserScreenPermission()
    {
    }

    public UserScreenPermission(Guid userId, string screenId)
    {
        UserId = userId;
        ScreenId = screenId;
    }

    public Guid UserId { get; private set; }
}

internal sealed class PermissionsDbContext(DbContextOptions<PermissionsDbContext> options, ModuleDbContextDependencies dependencies)
    : ModuleDbContext(options, dependencies)
{
    public const string SchemaName = "authz";

    public override string Schema => SchemaName;

    public DbSet<Screen> Screens => Set<Screen>();

    public DbSet<RoleScreenPermission> RolePermissions => Set<RoleScreenPermission>();

    public DbSet<UserScreenPermission> UserPermissions => Set<UserScreenPermission>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Screen>(b =>
        {
            b.ToTable("Screens");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).HasMaxLength(60).IsUnicode(false);
            b.Property(s => s.NameAr).HasMaxLength(200);
            b.Property(s => s.NameEn).HasMaxLength(200);
        });

        modelBuilder.Entity<RoleScreenPermission>(b =>
        {
            b.ToTable("RoleScreenPermissions");
            b.HasKey(p => p.Id);
            b.Property(p => p.RoleCode).HasMaxLength(40).IsUnicode(false);
            b.Property(p => p.ScreenId).HasMaxLength(60).IsUnicode(false);
            b.HasIndex(p => new { p.TenantId, p.RoleCode, p.ScreenId }).IsUnique();
            b.HasOne<Screen>().WithMany().HasForeignKey(p => p.ScreenId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserScreenPermission>(b =>
        {
            b.ToTable("UserScreenPermissions");
            b.HasKey(p => p.Id);
            b.Property(p => p.ScreenId).HasMaxLength(60).IsUnicode(false);
            b.HasIndex(p => new { p.TenantId, p.UserId, p.ScreenId }).IsUnique();
            b.HasOne<Screen>().WithMany().HasForeignKey(p => p.ScreenId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}

internal sealed class PermissionsDbContextDesignTimeFactory : ModuleDesignTimeFactory<PermissionsDbContext>
{
    protected override string Schema => PermissionsDbContext.SchemaName;

    protected override PermissionsDbContext Create(DbContextOptions<PermissionsDbContext> options, ModuleDbContextDependencies dependencies) =>
        new(options, dependencies);
}
