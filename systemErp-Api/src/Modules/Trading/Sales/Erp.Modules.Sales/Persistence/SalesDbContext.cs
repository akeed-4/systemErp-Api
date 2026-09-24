using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.Modules.Sales.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Modules.Sales.Persistence;

internal sealed class SalesDbContext(DbContextOptions<SalesDbContext> options, ModuleDbContextDependencies dependencies)
    : ModuleDbContext(options, dependencies)
{
    public const string SchemaName = "sales";

    public override string Schema => SchemaName;

    public DbSet<SalesQuotation> Quotations => Set<SalesQuotation>();

    public DbSet<SalesOrder> Orders => Set<SalesOrder>();

    public DbSet<SalesInvoice> Invoices => Set<SalesInvoice>();

    public DbSet<SalesInvoiceLine> InvoiceLines => Set<SalesInvoiceLine>();

    public DbSet<SalesNote> Notes => Set<SalesNote>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SalesQuotation>(b =>
        {
            b.ToTable("SalesQuotations");
            ConfigureDocument(b);
            b.Property(q => q.QuotationNumber).HasMaxLength(40).IsUnicode(false);
            b.Property(q => q.PaymentTerms).HasMaxLength(500);
            b.Property(q => q.TermsAndConditions).HasMaxLength(4000);
            b.HasIndex(q => new { q.TenantId, q.QuotationNumber }).IsUnique();
            b.HasIndex(q => new { q.TenantId, q.Date });
            b.HasMany(q => q.Lines).WithOne().HasForeignKey(l => l.QuotationId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(q => q.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        modelBuilder.Entity<SalesQuotationLine>(b =>
        {
            b.ToTable("SalesQuotationLines");
            ConfigureLine(b);
        });

        modelBuilder.Entity<SalesOrder>(b =>
        {
            b.ToTable("SalesOrders");
            ConfigureDocument(b);
            b.Property(o => o.OrderNumber).HasMaxLength(40).IsUnicode(false);
            b.Property(o => o.PaymentTerms).HasMaxLength(500);
            b.HasIndex(o => new { o.TenantId, o.OrderNumber }).IsUnique();
            b.HasIndex(o => new { o.TenantId, o.AgreementId }).HasFilter("[AgreementId] IS NOT NULL");
            b.HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        modelBuilder.Entity<SalesOrderLine>(b =>
        {
            b.ToTable("SalesOrderLines");
            ConfigureLine(b);
        });

        modelBuilder.Entity<SalesInvoice>(b =>
        {
            b.ToTable("SalesInvoices");
            ConfigureDocument(b);
            b.Property(i => i.InvoiceNumber).HasMaxLength(40).IsUnicode(false);
            b.Property(i => i.CurrencyCode).HasMaxLength(3).IsUnicode(false);
            b.Property(i => i.ExchangeRate).HasPrecision(18, 6);
            b.Property(i => i.TotalCost).HasPrecision(18, 2);
            b.Property(i => i.GrossProfit).HasPrecision(18, 2);
            b.Property(i => i.ReturnedTotal).HasPrecision(18, 2);
            b.Property(i => i.SourceModule).HasMaxLength(40).IsUnicode(false);
            b.Property(i => i.SourceDocumentType).HasMaxLength(60).IsUnicode(false);
            b.Property(i => i.SourceDocumentNumber).HasMaxLength(60);
            b.Property(i => i.QrCode).HasMaxLength(1000).IsUnicode(false);
            b.Property(i => i.CancellationReason).HasMaxLength(500);
            b.Ignore(i => i.PaidAtIssue);
            b.HasIndex(i => new { i.TenantId, i.InvoiceNumber }).IsUnique().HasFilter("[InvoiceNumber] IS NOT NULL");
            b.HasIndex(i => new { i.TenantId, i.IssueDate });
            b.HasIndex(i => new { i.TenantId, i.CustomerId });
            b.HasIndex(i => new { i.TenantId, i.SourceModule, i.SourceDocumentType, i.SourceDocumentId }).HasFilter("[SourceDocumentId] IS NOT NULL");
            b.HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(i => i.Payments).WithOne().HasForeignKey(p => p.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(i => i.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
            b.Navigation(i => i.Payments).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        modelBuilder.Entity<SalesInvoiceLine>(b =>
        {
            b.ToTable("SalesInvoiceLines");
            ConfigureLine(b);
            b.Property(l => l.UnitCost).HasPrecision(18, 4);
            b.Property(l => l.TotalCost).HasPrecision(18, 2);
            b.Property(l => l.ReturnedQuantity).HasPrecision(18, 3);
            b.Ignore(l => l.ReturnableQuantity);
        });
        modelBuilder.Entity<SalesInvoicePayment>(b =>
        {
            b.ToTable("SalesInvoicePayments");
            b.HasKey(p => p.Id);
            b.Property(p => p.Amount).HasPrecision(18, 2);
            b.Property(p => p.Reference).HasMaxLength(100);
        });

        modelBuilder.Entity<SalesNote>(b =>
        {
            b.ToTable("SalesNotes");
            b.HasKey(n => n.Id);
            b.Property(n => n.NoteNumber).HasMaxLength(40).IsUnicode(false);
            b.Property(n => n.OriginalInvoiceNumber).HasMaxLength(40).IsUnicode(false);
            b.Property(n => n.PartyName).HasMaxLength(200);
            b.Property(n => n.Reason).HasMaxLength(500);
            b.Property(n => n.NetTotal).HasPrecision(18, 2);
            b.Property(n => n.VatTotal).HasPrecision(18, 2);
            b.Property(n => n.GrandTotal).HasPrecision(18, 2);
            b.Property(n => n.TotalCost).HasPrecision(18, 2);
            b.Property(n => n.QrCode).HasMaxLength(1000).IsUnicode(false);
            b.HasIndex(n => new { n.TenantId, n.NoteNumber }).IsUnique();
            b.HasOne<SalesInvoice>().WithMany().HasForeignKey(n => n.OriginalInvoiceId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(n => n.Lines).WithOne().HasForeignKey(l => l.NoteId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(n => n.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        modelBuilder.Entity<SalesNoteLine>(b =>
        {
            b.ToTable("SalesNoteLines");
            b.HasKey(l => l.Id);
            b.Property(l => l.Description).HasMaxLength(500);
            b.Property(l => l.Quantity).HasPrecision(18, 3);
            b.Property(l => l.UnitPrice).HasPrecision(18, 2);
            b.Property(l => l.NetAmount).HasPrecision(18, 2);
            b.Property(l => l.VatRate).HasPrecision(9, 4);
            b.Property(l => l.VatAmount).HasPrecision(18, 2);
            b.Property(l => l.TotalWithVat).HasPrecision(18, 2);
            b.Property(l => l.UnitCost).HasPrecision(18, 4);
            b.Property(l => l.TotalCost).HasPrecision(18, 2);
        });
    }

    private static void ConfigureDocument<T>(EntityTypeBuilder<T> b)
        where T : SalesDocument
    {
        b.HasKey(d => d.Id);
        b.Property(d => d.PartyName).HasMaxLength(200);
        b.Property(d => d.PartyVatNumber).HasMaxLength(15).IsUnicode(false);
        b.Property(d => d.PartyCrNumber).HasMaxLength(20).IsUnicode(false);
        b.Property(d => d.PartyPhone).HasMaxLength(32);
        b.Property(d => d.PartyEmail).HasMaxLength(256);
        b.Property(d => d.PartyAddress).HasMaxLength(400);
        b.Property(d => d.GrossTotal).HasPrecision(18, 2);
        b.Property(d => d.ItemsDiscountTotal).HasPrecision(18, 2);
        b.Property(d => d.InvoiceDiscount).HasPrecision(18, 2);
        b.Property(d => d.DiscountTotal).HasPrecision(18, 2);
        b.Property(d => d.NetTotal).HasPrecision(18, 2);
        b.Property(d => d.VatTotal).HasPrecision(18, 2);
        b.Property(d => d.GrandTotal).HasPrecision(18, 2);
        b.Property(d => d.Notes).HasMaxLength(2000);
        b.Property(d => d.RowVersion).IsRowVersion();
    }

    private static void ConfigureLine<T>(EntityTypeBuilder<T> b)
        where T : SalesLine
    {
        b.HasKey(l => l.Id);
        b.Property(l => l.Description).HasMaxLength(500);
        b.Property(l => l.Unit).HasMaxLength(40);
        b.Property(l => l.Quantity).HasPrecision(18, 3);
        b.Property(l => l.UnitPrice).HasPrecision(18, 2);
        b.Property(l => l.Discount).HasPrecision(18, 2);
        b.Property(l => l.AllocatedInvoiceDiscount).HasPrecision(18, 2);
        b.Property(l => l.VatRate).HasPrecision(9, 4);
        b.Property(l => l.NetAmount).HasPrecision(18, 2);
        b.Property(l => l.VatAmount).HasPrecision(18, 2);
        b.Property(l => l.TotalWithVat).HasPrecision(18, 2);
    }
}

internal sealed class SalesDbContextDesignTimeFactory : ModuleDesignTimeFactory<SalesDbContext>
{
    protected override string Schema => SalesDbContext.SchemaName;

    protected override SalesDbContext Create(DbContextOptions<SalesDbContext> options, ModuleDbContextDependencies dependencies) =>
        new(options, dependencies);
}
