namespace ERP.Core.DTOs.Accounting.Contracts;

/// <summary>معدة في السجل؛ حقول القراءة (الأصل، المؤجر، الإسناد الحالي، استحقاق الصيانة) من الخادم.</summary>
public class EquipmentDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string? Category { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public int? ManufactureYear { get; set; }
    public string? SerialNumber { get; set; }
    public string? PlateNumber { get; set; }
    public string? Capacity { get; set; }
    public string? Specifications { get; set; }
    public string Ownership { get; set; } = EquipmentOwnerships.Owned;
    /// <summary><see cref="EquipmentCostMethods"/>: الإهلاك أو السعر الداخلي (مملوكة)، أو الإيجار الفعلي (مستأجرة).</summary>
    public string CostMethod { get; set; } = EquipmentCostMethods.InternalRate;
    public Guid? FixedAssetId { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? LibraryResourceId { get; set; }
    public Guid? CostCenterId { get; set; }
    public string Status { get; set; } = EquipmentStatuses.Available;
    public string RateBasis { get; set; } = ProjectRateBases.Hour;
    public decimal Rate { get; set; }
    public decimal FuelPerHour { get; set; }
    public decimal MaintenancePerHour { get; set; }
    public decimal OperatorPerHour { get; set; }
    public decimal HoursPerDay { get; set; } = 8;
    public decimal DaysPerMonth { get; set; } = 26;
    public decimal MeterHours { get; set; }
    public decimal? ServiceIntervalHours { get; set; }
    public int? ServiceIntervalDays { get; set; }
    public decimal? LastServiceMeter { get; set; }
    public DateTime? LastServiceDate { get; set; }
    public string? Notes { get; set; }

    // ---- للقراءة ----
    public decimal HourlyCost { get; set; }
    public string? SupplierName { get; set; }
    public string? CostCenterName { get; set; }
    public string? FixedAssetCode { get; set; }
    public decimal? AssetPurchaseCost { get; set; }
    public decimal? AssetBookValue { get; set; }
    public decimal? AssetAccumulatedDepreciation { get; set; }
    public Guid? CurrentContractId { get; set; }
    public string? CurrentProject { get; set; }
    public string? CurrentSite { get; set; }
    public string? CurrentOperator { get; set; }
    /// <summary>الصيانة الوقائية مستحقة (بالعدّاد أو بالتاريخ).</summary>
    public bool ServiceDue { get; set; }
    public decimal? HoursToService { get; set; }
    public DateTime? NextServiceDate { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SaveEquipmentDto
{
    public string? Code { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string? Category { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public int? ManufactureYear { get; set; }
    public string? SerialNumber { get; set; }
    public string? PlateNumber { get; set; }
    public string? Capacity { get; set; }
    public string? Specifications { get; set; }
    public string Ownership { get; set; } = EquipmentOwnerships.Owned;
    /// <summary>فارغ = الافتراضي للملكية (المملوكة بسعر داخلي، والمستأجرة بإيجارها الفعلي).</summary>
    public string? CostMethod { get; set; }
    public Guid? FixedAssetId { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? LibraryResourceId { get; set; }
    public Guid? CostCenterId { get; set; }
    /// <summary>الحالة اليدوية فقط (متاحة، خارج الخدمة، مُعادة للمؤجر)؛ فارغ = لا تغيير.</summary>
    public string? Status { get; set; }
    public string? RateBasis { get; set; }
    public decimal Rate { get; set; }
    public decimal FuelPerHour { get; set; }
    public decimal MaintenancePerHour { get; set; }
    public decimal OperatorPerHour { get; set; }
    public decimal? HoursPerDay { get; set; }
    public decimal? DaysPerMonth { get; set; }
    public decimal? MeterHours { get; set; }
    public decimal? ServiceIntervalHours { get; set; }
    public int? ServiceIntervalDays { get; set; }
    public string? Notes { get; set; }
}

public class EquipmentAssignmentDto
{
    public Guid Id { get; set; }
    public Guid EquipmentId { get; set; }
    public string EquipmentCode { get; set; } = string.Empty;
    public string EquipmentName { get; set; } = string.Empty;
    public string Ownership { get; set; } = string.Empty;
    public Guid ContractId { get; set; }
    public string? ContractNumber { get; set; }
    public string? ProjectName { get; set; }
    public Guid? ProjectResourceId { get; set; }
    public string? SiteName { get; set; }
    public Guid? OperatorEmployeeId { get; set; }
    public string? OperatorName { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal? MeterStart { get; set; }
    public decimal? MeterEnd { get; set; }
    public string? Notes { get; set; }
    public string? ReturnNotes { get; set; }
    /// <summary>من التقارير اليومية المعتمدة على مورد المشروع.</summary>
    public decimal ReportedHours { get; set; }
    public decimal IdleHours { get; set; }
    public decimal ChargedCost { get; set; }
}

public class AssignEquipmentDto
{
    public Guid EquipmentId { get; set; }
    public Guid ContractId { get; set; }
    /// <summary>بند الاحتياج في خطة التنفيذ (مورد المكتبة) الذي يغطيه الإسناد؛ فارغ = مورد مكتبة المعدة نفسها.</summary>
    public Guid? LibraryResourceId { get; set; }
    public string? SiteName { get; set; }
    public Guid? OperatorEmployeeId { get; set; }
    public DateTime? FromDate { get; set; }
    public string? Notes { get; set; }
}

/// <summary>تعديل الإسناد النشط: الموقع والمشغّل والملاحظات.</summary>
public class UpdateEquipmentAssignmentDto
{
    public string? SiteName { get; set; }
    public Guid? OperatorEmployeeId { get; set; }
    public string? Notes { get; set; }
}

public class ReturnEquipmentDto
{
    public DateTime? Date { get; set; }
    public decimal? MeterReading { get; set; }
    public string? Notes { get; set; }
}

public class TransferEquipmentDto
{
    public Guid ContractId { get; set; }
    public string? SiteName { get; set; }
    public Guid? OperatorEmployeeId { get; set; }
    public DateTime? Date { get; set; }
    public decimal? MeterReading { get; set; }
    public string? Notes { get; set; }
}

public class EquipmentLogDto
{
    public Guid Id { get; set; }
    public Guid EquipmentId { get; set; }
    public string EquipmentCode { get; set; } = string.Empty;
    public string EquipmentName { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public Guid? ContractId { get; set; }
    public string? ProjectName { get; set; }
    public decimal Hours { get; set; }
    public decimal Quantity { get; set; }
    public decimal Amount { get; set; }
    public decimal? MeterReading { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }
    public bool ChargeToProject { get; set; }
    public string? ExpenseAccountCode { get; set; }
    public string? CreditAccountCode { get; set; }
    public Guid? JournalEntryId { get; set; }
    public bool Posted { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SaveEquipmentLogDto
{
    public Guid EquipmentId { get; set; }
    public string Kind { get; set; } = EquipmentLogKinds.Operation;
    public DateTime? Date { get; set; }
    public decimal Hours { get; set; }
    public decimal Quantity { get; set; }
    public decimal Amount { get; set; }
    public decimal? MeterReading { get; set; }
    public string? Reason { get; set; }
    public string? Notes { get; set; }
    /// <summary>فارغ = الافتراضي: المستأجرة على المشروع، والمملوكة على مركز المعدة (تكلفتها محمّلة بالساعة).</summary>
    public bool? ChargeToProject { get; set; }
    /// <summary>فارغ = حساب تكلفة معدات المشاريع من الإعدادات.</summary>
    public string? ExpenseAccountCode { get; set; }
    /// <summary>حساب السداد (صندوق/بنك/مورد). فارغ = بلا قيد (المصروف مقيَّد من مستنده).</summary>
    public string? CreditAccountCode { get; set; }
}

public class EquipmentMaintenancePartDto
{
    public Guid? Id { get; set; }
    public Guid ProductId { get; set; }
    public string? ItemName { get; set; }
    public string? Sku { get; set; }
    public string? Unit { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
    /// <summary>للقراءة: رصيد موقع الأمر (قبل الإكمال).</summary>
    public decimal? Stock { get; set; }
}

public class EquipmentMaintenanceOrderDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid EquipmentId { get; set; }
    public string EquipmentCode { get; set; } = string.Empty;
    public string EquipmentName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime ScheduledDate { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public decimal? MeterReading { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? WorkDone { get; set; }
    public Guid? TechnicianEmployeeId { get; set; }
    public string? TechnicianName { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public decimal LaborCost { get; set; }
    public decimal ExternalCost { get; set; }
    public decimal PartsCost { get; set; }
    public decimal TotalCost { get; set; }
    public decimal DowntimeHours { get; set; }
    public Guid? ContractId { get; set; }
    public string? ProjectName { get; set; }
    public bool ChargeToProject { get; set; }
    public string? ExpenseAccountCode { get; set; }
    public string? CreditAccountCode { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string? CompletedBy { get; set; }
    public string? CancelReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<EquipmentMaintenancePartDto> Parts { get; set; } = new();
}

public class SaveEquipmentMaintenanceOrderDto
{
    public Guid EquipmentId { get; set; }
    public string Type { get; set; } = EquipmentMaintenanceTypes.Corrective;
    public DateTime? ScheduledDate { get; set; }
    public decimal? MeterReading { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? WorkDone { get; set; }
    public Guid? TechnicianEmployeeId { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? WarehouseId { get; set; }
    public decimal LaborCost { get; set; }
    public decimal ExternalCost { get; set; }
    public decimal DowntimeHours { get; set; }
    public bool ChargeToProject { get; set; }
    public string? ExpenseAccountCode { get; set; }
    /// <summary>الحساب الدائن لتكلفة العمالة والورشة الخارجية. فارغ = لا تُقيَّد (من فاتورتها).</summary>
    public string? CreditAccountCode { get; set; }
    public List<EquipmentMaintenancePartDto> Parts { get; set; } = new();
}

public class CompleteEquipmentMaintenanceDto
{
    public DateTime? CompletedAt { get; set; }
    public decimal? MeterReading { get; set; }
    public string? WorkDone { get; set; }
    public decimal? DowntimeHours { get; set; }
}

public class EquipmentReasonDto
{
    public string? Reason { get; set; }
}

public class EquipmentRentalContractDto
{
    public Guid Id { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public Guid EquipmentId { get; set; }
    public string EquipmentCode { get; set; } = string.Empty;
    public string EquipmentName { get; set; } = string.Empty;
    public Guid SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string RateBasis { get; set; } = ProjectRateBases.Month;
    public decimal Rate { get; set; }
    public decimal IdleBillingPercent { get; set; }
    public decimal StandbyBillingPercent { get; set; }
    public decimal HoursPerDay { get; set; }
    public decimal DaysPerMonth { get; set; }
    public decimal DepositAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Terms { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>للقراءة: تكلفة الساعة، وأيام المتبقي، والمستحق من التقارير اليومية وما سُوِّي منه بفواتير المؤجر.</summary>
    public decimal HourlyCost { get; set; }
    public int? DaysRemaining { get; set; }
    public decimal AccruedAmount { get; set; }
    public decimal SettledAmount { get; set; }
    public decimal OpenAmount { get; set; }
}

public class SaveEquipmentRentalContractDto
{
    public Guid EquipmentId { get; set; }
    public Guid SupplierId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string RateBasis { get; set; } = ProjectRateBases.Month;
    public decimal Rate { get; set; }
    public decimal? IdleBillingPercent { get; set; }
    public decimal? StandbyBillingPercent { get; set; }
    public decimal? HoursPerDay { get; set; }
    public decimal? DaysPerMonth { get; set; }
    public decimal DepositAmount { get; set; }
    public string? Terms { get; set; }
}

// ---------------- اللوحة والتقارير ----------------

public class EquipmentCountDto
{
    public string Key { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class EquipmentAlertDto
{
    /// <summary>service_due | rental_expiring | rental_expired | open_order.</summary>
    public string Kind { get; set; } = string.Empty;
    public Guid EquipmentId { get; set; }
    public string EquipmentCode { get; set; } = string.Empty;
    public string EquipmentName { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public DateTime? Date { get; set; }
    public decimal? Value { get; set; }
}

public class EquipmentDashboardDto
{
    public int Total { get; set; }
    public int Owned { get; set; }
    public int Rented { get; set; }
    public List<EquipmentCountDto> ByStatus { get; set; } = new();
    public List<EquipmentCountDto> ByCategory { get; set; } = new();
    public int ActiveAssignments { get; set; }
    public int OpenOrders { get; set; }
    public int ServiceDue { get; set; }
    public int ActiveRentals { get; set; }
    /// <summary>نسبة المسند من الصالح للعمل (المتاح + المسند).</summary>
    public decimal UtilizationPercent { get; set; }
    /// <summary>الشهر الحالي.</summary>
    public decimal MonthHours { get; set; }
    public decimal MonthFuelCost { get; set; }
    public decimal MonthExpenses { get; set; }
    public decimal MonthMaintenanceCost { get; set; }
    public decimal MonthChargedToProjects { get; set; }
    public decimal MonthDowntimeHours { get; set; }
    public List<EquipmentAlertDto> Alerts { get; set; } = new();
}

/// <summary>تكلفة معدة في فترة: ما حُمِّل على المشاريع (بالساعة) مقابل تكلفتها الفعلية (وقود، مصاريف، صيانة، إهلاك، إيجار).</summary>
public class EquipmentCostRowDto
{
    public Guid EquipmentId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string Ownership { get; set; } = string.Empty;
    public string CostMethod { get; set; } = string.Empty;
    public decimal WorkHours { get; set; }
    public decimal IdleHours { get; set; }
    public decimal DowntimeHours { get; set; }
    public decimal FuelQuantity { get; set; }
    public decimal FuelCost { get; set; }
    public decimal Expenses { get; set; }
    public decimal MaintenanceCost { get; set; }
    public decimal Depreciation { get; set; }
    public decimal RentalCost { get; set; }
    public decimal ActualCost { get; set; }
    /// <summary>المحمّل على المشاريع: بالساعة من التقارير المعتمدة + حصص الإهلاك الموزّعة (<see cref="DepreciationAllocated"/>).</summary>
    public decimal ChargedToProjects { get; set; }
    /// <summary>طريقة الإهلاك: ما وُزِّع من الإهلاك الفعلي على المشاريع (الباقي على مركز الأصل).</summary>
    public decimal DepreciationAllocated { get; set; }
    /// <summary>المحمّل − الفعلي (موجب = استرداد زائد).</summary>
    public decimal Recovery { get; set; }
    public decimal CostPerHour { get; set; }
}

public class EquipmentProjectCostRowDto
{
    public Guid ContractId { get; set; }
    public string? ContractNumber { get; set; }
    public string? ProjectName { get; set; }
    public Guid EquipmentId { get; set; }
    public string EquipmentCode { get; set; } = string.Empty;
    public string EquipmentName { get; set; } = string.Empty;
    public string Ownership { get; set; } = string.Empty;
    public decimal WorkHours { get; set; }
    public decimal IdleHours { get; set; }
    /// <summary>المحمّل بالساعة من التقارير المعتمدة (المملوكة قيد التحميل، والمستأجرة استحقاق الإيجار).</summary>
    public decimal ChargedCost { get; set; }
    public string CostMethod { get; set; } = string.Empty;
    /// <summary>حصة المشروع من الإهلاك الفعلي (طريقة الإهلاك)؛ ضمن <see cref="TotalCost"/>.</summary>
    public decimal DepreciationAllocated { get; set; }
    /// <summary>وقود ومصاريف وصيانة حُمِّلت مباشرة على مركز المشروع.</summary>
    public decimal DirectCost { get; set; }
    public decimal TotalCost { get; set; }
}

public class EquipmentCostReportDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public List<EquipmentCostRowDto> Equipment { get; set; } = new();
    public List<EquipmentProjectCostRowDto> Projects { get; set; } = new();
}

/// <summary>خيارات النماذج: المشاريع المتاحة للإسناد، والموظفون (إن وُجدت الموارد البشرية)، والأصول غير المربوطة.</summary>
public class EquipmentOptionsDto
{
    public List<EquipmentOptionDto> Projects { get; set; } = new();
    public List<EquipmentOptionDto> Employees { get; set; } = new();
    public List<EquipmentOptionDto> FixedAssets { get; set; } = new();
    public List<string> Categories { get; set; } = new();
    public string? DefaultExpenseAccountCode { get; set; }
}

public class EquipmentOptionDto
{
    public Guid Id { get; set; }
    public string? Code { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Detail { get; set; }
}

// ---------- إيرادات المعدات وربحيتها ----------

/// <summary>تسعيرة إيراد معدة: افتراضية (بلا عقد) أو خاصة بعقد، وبندها المربوط في جدول الكميات (للإيراد المحاسبي من المستخلصات).</summary>
public class EquipmentRevenueRateDto
{
    public Guid Id { get; set; }
    public Guid EquipmentId { get; set; }
    public string EquipmentCode { get; set; } = string.Empty;
    public string EquipmentName { get; set; } = string.Empty;
    public string? Category { get; set; }
    public Guid? ContractId { get; set; }
    public string? ContractNumber { get; set; }
    public string? ProjectName { get; set; }
    public Guid? ContractItemId { get; set; }
    public string? ContractItemLabel { get; set; }
    public string? ContractItemUnit { get; set; }
    public decimal? ContractItemUnitPrice { get; set; }
    public string Basis { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public decimal ShiftHours { get; set; }
    public decimal MinimumDailyHours { get; set; }
    public decimal IdleBillingPercent { get; set; }
    public decimal StandbyBillingPercent { get; set; }
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SaveEquipmentRevenueRateDto
{
    public Guid EquipmentId { get; set; }
    public Guid? ContractId { get; set; }
    public Guid? ContractItemId { get; set; }
    public string Basis { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public decimal? ShiftHours { get; set; }
    public decimal? MinimumDailyHours { get; set; }
    public decimal? IdleBillingPercent { get; set; }
    public decimal? StandbyBillingPercent { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public string? Notes { get; set; }
}

/// <summary>بند جدول كميات قابل للربط بتسعيرة معدة (ومعدته المرتبطة حالياً إن وُجدت).</summary>
public class EquipmentRevenueItemOptionDto
{
    public Guid Id { get; set; }
    public string? LineCode { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public Guid? LinkedEquipmentId { get; set; }
    public string? LinkedEquipmentCode { get; set; }
}

/// <summary>
/// ربحية معدة أو معدة في مشروع لفترة. الإيراد التشغيلي محسوب من الاستخدام الفعلي × التسعيرة (بلا قيد)، والمحاسبي من المستخلصات
/// المعتمدة والمفوترة لبنودها المربوطة. التكلفة الفعلية: وقود ومصاريف وصيانة وإيجار وإهلاك (مما هو مسجّل) + المشغّل بسعره المعياري.
/// </summary>
public class EquipmentProfitRowDto
{
    public Guid EquipmentId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string Ownership { get; set; } = string.Empty;
    /// <summary>فارغ في صف المعدة الإجمالي.</summary>
    public Guid? ContractId { get; set; }
    public string? ContractNumber { get; set; }
    public string? ProjectName { get; set; }
    /// <summary>أسس التسعيرات المطبّقة في الفترة (hour/day/...).</summary>
    public List<string> Bases { get; set; } = new();
    public decimal WorkHours { get; set; }
    public decimal IdleHours { get; set; }
    public decimal WorkDays { get; set; }
    public decimal WorkUnits { get; set; }
    /// <summary>ساعات عمل بلا تسعيرة سارية (إيرادها غير محسوب).</summary>
    public decimal UnpricedHours { get; set; }
    public decimal OperationalRevenue { get; set; }
    public decimal CertifiedQuantity { get; set; }
    public decimal AccountingRevenue { get; set; }
    public decimal FuelCost { get; set; }
    public decimal MaintenanceCost { get; set; }
    public decimal Expenses { get; set; }
    public decimal OperatorCost { get; set; }
    public decimal Depreciation { get; set; }
    public decimal RentalCost { get; set; }
    public decimal TotalCost { get; set; }
    /// <summary>صف المعدة: تكلفة لم تُنسب لمشروع (فترات بلا إسناد، إهلاك السعر الداخلي، ورشة).</summary>
    public decimal UnallocatedCost { get; set; }
    public decimal OperationalProfit { get; set; }
    public decimal AccountingProfit { get; set; }
    public decimal? OperationalMargin { get; set; }
    public decimal? AccountingMargin { get; set; }
    /// <summary>التشغيلي − المحاسبي (موجب = عمل لم يُعتمد في المستخلصات بعد).</summary>
    public decimal UncertifiedRevenue { get; set; }
}

/// <summary>إجمالي مشروع من صفوف معداته.</summary>
public class EquipmentProjectProfitDto
{
    public Guid ContractId { get; set; }
    public string? ContractNumber { get; set; }
    public string? ProjectName { get; set; }
    public int EquipmentCount { get; set; }
    public decimal WorkHours { get; set; }
    public decimal OperationalRevenue { get; set; }
    public decimal AccountingRevenue { get; set; }
    public decimal TotalCost { get; set; }
    public decimal OperationalProfit { get; set; }
    public decimal AccountingProfit { get; set; }
    public decimal? OperationalMargin { get; set; }
    public decimal? AccountingMargin { get; set; }
}

public class EquipmentProfitTotalsDto
{
    public decimal OperationalRevenue { get; set; }
    public decimal AccountingRevenue { get; set; }
    public decimal TotalCost { get; set; }
    public decimal OperationalProfit { get; set; }
    public decimal AccountingProfit { get; set; }
    public decimal UncertifiedRevenue { get; set; }
    public decimal UnpricedHours { get; set; }
    public int LossMaking { get; set; }
}

public class EquipmentProfitabilityReportDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public Guid? ContractId { get; set; }
    public List<EquipmentProfitRowDto> Equipment { get; set; } = new();
    public List<EquipmentProfitRowDto> EquipmentProjects { get; set; } = new();
    public List<EquipmentProjectProfitDto> Projects { get; set; } = new();
    public EquipmentProfitTotalsDto Totals { get; set; } = new();
}
