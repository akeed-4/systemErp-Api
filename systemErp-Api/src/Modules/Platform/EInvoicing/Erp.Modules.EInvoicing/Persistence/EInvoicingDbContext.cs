using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.EInvoicing.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.EInvoicing.Persistence;

internal sealed class EInvoicingDbContext(DbContextOptions<EInvoicingDbContext> options, ModuleDbContextDependencies dependencies)
    : ModuleDbContext(options, dependencies)
{
    public const string SchemaName = "einvoicing";

    public override string Schema => SchemaName;

    public DbSet<EInvoicingDevice> Devices => Set<EInvoicingDevice>();

    public DbSet<EInvoiceDocument> Documents => Set<EInvoiceDocument>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EInvoicingDevice>(b =>
        {
            b.ToTable("EInvoicingDevices");
            b.HasKey(d => d.Id);
            b.Property(d => d.SerialNumber).HasMaxLength(60).IsUnicode(false);
            b.Property(d => d.Name).HasMaxLength(200);
            b.Property(d => d.SolutionName).HasMaxLength(100);
            b.Property(d => d.SolutionVersion).HasMaxLength(20);
            b.Property(d => d.TaxRegistrationNumber).HasMaxLength(15).IsUnicode(false);
            b.Property(d => d.CsrCommonName).HasMaxLength(100);
            b.Property(d => d.OrganizationUnit).HasMaxLength(100);
            b.Property(d => d.OrganizationName).HasMaxLength(200);
            b.Property(d => d.CountryCode).HasMaxLength(2).IsUnicode(false);
            b.Property(d => d.BusinessCategory).HasMaxLength(100);
            b.Property(d => d.CustomEndpointUrl).HasMaxLength(500);
            b.Property(d => d.CsidEncrypted).HasMaxLength(4000).IsUnicode(false);
            b.Property(d => d.SecretEncrypted).HasMaxLength(4000).IsUnicode(false);
            b.Property(d => d.PrivateKeyEncrypted).HasMaxLength(8000).IsUnicode(false);
            b.Property(d => d.LastInvoiceHash).HasMaxLength(100).IsUnicode(false);
            b.Property(d => d.LastTestMessage).HasMaxLength(1000);
            b.Property(d => d.RowVersion).IsRowVersion();
            b.HasIndex(d => new { d.TenantId, d.SerialNumber }).IsUnique();
            b.HasIndex(d => new { d.TenantId, d.IsDefault }).IsUnique().HasFilter("[IsDefault] = 1");
        });

        modelBuilder.Entity<EInvoiceDocument>(b =>
        {
            b.ToTable("EInvoiceDocuments");
            b.HasKey(d => d.Id);
            b.Property(d => d.SourceModule).HasMaxLength(40).IsUnicode(false);
            b.Property(d => d.SourceDocumentType).HasMaxLength(60).IsUnicode(false);
            b.Property(d => d.DocumentNumber).HasMaxLength(60);
            b.Property(d => d.TotalWithVat).HasPrecision(18, 2);
            b.Property(d => d.VatTotal).HasPrecision(18, 2);
            b.Property(d => d.BuyerName).HasMaxLength(200);
            b.Property(d => d.BuyerVatNumber).HasMaxLength(15).IsUnicode(false);
            b.Property(d => d.OriginalDocumentNumber).HasMaxLength(60);
            b.Property(d => d.PreviousInvoiceHash).HasMaxLength(100).IsUnicode(false);
            b.Property(d => d.InvoiceHash).HasMaxLength(100).IsUnicode(false);
            b.Property(d => d.QrCode).HasMaxLength(1000).IsUnicode(false);
            b.Property(d => d.ResponseMessage).HasMaxLength(2000);
            b.Ignore(d => d.IsStandard);
            b.HasIndex(d => new { d.TenantId, d.SourceModule, d.SourceDocumentType, d.SourceDocumentId }).IsUnique();
            b.HasIndex(d => new { d.TenantId, d.Uuid }).IsUnique();
            b.HasIndex(d => new { d.TenantId, d.DeviceId, d.Icv }).IsUnique();
            b.HasIndex(d => new { d.TenantId, d.Status, d.IssuedAt });
            b.HasOne<EInvoicingDevice>().WithMany().HasForeignKey(d => d.DeviceId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}

internal sealed class EInvoicingDbContextDesignTimeFactory : ModuleDesignTimeFactory<EInvoicingDbContext>
{
    protected override string Schema => EInvoicingDbContext.SchemaName;

    protected override EInvoicingDbContext Create(DbContextOptions<EInvoicingDbContext> options, ModuleDbContextDependencies dependencies) =>
        new(options, dependencies);
}
