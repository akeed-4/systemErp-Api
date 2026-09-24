using System.Linq.Expressions;
using System.Reflection;
using ERP.Core.Contracts.Shared;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ERP.Service.Data;

/// <summary>
/// سياق قاعدة البيانات الوحيد للنظام. تُسجَّل كل كيانات ERP.Core (الوارثة من BaseEntity) تلقائياً،
/// ويُطبَّق عليها مرشّح المنشأة (Tenant) عالمياً، وتُضبط القيود والفهارس هنا في مكان واحد.
/// </summary>
public class ErpDbContext : DbContext
{
    private readonly ITenantContext _tenant;

    public ErpDbContext(DbContextOptions<ErpDbContext> options, ITenantContext tenant) : base(options)
    {
        _tenant = tenant;
    }

    /// <summary>تُقرأ من نفس نسخة الـ DbContext لكل طلب حتى يُعاد تقييم مرشّح الاستعلام ولا يتجمّد على أول منشأة.</summary>
    public Guid CurrentTenantId => _tenant.TenantId ?? Guid.Empty;

    private static readonly Type[] EntityTypes = typeof(BaseEntity).Assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(BaseEntity).IsAssignableFrom(t))
        .ToArray();

    // (تابع، مفتاح أجنبي، أساسي) - علاقات بدون خاصية ملاحة تحتاج قيداً صريحاً في قاعدة البيانات.
    private static readonly (Type Dependent, string Fk, Type Principal)[] References =
    {
        (typeof(CarModel), nameof(CarModel.BrandId), typeof(CarBrand)),
        (typeof(CarTrim), nameof(CarTrim.ModelId), typeof(CarModel)),
        (typeof(CarYearModel), nameof(CarYearModel.TrimId), typeof(CarTrim)),
        (typeof(CarSalesContract), nameof(CarSalesContract.VehicleId), typeof(Vehicle)),
        (typeof(CarProcurementOrder), nameof(CarProcurementOrder.SupplierId), typeof(Supplier)),
        (typeof(CustomerLoyalty), nameof(CustomerLoyalty.CustomerId), typeof(Customer)),
        (typeof(PosTransaction), nameof(PosTransaction.ShiftId), typeof(PosShift)),
        (typeof(PosShift), nameof(PosShift.CashierId), typeof(User)),
        (typeof(PosSalesReturn), nameof(PosSalesReturn.OriginalTransactionId), typeof(PosTransaction)),
        (typeof(AppNotification), nameof(AppNotification.RecipientUserId), typeof(User)),
        (typeof(ApprovalRequest), nameof(ApprovalRequest.PolicyId), typeof(ApprovalPolicy)),
        (typeof(ApprovalRequest), nameof(ApprovalRequest.RequesterUserId), typeof(User)),
        (typeof(ApprovalHistoryItem), nameof(ApprovalHistoryItem.ApproverUserId), typeof(User)),
        (typeof(FixedAsset), nameof(FixedAsset.AssetAccountId), typeof(Account)),
        (typeof(FixedAsset), nameof(FixedAsset.AccumulatedDepreciationAccountId), typeof(Account)),
        (typeof(PasswordResetOtp), nameof(PasswordResetOtp.UserId), typeof(User)),
    };

    // فهارس فريدة على مستوى المنشأة (TenantId + الخاصية).
    private static readonly (Type Entity, string Property)[] UniquePerTenant =
    {
        (typeof(Customer), nameof(Customer.Code)),
        (typeof(Supplier), nameof(Supplier.Code)),
        (typeof(BankEntity), nameof(BankEntity.Code)),
        (typeof(Product), nameof(Product.Sku)),
        (typeof(ProductCategory), nameof(ProductCategory.Code)),
        (typeof(UnitOfMeasure), nameof(UnitOfMeasure.Code)),
        (typeof(PaymentMethodItem), nameof(PaymentMethodItem.Code)),
        (typeof(Currency), nameof(Currency.Code)),
        (typeof(Warehouse), nameof(Warehouse.Code)),
        (typeof(Account), nameof(Account.Code)),
        (typeof(CostCenter), nameof(CostCenter.Code)),
        (typeof(Invoice), nameof(Invoice.InvoiceNumber)),
        (typeof(JournalEntry), nameof(JournalEntry.EntryNumber)),
        (typeof(Voucher), nameof(Voucher.VoucherNumber)),
        (typeof(Quotation), nameof(Quotation.QuotationNumber)),
        (typeof(MaterialRequisition), nameof(MaterialRequisition.RequisitionNumber)),
        (typeof(CommercialOrder), nameof(CommercialOrder.OrderNumber)),
        (typeof(CommercialContract), nameof(CommercialContract.ContractNumber)),
        (typeof(CarProcurementOrder), nameof(CarProcurementOrder.OrderNumber)),
        (typeof(CarSalesContract), nameof(CarSalesContract.ContractNumber)),
        (typeof(Vehicle), nameof(Vehicle.ChassisNumber)),
        (typeof(PosShift), nameof(PosShift.ShiftNumber)),
        (typeof(PosTransaction), nameof(PosTransaction.InvoiceNumber)),
        (typeof(PosCoupon), nameof(PosCoupon.Code)),
        (typeof(NumberSequence), nameof(NumberSequence.Key)),
        (typeof(User), nameof(User.Email)),
    };

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach (var type in EntityTypes)
        {
            var entity = modelBuilder.Entity(type);
            entity.HasKey(nameof(BaseEntity.Id));
            entity.HasIndex(nameof(BaseEntity.TenantId));
            typeof(ErpDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(type).Invoke(this, new object[] { modelBuilder });

            // الخصائص المعقّدة (كائنات قيمة غير BaseEntity) تُخزَّن كأعمدة داخل جدول المالك.
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var pt = prop.PropertyType;
                if (pt.IsClass && pt != typeof(string) && pt.Namespace?.StartsWith("ERP.Core.Models") == true
                    && !typeof(BaseEntity).IsAssignableFrom(pt))
                    entity.OwnsOne(pt, prop.Name);
            }
        }

        // القيم المعدّدة كنصوص مقروءة في قاعدة البيانات.
        foreach (var et in modelBuilder.Model.GetEntityTypes())
            foreach (var p in et.GetProperties())
            {
                var t = Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType;
                if (t.IsEnum) p.SetProviderClrType(typeof(string));
            }

        modelBuilder.Entity<Account>().Ignore(a => a.Children);

        foreach (var (dep, fk, principal) in References)
            modelBuilder.Entity(dep).HasOne(principal).WithMany().HasForeignKey(fk).OnDelete(DeleteBehavior.Restrict);

        foreach (var (entity, prop) in UniquePerTenant)
            modelBuilder.Entity(entity).HasIndex(nameof(BaseEntity.TenantId), prop).IsUnique();

        modelBuilder.Entity<NumberSequence>().Property(s => s.Version).IsConcurrencyToken();

        // الأبناء المملوكون للتجميع يُحذفون معه، وأي علاقة أخرى تمنع الحذف (Restrict) لتفادي مسارات حذف متعددة.
        foreach (var fk in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
        {
            if (fk.IsOwnership) continue;
            var ownsAsChild = fk.PrincipalToDependent is { IsCollection: true };
            fk.DeleteBehavior = ownsAsChild ? DeleteBehavior.Cascade : DeleteBehavior.Restrict;
        }
    }

    private void ApplyTenantFilter<T>(ModelBuilder modelBuilder) where T : BaseEntity
        => modelBuilder.Entity<T>().HasQueryFilter(e => e.TenantId == CurrentTenantId);

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampEntities();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampEntities();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private void StampEntities()
    {
        var tenantId = _tenant.TenantId;
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity is Tenant t)
                    {
                        t.TenantId = t.Id; // المنشأة مرجعها الذاتي
                    }
                    else if (tenantId.HasValue)
                    {
                        if (entry.Entity.TenantId != Guid.Empty && entry.Entity.TenantId != tenantId.Value)
                            throw new InvalidOperationException("لا يمكن حفظ سجل ينتمي إلى منشأة أخرى.");
                        entry.Entity.TenantId = tenantId.Value;
                    }
                    else if (entry.Entity.TenantId == Guid.Empty)
                    {
                        throw new InvalidOperationException("لا يمكن حفظ سجل بدون منشأة (tenant).");
                    }
                    entry.Entity.CreatedAt = now;
                    break;
                case EntityState.Modified:
                    entry.Property(nameof(BaseEntity.TenantId)).IsModified = false;
                    entry.Property(nameof(BaseEntity.CreatedAt)).IsModified = false;
                    entry.Entity.UpdatedAt = now;
                    break;
            }
        }
    }
}
