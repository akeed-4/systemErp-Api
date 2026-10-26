using System.Linq.Expressions;
using System.Reflection;
using ERP.Core.Contracts.Shared;
using ERP.Service.Services.Shared;
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

    /// <summary>للسياق الموجَّه بالمنشأة (<see cref="TenantErpDbContext"/>): النموذج نفسه بخيارات نوعه.</summary>
    protected ErpDbContext(DbContextOptions options, ITenantContext tenant) : base(options)
    {
        _tenant = tenant;
    }

    /// <summary>تُقرأ من نفس نسخة الـ DbContext لكل طلب حتى يُعاد تقييم مرشّح الاستعلام ولا يتجمّد على أول منشأة.</summary>
    public Guid CurrentTenantId => _tenant.TenantId ?? Guid.Empty;

    /// <summary>
    /// القاعدة المشتركة (الدليل): بيانات المنصة (الباقات، الوحدات، ربط القواعد) والقراءات العابرة للمنشآت
    /// (الدخول، تفرّد البريد والرقم الضريبي، لوحة المنصة، إشعار الدفع). هنا هي نفسها؛ السياق الموجَّه يعيد سياقاً مستقلاً عليها.
    /// </summary>
    public virtual ErpDbContext Catalog => this;

    /// <summary>نسخ سجلات الدليل بين القاعدتين: تُحفظ كما هي (المنشأة وتاريخ الإنشاء) بلا ختم جديد.</summary>
    internal bool PreserveStamps { get; set; }

    /// <summary>
    /// جذور الوراثة بنمط TPC (جدول فعلي مستقل لكل نوع مشتق، ولا جدول للجذر): المفتاح ومرشّح المنشأة على الجذر وحده،
    /// والاستعلام عن الجذر يجمع جداوله كلها. الفواتير: Invoice (المعارض) وجداول التجارة العامة الأربعة، وكذلك بنودها.
    /// </summary>
    private static readonly Type[] TpcRoots = { typeof(InvoiceBase), typeof(InvoiceItemBase) };

    private static readonly Type[] EntityTypes = typeof(BaseEntity).Assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(BaseEntity).IsAssignableFrom(t))
        .ToArray();

    private static bool IsTpcDerived(Type t) => TpcRoots.Any(root => root != t && root.IsAssignableFrom(t));

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
        (typeof(FixedAsset), nameof(FixedAsset.DepreciationExpenseAccountId), typeof(Account)),
        (typeof(FixedAsset), nameof(FixedAsset.CostCenterId), typeof(CostCenter)),
        (typeof(FixedAsset), nameof(FixedAsset.WarehouseId), typeof(Warehouse)),
        (typeof(FixedAssetDepreciation), nameof(FixedAssetDepreciation.FixedAssetId), typeof(FixedAsset)),
        (typeof(FixedAssetDepreciation), nameof(FixedAssetDepreciation.CostCenterId), typeof(CostCenter)),
        (typeof(FixedAssetDepreciation), nameof(FixedAssetDepreciation.WarehouseId), typeof(Warehouse)),
        // VoucherAllocation.InvoiceId: الفاتورة في أحد جداول InvoiceBase (TPC)، فلا قيد قاعدة بيانات عليه؛ يُتحقق منه برمجياً.
        (typeof(PayrollLine), nameof(PayrollLine.EmployeeId), typeof(Employee)),
        (typeof(Employee), nameof(Employee.CostCenterId), typeof(CostCenter)),
        (typeof(CostCenter), nameof(CostCenter.ParentId), typeof(CostCenter)),
        (typeof(Employee), nameof(Employee.DepartmentId), typeof(Department)),
        (typeof(LeaveRequest), nameof(LeaveRequest.EmployeeId), typeof(Employee)),
        (typeof(LeaveRequest), nameof(LeaveRequest.LeaveTypeId), typeof(LeaveType)),
        (typeof(LeaveAttachment), nameof(LeaveAttachment.LeaveRequestId), typeof(LeaveRequest)),
        (typeof(Employee), nameof(Employee.UserId), typeof(User)),
        (typeof(UserInvite), nameof(UserInvite.UserId), typeof(User)),
        (typeof(ZatcaUnit), nameof(ZatcaUnit.UserId), typeof(User)),
        (typeof(AttendanceRecord), nameof(AttendanceRecord.EmployeeId), typeof(Employee)),
        (typeof(EmployeeLoan), nameof(EmployeeLoan.EmployeeId), typeof(Employee)),
        (typeof(EmployeeLoanRepayment), nameof(EmployeeLoanRepayment.EmployeeLoanId), typeof(EmployeeLoan)),
        (typeof(EndOfServiceSettlement), nameof(EndOfServiceSettlement.EmployeeId), typeof(Employee)),
        (typeof(WarehouseStock), nameof(WarehouseStock.ItemId), typeof(Product)),
        (typeof(WarehouseStock), nameof(WarehouseStock.WarehouseId), typeof(Warehouse)),
        (typeof(StockTransfer), nameof(StockTransfer.FromWarehouseId), typeof(Warehouse)),
        (typeof(StockTransfer), nameof(StockTransfer.ToWarehouseId), typeof(Warehouse)),
        (typeof(StockTransferItem), nameof(StockTransferItem.ItemId), typeof(Product)),
        (typeof(InvoiceBase), nameof(InvoiceBase.WarehouseId), typeof(Warehouse)),
        (typeof(PosInvoiceSettings), nameof(PosInvoiceSettings.WarehouseId), typeof(Warehouse)),
        (typeof(PasswordResetOtp), nameof(PasswordResetOtp.UserId), typeof(User)),
        (typeof(InventoryCountApproval), nameof(InventoryCountApproval.InventoryCountId), typeof(InventoryCount)),
        (typeof(AdditionalChargeLine), nameof(AdditionalChargeLine.ExpenseTypeId), typeof(ExpenseType)),
        (typeof(AdditionalChargeLine), nameof(AdditionalChargeLine.SupplierId), typeof(Supplier)),
        (typeof(AdditionalChargeAllocation), nameof(AdditionalChargeAllocation.VehicleId), typeof(Vehicle)),
        (typeof(LandedCostVoucher), nameof(LandedCostVoucher.SupplierId), typeof(Supplier)),
        (typeof(LandedCostVoucher), nameof(LandedCostVoucher.ProcurementOrderId), typeof(CarProcurementOrder)),
        (typeof(LandedCostVoucherLine), nameof(LandedCostVoucherLine.ExpenseTypeId), typeof(ExpenseType)),
        (typeof(LandedCostVoucherAllocation), nameof(LandedCostVoucherAllocation.VehicleId), typeof(Vehicle)),
        (typeof(VehicleReservation), nameof(VehicleReservation.VehicleId), typeof(Vehicle)),
        (typeof(VehicleReservation), nameof(VehicleReservation.CustomerId), typeof(Customer)),
        (typeof(TammOperation), nameof(TammOperation.VehicleId), typeof(Vehicle)),
        (typeof(VehicleCostAdjustment), nameof(VehicleCostAdjustment.VehicleId), typeof(Vehicle)),
        (typeof(PrepaidExpense), nameof(PrepaidExpense.ExpenseTypeId), typeof(ExpenseType)),
        (typeof(PrepaidExpense), nameof(PrepaidExpense.SupplierId), typeof(Supplier)),
        (typeof(ImportCostAdvance), nameof(ImportCostAdvance.ProcurementOrderId), typeof(CarProcurementOrder)),
        (typeof(ImportCostAdvance), nameof(ImportCostAdvance.ExpenseTypeId), typeof(ExpenseType)),
        (typeof(ImportCostAdvance), nameof(ImportCostAdvance.SupplierId), typeof(Supplier)),
        (typeof(LandedCostVoucherLine), nameof(LandedCostVoucherLine.ImportCostAdvanceId), typeof(ImportCostAdvance)),
        (typeof(ContractApproval), nameof(ContractApproval.ContractId), typeof(CommercialContract)),
        (typeof(ContractStatusLog), nameof(ContractStatusLog.ContractId), typeof(CommercialContract)),
        (typeof(ContractInvoice), nameof(ContractInvoice.ContractId), typeof(CommercialContract)),
        (typeof(ContractAttachment), nameof(ContractAttachment.ContractId), typeof(CommercialContract)),
        // ContractInvoice.InvoiceId: الفاتورة في أحد جداول InvoiceBase (TPC)، فلا قيد قاعدة بيانات عليه.
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
        (typeof(InvoiceBase), nameof(InvoiceBase.InvoiceNumber)), // فريد في كل جدول فواتير
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
        (typeof(InventoryCount), nameof(InventoryCount.CountNumber)),
        (typeof(StockTransfer), nameof(StockTransfer.TransferNumber)),
        (typeof(Employee), nameof(Employee.Code)),
        (typeof(Department), nameof(Department.Code)),
        (typeof(LeaveType), nameof(LeaveType.Code)),
        (typeof(LeaveRequest), nameof(LeaveRequest.RequestNumber)),
        (typeof(EmployeeLoan), nameof(EmployeeLoan.LoanNumber)),
        (typeof(EndOfServiceSettlement), nameof(EndOfServiceSettlement.SettlementNumber)),
        (typeof(PayrollRun), nameof(PayrollRun.RunNumber)),
        (typeof(InventoryCountApproval), nameof(InventoryCountApproval.ApprovalNumber)),
        (typeof(NumberSequence), nameof(NumberSequence.Key)),
        (typeof(ExpenseType), nameof(ExpenseType.Code)),
        (typeof(LandedCostVoucher), nameof(LandedCostVoucher.VoucherNumber)),
        (typeof(TammOperation), nameof(TammOperation.OperationNumber)),
        (typeof(VehicleCostAdjustment), nameof(VehicleCostAdjustment.AdjustmentNumber)),
        (typeof(PrepaidExpense), nameof(PrepaidExpense.Number)),
        (typeof(ImportCostAdvance), nameof(ImportCostAdvance.Number)),
        (typeof(User), nameof(User.Email)),
    };

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach (var root in TpcRoots)
            modelBuilder.Entity(root).UseTpcMappingStrategy();

        foreach (var type in TpcRoots.Concat(EntityTypes))
        {
            var entity = modelBuilder.Entity(type);
            if (IsTpcDerived(type)) continue; // يرث المفتاح والفهرس والمرشّح من جذره
            entity.HasKey(nameof(BaseEntity.Id));
            entity.HasIndex(nameof(BaseEntity.TenantId));
            typeof(ErpDbContext).GetMethod(FilterMethodFor(type), BindingFlags.NonPublic | BindingFlags.Instance)!
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

        // كتالوج الباقات على مستوى المنصة (ليس تابعاً لمنشأة): بلا مرشّح منشأة، ويُزرع بالقيم الافتراضية.
        // معرّف الباقة رمز نصي (SubscriptionPlanId) يطابق Subscription.PlanType و OnlinePayment.PlanId.
        modelBuilder.Entity<PlanDefinition>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).ValueGeneratedNever();
            e.HasData(SubscriptionCatalog.Seed);
        });

        modelBuilder.Entity<PlatformModuleDefinition>(e =>
        {
            e.HasKey(m => m.Key);
            e.Property(m => m.Key).HasMaxLength(64);
            e.Property(m => m.NameAr).HasMaxLength(200).IsRequired();
            e.Property(m => m.NameEn).HasMaxLength(200).IsRequired();
            e.HasIndex(m => m.Number).IsUnique();
            e.HasData(PlatformModules.Catalog);
        });

        // ربط المنشأة بقاعدتها المستقلة (على مستوى المنصة، بلا مرشّح منشأة): يُقرأ ويُكتب على القاعدة المشتركة وحدها.
        modelBuilder.Entity<TenantDatabase>(e =>
        {
            e.HasKey(d => d.TenantId);
            e.Property(d => d.TenantId).ValueGeneratedNever();
            e.Property(d => d.DatabaseName).HasMaxLength(128).IsRequired();
            e.Property(d => d.ConnectionStringEncrypted).IsRequired();
            e.Property(d => d.LastError).HasMaxLength(1000);
        });

        modelBuilder.Entity<PlatformSetting>(e =>
        {
            e.HasKey(s => s.Key);
            e.Property(s => s.Key).HasMaxLength(64);
            e.Property(s => s.Value).HasMaxLength(1000).IsRequired();
            e.Property(s => s.UpdatedBy).HasMaxLength(256);
        });

        modelBuilder.Entity<ContractStageDefinition>(e =>
        {
            e.ToTable("ContractStageDefinition");
            e.HasKey(s => s.Id);
            e.Property(s => s.Id).HasMaxLength(64);
            e.Property(s => s.WorkflowType).HasMaxLength(64).IsRequired();
            e.Property(s => s.NameAr).HasMaxLength(200).IsRequired();
            e.Property(s => s.NameEn).HasMaxLength(200).IsRequired();
            e.Property(s => s.DescAr).HasMaxLength(1000).IsRequired();
            e.Property(s => s.DescEn).HasMaxLength(1000).IsRequired();
            e.Property(s => s.Icon).HasMaxLength(100).IsRequired();
            e.Property(s => s.BadgeColor).HasMaxLength(300).IsRequired();
            e.Property(s => s.HeaderColor).HasMaxLength(200).IsRequired();
            e.Property(s => s.AllowedActions).HasMaxLength(300).IsRequired();
            e.Property(s => s.ShortNameAr).HasMaxLength(50).IsRequired();
            e.Property(s => s.ShortNameEn).HasMaxLength(50).IsRequired();
            e.Property(s => s.Phase).HasMaxLength(20).IsRequired();
            e.HasIndex(s => new { s.WorkflowType, s.Number }).IsUnique();
            e.HasData(ContractStageCatalogSeed.All);
        });

        // انتقالات حالات العقد: كتالوج عام يُزرع مع الترحيل ويُعدَّل من قاعدة البيانات
        modelBuilder.Entity<ContractTransition>(e =>
        {
            e.ToTable("ContractTransition");
            e.HasKey(t => t.Id);
            e.Property(t => t.Id).ValueGeneratedNever();
            e.Property(t => t.FromStatus).HasMaxLength(64).IsRequired();
            e.Property(t => t.ToStatus).HasMaxLength(64).IsRequired();
            e.Property(t => t.NameAr).HasMaxLength(200).IsRequired();
            e.Property(t => t.NameEn).HasMaxLength(200).IsRequired();
            e.Property(t => t.RequiredActions).HasMaxLength(200).IsRequired();
            e.Property(t => t.Condition).HasMaxLength(64);
            e.Property(t => t.Tone).HasMaxLength(16).IsRequired();
            e.HasIndex(t => new { t.FromStatus, t.ToStatus }).IsUnique();
            e.HasData(ContractTransitionSeed.All);
        });

        // أنواع العقود لكل منشأة: الرمز فريد ويُحفظ في CommercialContract.ContractType
        modelBuilder.Entity<ContractTypeDefinition>(e =>
        {
            e.Property(t => t.Code).HasMaxLength(64).IsRequired();
            e.Property(t => t.NameAr).HasMaxLength(200).IsRequired();
            e.Property(t => t.NameEn).HasMaxLength(200).IsRequired();
            e.Property(t => t.Workflow).HasMaxLength(16).IsRequired();
            e.Property(t => t.Billing).HasMaxLength(16).IsRequired();
            e.HasIndex(t => new { t.TenantId, t.Code }).IsUnique();
        });
        modelBuilder.Entity<ContractApproval>().HasIndex(a => new { a.TenantId, a.ContractId, a.Round });
        modelBuilder.Entity<ContractStatusLog>().HasIndex(l => new { l.TenantId, l.ContractId });
        modelBuilder.Entity<ContractInvoice>().HasIndex(i => new { i.TenantId, i.ContractId });
        // بيان الاستلام لا يُفوتر مرتين (فوترة بعد الاستلام)
        modelBuilder.Entity<ContractInvoice>().HasIndex(i => new { i.TenantId, i.DeliveryNoteId, i.Kind }).IsUnique()
            .HasFilter("[DeliveryNoteId] IS NOT NULL AND [Kind] <> N'credit_note'");
        modelBuilder.Entity<ContractAttachment>().HasIndex(a => new { a.TenantId, a.ContractId });

        modelBuilder.Entity<CarStageDefinition>(e =>
        {
            e.ToTable("CarStageDefinition");
            e.HasKey(s => new { s.WorkflowType, s.Id });
            e.Property(s => s.WorkflowType).HasMaxLength(64);
            e.Property(s => s.Id).HasMaxLength(64);
            e.Property(s => s.NameAr).HasMaxLength(200).IsRequired();
            e.Property(s => s.NameEn).HasMaxLength(200).IsRequired();
            e.Property(s => s.DescAr).HasMaxLength(1000).IsRequired();
            e.Property(s => s.DescEn).HasMaxLength(1000).IsRequired();
            e.Property(s => s.Icon).HasMaxLength(100).IsRequired();
            e.Property(s => s.Color).HasMaxLength(50).IsRequired();
            e.HasIndex(s => new { s.WorkflowType, s.Number }).IsUnique();
            e.HasData(CarStageCatalogSeed.All);
        });

        // القيم المعدّدة كنصوص مقروءة في قاعدة البيانات.
        foreach (var et in modelBuilder.Model.GetEntityTypes())
            foreach (var p in et.GetProperties())
            {
                var t = Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType;
                if (t.IsEnum) p.SetProviderClrType(typeof(string));
            }

        // الدخول يبحث بالبريد أو الجوال عبر كل المنشآت، والفهرس المركّب (TenantId, Email) لا يخدم هذا البحث.
        modelBuilder.Entity<User>().HasIndex(u => u.Email);
        modelBuilder.Entity<User>().HasIndex(u => u.Phone);

        modelBuilder.Entity<Account>().Ignore(a => a.Children);

        // الصلاحيات: رمز الدور فريد في المنشأة، وسجل التغييرات يُقرأ بالأحدث أولاً
        modelBuilder.Entity<RoleDefinition>(e =>
        {
            e.Property(r => r.Code).HasMaxLength(64).IsRequired();
            e.Property(r => r.NameAr).HasMaxLength(200).IsRequired();
            e.Property(r => r.NameEn).HasMaxLength(200).IsRequired();
            e.Property(r => r.Description).HasMaxLength(1000);
            e.Property(r => r.UpdatedBy).HasMaxLength(256);
            e.HasIndex(r => new { r.TenantId, r.Code }).IsUnique();
        });
        modelBuilder.Entity<PermissionChangeLog>(e =>
        {
            e.Property(l => l.ChangedByName).HasMaxLength(256);
            e.Property(l => l.TargetType).HasMaxLength(16);
            e.Property(l => l.TargetId).HasMaxLength(64);
            e.Property(l => l.TargetName).HasMaxLength(256);
            e.Property(l => l.Permission).HasMaxLength(128);
            e.Property(l => l.OldValue).HasMaxLength(1000);
            e.Property(l => l.NewValue).HasMaxLength(1000);
            e.HasIndex(l => new { l.TenantId, l.Timestamp });
        });
        modelBuilder.Entity<UserRolePermission>().Property(p => p.Scope).HasDefaultValue(DataScope.All);
        modelBuilder.Entity<ScreenPermissionItem>().Property(p => p.Actions).HasMaxLength(1000);

        foreach (var (dep, fk, principal) in References)
            modelBuilder.Entity(dep).HasOne(principal).WithMany().HasForeignKey(fk).OnDelete(DeleteBehavior.Restrict);

        foreach (var (entity, prop) in UniquePerTenant)
            modelBuilder.Entity(entity).HasIndex(nameof(BaseEntity.TenantId), prop).IsUnique();

        // الأصل لا يُرحَّل إهلاكه مرتين لنفس الفترة (ضمان على مستوى قاعدة البيانات).
        modelBuilder.Entity<FixedAssetDepreciation>()
            .HasIndex(d => new { d.TenantId, d.FixedAssetId, d.Period }).IsUnique();

        // عملية تم الواحدة لا تُنشئ مصروفها مرتين (ضمان على مستوى قاعدة البيانات).
        modelBuilder.Entity<AdditionalChargeLine>()
            .HasIndex(l => new { l.TenantId, l.TammOperationId }).IsUnique().HasFilter("[TammOperationId] IS NOT NULL");
        modelBuilder.Entity<AdditionalChargeLine>().HasIndex(l => new { l.TenantId, l.DocumentType, l.DocumentId });
        modelBuilder.Entity<AdditionalChargeAllocation>().HasIndex(a => new { a.TenantId, a.VehicleId });

        // حجز فعّال واحد لكل سيارة، وصف إعدادات واحد لكل منشأة.
        modelBuilder.Entity<VehicleReservation>()
            .HasIndex(r => new { r.TenantId, r.VehicleId }).IsUnique().HasFilter("[Status] = N'Active'");
        modelBuilder.Entity<CarShowroomSettings>().HasIndex(s => s.TenantId).IsUnique();

        // المصروف المدفوع مقدماً لا يُطفأ مرتين للشهر نفسه (ضمان على مستوى قاعدة البيانات).
        modelBuilder.Entity<PrepaidExpenseAmortization>()
            .HasIndex(a => new { a.TenantId, a.PrepaidExpenseId, a.Period }).IsUnique();

        // الأرصدة والمخزون: تعارض التزامن يُرفض (ويُعاد تنفيذ المعاملة) بدل أن يُكتب رصيد فوق رصيد
        modelBuilder.Entity<Account>().Property(a => a.RowVersion).IsRowVersion();
        modelBuilder.Entity<Product>().Property(p => p.RowVersion).IsRowVersion();
        // رصيد واحد لكل صنف في كل مستودع
        modelBuilder.Entity<WarehouseStock>().Property(s => s.RowVersion).IsRowVersion();
        modelBuilder.Entity<WarehouseStock>().HasIndex(s => new { s.TenantId, s.ItemId, s.WarehouseId }).IsUnique();
        modelBuilder.Entity<InvoiceItemBase>().Property(i => i.VatCategory).HasDefaultValue(VatCategory.Standard);
        // توزيع السند يُقرأ بفاتورته (المرجع بلا قيد لأن الفاتورة في أحد جداول InvoiceBase)
        modelBuilder.Entity<VoucherAllocation>().HasIndex(a => a.InvoiceId);
        modelBuilder.Entity<Product>().Property(p => p.VatCategory).HasDefaultValue(VatCategory.Standard);
        // الفواتير القائمة «أساسي»: كل سطر بتصنيفه كما رُحِّل
        foreach (var t in new[] { typeof(Invoice), typeof(SalesInvoice), typeof(PurchaseInvoice), typeof(SalesReturn), typeof(PurchaseReturn) })
            modelBuilder.Entity(t).Property(nameof(InvoiceBase.VatTreatment)).HasDefaultValue(InvoiceVatTreatment.Standard);
        // الحسابات القائمة «اختياري» (سلوكها قبل الإعداد)، وتقارير المراكز تقرأ سطور القيود بمركزها
        modelBuilder.Entity<Account>().Property(a => a.CostCenterPolicy).HasDefaultValue(CostCenterPolicy.Optional);
        modelBuilder.Entity<JournalEntryLine>().HasIndex(l => new { l.TenantId, l.CostCenterId });

        // شواصي الدفعة وبطاقات فاتورة الشراء تُقرأ جماعياً بمعرّف الدفعة/الفاتورة
        modelBuilder.Entity<InvoiceVehicleLine>().HasIndex(l => l.BatchId);
        modelBuilder.Entity<Vehicle>().HasIndex(v => new { v.TenantId, v.PurchaseInvoiceId });

        modelBuilder.Entity<NumberSequence>().Property(s => s.Version).IsConcurrencyToken();
        modelBuilder.Entity<ZatcaUnit>().Property(c => c.Version).IsConcurrencyToken();
        modelBuilder.Entity<InventoryCountApproval>().Property(a => a.Version).IsConcurrencyToken();

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

    // ---------- نطاق الوصول ----------
    // المستندات ذات النطاق (IScopedRecord) يُضاف لمرشّح منشأتها نطاق المستخدم، ويبقى معطّلاً (ScopeMode = 0) ما لم يفعّله
    // مرشّح ScopedRecord في طلبات القراءة؛ فالترحيل والأرصدة والتقارير الداخلية لا تتأثر به.

    /// <summary>نطاق الطلب الحالي (null = كل السجلات).</summary>
    public DataScopeFilter? ActiveDataScope { get; private set; }
    public int ScopeMode { get; private set; }
    public Guid ScopeUserId { get; private set; }
    public List<Guid> ScopeWarehouseIds { get; private set; } = new();
    public List<Guid> ScopeUserIds { get; private set; } = new();

    public void ApplyDataScope(DataScopeFilter? scope)
    {
        ActiveDataScope = scope;
        ScopeMode = scope == null ? 0 : (int)scope.Scope;
        ScopeUserId = scope?.UserId ?? Guid.Empty;
        ScopeWarehouseIds = scope?.WarehouseIds.ToList() ?? new List<Guid>();
        ScopeUserIds = scope == null ? new List<Guid>() : scope.DepartmentUserIds.Append(scope.UserId).Distinct().ToList();
    }

    /// <summary>يعطّل النطاق مؤقتاً (قراءة سجل لفحص صلاحيته) ويعيده عند التخلّص.</summary>
    public IDisposable SuspendDataScope()
    {
        var saved = ActiveDataScope;
        ApplyDataScope(null);
        return new ScopeRestorer(() => ApplyDataScope(saved));
    }

    private sealed class ScopeRestorer(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    private static string FilterMethodFor(Type type)
    {
        if (!typeof(IScopedRecord).IsAssignableFrom(type)) return nameof(ApplyTenantFilter);
        return type.GetProperty("WarehouseId")?.PropertyType == typeof(Guid?) ? nameof(ApplyBranchScopedFilter) : nameof(ApplyScopedFilter);
    }

    /// <summary>مستند بلا مستودع: نطاق «فروع محددة» لا يقيّده.</summary>
    private void ApplyScopedFilter<T>(ModelBuilder modelBuilder) where T : BaseEntity, IScopedRecord
        => modelBuilder.Entity<T>().HasQueryFilter(e => e.TenantId == CurrentTenantId && (ScopeMode == 0 || ScopeMode == 1
            || (ScopeMode == 2 && e.CreatedByUserId != null && ScopeUserIds.Contains(e.CreatedByUserId.Value))
            || (ScopeMode == 3 && e.CreatedByUserId == ScopeUserId)
            || (ScopeMode == 4 && (e.AssignedToUserId == ScopeUserId || e.CreatedByUserId == ScopeUserId))));

    /// <summary>مستند بمستودع (فرع): المستند بلا مستودع لا يتبع فرعاً فيظهر لكل الفروع.</summary>
    private void ApplyBranchScopedFilter<T>(ModelBuilder modelBuilder) where T : BaseEntity, IScopedRecord
        => modelBuilder.Entity<T>().HasQueryFilter(e => e.TenantId == CurrentTenantId && (ScopeMode == 0
            || (ScopeMode == 1 && (EF.Property<Guid?>(e, "WarehouseId") == null || ScopeWarehouseIds.Contains(EF.Property<Guid?>(e, "WarehouseId")!.Value)))
            || (ScopeMode == 2 && e.CreatedByUserId != null && ScopeUserIds.Contains(e.CreatedByUserId.Value))
            || (ScopeMode == 3 && e.CreatedByUserId == ScopeUserId)
            || (ScopeMode == 4 && (e.AssignedToUserId == ScopeUserId || e.CreatedByUserId == ScopeUserId))));

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
        if (PreserveStamps) return;
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
                            throw new InvalidOperationException(Messages.CannotSaveOtherTenantRecord);
                        entry.Entity.TenantId = tenantId.Value;
                    }
                    else if (entry.Entity.TenantId == Guid.Empty)
                    {
                        throw new InvalidOperationException(Messages.CannotSaveRecordWithoutTenant);
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
