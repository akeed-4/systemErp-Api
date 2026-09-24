using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Identity.Persistence;

internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options, ModuleDbContextDependencies dependencies)
    : ModuleDbContext(options, dependencies)
{
    public const string SchemaName = "identity";

    public override string Schema => SchemaName;

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<PasswordResetRequest> PasswordResetRequests => Set<PasswordResetRequest>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Role>(b =>
        {
            b.ToTable("Roles");
            b.HasKey(r => r.Code);
            b.Property(r => r.Code).HasMaxLength(40).IsUnicode(false);
            b.Property(r => r.NameAr).HasMaxLength(100);
            b.Property(r => r.NameEn).HasMaxLength(100);
        });

        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("Users");
            b.HasKey(u => u.Id);
            b.Property(u => u.Name).HasMaxLength(200);
            b.Property(u => u.Email).HasMaxLength(256);
            b.Property(u => u.NormalizedEmail).HasMaxLength(256);
            b.Property(u => u.Phone).HasMaxLength(32);
            b.Property(u => u.NormalizedPhone).HasMaxLength(32).IsUnicode(false);
            b.Property(u => u.PasswordHash).HasMaxLength(500).IsUnicode(false);
            b.Property(u => u.RoleCode).HasMaxLength(40).IsUnicode(false);
            b.Property(u => u.JobTitle).HasMaxLength(100);
            b.Property(u => u.Department).HasMaxLength(100);
            b.Property(u => u.AvatarUrl).HasMaxLength(1000);
            b.Property(u => u.RowVersion).IsRowVersion();
            b.Ignore(u => u.AvatarInitials);
            b.HasIndex(u => new { u.TenantId, u.NormalizedEmail }).IsUnique().HasFilter("[NormalizedEmail] IS NOT NULL");
            b.HasIndex(u => new { u.TenantId, u.NormalizedPhone }).IsUnique().HasFilter("[NormalizedPhone] IS NOT NULL");
            b.HasIndex(u => new { u.TenantId, u.RoleCode });
            b.HasOne<Role>().WithMany().HasForeignKey(u => u.RoleCode).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RefreshToken>(b =>
        {
            b.ToTable("RefreshTokens");
            b.HasKey(t => t.Id);
            b.Property(t => t.TokenHash).HasMaxLength(64).IsUnicode(false);
            b.HasIndex(t => new { t.TenantId, t.TokenHash }).IsUnique();
            b.HasIndex(t => new { t.TenantId, t.UserId });
            b.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PasswordResetRequest>(b =>
        {
            b.ToTable("PasswordResetRequests");
            b.HasKey(r => r.Id);
            b.Property(r => r.OtpHash).HasMaxLength(64).IsUnicode(false);
            b.Property(r => r.ResetTokenHash).HasMaxLength(64).IsUnicode(false);
            b.HasIndex(r => new { r.TenantId, r.UserId, r.CreatedAt });
            b.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

internal sealed class IdentityDbContextDesignTimeFactory : ModuleDesignTimeFactory<IdentityDbContext>
{
    protected override string Schema => IdentityDbContext.SchemaName;

    protected override IdentityDbContext Create(DbContextOptions<IdentityDbContext> options, ModuleDbContextDependencies dependencies) =>
        new(options, dependencies);
}
