using Erp.BuildingBlocks.Infrastructure.Persistence;
using Erp.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Erp.Modules.Settings.Persistence;

internal enum RecordStatus
{
    Active,
    Inactive,
}

/// <summary>settings.TenantSettings: typed global settings of one tenant (Id = TenantId). Module-specific settings live in their modules.</summary>
internal sealed class TenantSettings : TenantEntity
{
    private TenantSettings()
    {
    }

    public TenantSettings(Guid tenantId)
    {
        Id = tenantId;
    }

    public string DefaultLanguage { get; private set; } = "ar";

    public decimal DefaultVatRate { get; private set; } = 15m;

    public bool IsCarShowroomEnabled { get; private set; }

    public bool IsPosEnabled { get; private set; }

    public bool IsGeneralTradingEnabled { get; private set; } = true;

    public void Update(string language, decimal vatRate, bool carShowroom, bool pos, bool generalTrading)
    {
        DefaultLanguage = language;
        DefaultVatRate = vatRate;
        IsCarShowroomEnabled = carShowroom;
        IsPosEnabled = pos;
        IsGeneralTradingEnabled = generalTrading;
    }
}

internal sealed class Currency : TenantEntity
{
    private Currency()
    {
    }

    public Currency(string code, string nameAr, string nameEn, string symbol, bool isBase, decimal exchangeRate, int decimalPlaces, DateTimeOffset at)
    {
        Code = code.Trim().ToUpperInvariant();
        IsBaseCurrency = isBase;
        Update(nameAr, nameEn, symbol, exchangeRate, decimalPlaces, RecordStatus.Active, at);
    }

    public string Code { get; private set; } = string.Empty;

    public string NameAr { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    public string Symbol { get; private set; } = string.Empty;

    public bool IsBaseCurrency { get; private set; }

    public decimal ExchangeRate { get; private set; } = 1m;

    public int DecimalPlaces { get; private set; } = 2;

    public DateTimeOffset LastUpdated { get; private set; }

    public RecordStatus Status { get; private set; }

    public void Update(string nameAr, string nameEn, string symbol, decimal exchangeRate, int decimalPlaces, RecordStatus status, DateTimeOffset at)
    {
        NameAr = nameAr.Trim();
        NameEn = nameEn.Trim();
        Symbol = symbol.Trim();
        ExchangeRate = IsBaseCurrency ? 1m : exchangeRate;
        DecimalPlaces = decimalPlaces;
        Status = status;
        LastUpdated = at;
    }
}

internal sealed class DocumentSequence : TenantEntity
{
    private DocumentSequence()
    {
    }

    /// <summary>A new series whose first number (1) is consumed by the creator, so it starts at 2.</summary>
    public DocumentSequence(string documentType, Guid? scopeId, int year, string prefix, int padding)
    {
        DocumentType = documentType;
        ScopeId = scopeId;
        Year = year;
        Prefix = prefix;
        Padding = padding;
        NextNumber = 2;
    }

    public string DocumentType { get; private set; } = string.Empty;

    public Guid? ScopeId { get; private set; }

    public int Year { get; private set; }

    public string Prefix { get; private set; } = string.Empty;

    public long NextNumber { get; private set; }

    public int Padding { get; private set; }

    public void Configure(string prefix, int padding, long? nextNumber)
    {
        Prefix = prefix;
        Padding = padding;
        if (nextNumber is { } next && next > NextNumber)
        {
            NextNumber = next;
        }
    }
}

internal sealed class SettingsDbContext(DbContextOptions<SettingsDbContext> options, ModuleDbContextDependencies dependencies)
    : ModuleDbContext(options, dependencies)
{
    public const string SchemaName = "settings";

    public override string Schema => SchemaName;

    public DbSet<TenantSettings> TenantSettings => Set<TenantSettings>();

    public DbSet<Currency> Currencies => Set<Currency>();

    public DbSet<DocumentSequence> DocumentSequences => Set<DocumentSequence>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TenantSettings>(b =>
        {
            b.ToTable("TenantSettings");
            b.HasKey(s => s.Id);
            b.HasIndex(s => s.TenantId).IsUnique();
            b.Property(s => s.DefaultLanguage).HasMaxLength(5).IsUnicode(false);
            b.Property(s => s.DefaultVatRate).HasPrecision(9, 4);
        });

        modelBuilder.Entity<Currency>(b =>
        {
            b.ToTable("Currencies");
            b.HasKey(c => c.Id);
            b.Property(c => c.Code).HasMaxLength(3).IsUnicode(false);
            b.Property(c => c.NameAr).HasMaxLength(100);
            b.Property(c => c.NameEn).HasMaxLength(100);
            b.Property(c => c.Symbol).HasMaxLength(10);
            b.Property(c => c.ExchangeRate).HasPrecision(18, 6);
            b.HasIndex(c => new { c.TenantId, c.Code }).IsUnique();
            b.HasIndex(c => new { c.TenantId, c.IsBaseCurrency }).IsUnique().HasFilter("[IsBaseCurrency] = 1");
        });

        modelBuilder.Entity<DocumentSequence>(b =>
        {
            b.ToTable("DocumentSequences");
            b.HasKey(s => s.Id);
            b.Property(s => s.DocumentType).HasMaxLength(60).IsUnicode(false);
            b.Property(s => s.Prefix).HasMaxLength(40);
            b.HasIndex(s => new { s.TenantId, s.DocumentType, s.ScopeId, s.Year }).IsUnique();
        });
    }
}

internal sealed class SettingsDbContextDesignTimeFactory : ModuleDesignTimeFactory<SettingsDbContext>
{
    protected override string Schema => SettingsDbContext.SchemaName;

    protected override SettingsDbContext Create(DbContextOptions<SettingsDbContext> options, ModuleDbContextDependencies dependencies) =>
        new(options, dependencies);
}
