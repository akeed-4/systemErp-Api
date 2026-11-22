namespace ERP.Core.DTOs.Accounting;

// ---------- الإعداد ----------

public class WorkshopSettingsDto
{
    public decimal DefaultLaborRate { get; set; }
    public int ReservationHoldDays { get; set; }
    public int DefaultWarrantyMonths { get; set; }
    public int DefaultWarrantyKm { get; set; }
    public Guid? DefaultWarehouseId { get; set; }
    public string? DefaultWarehouseName { get; set; }
    public bool RequireReceptionInspection { get; set; }
}

public class SaveWorkshopSettingsDto
{
    public decimal DefaultLaborRate { get; set; }
    public int ReservationHoldDays { get; set; } = 7;
    public int DefaultWarrantyMonths { get; set; }
    public int DefaultWarrantyKm { get; set; }
    public Guid? DefaultWarehouseId { get; set; }
    public bool RequireReceptionInspection { get; set; }
}

public class WorkshopStageDto
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public class SaveWorkshopStageDto
{
    public string Status { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class WorkshopPauseReasonDto
{
    public Guid Id { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public class SaveWorkshopPauseReasonDto
{
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class WorkshopServiceDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public string? ProductSku { get; set; }
    public string? ProductName { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public decimal StandardHours { get; set; }
    public string PricingMethod { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    /// <summary>سعر البيع المحسوب قبل الضريبة (الثابت، أو الساعات × سعر الساعة).</summary>
    public decimal EffectivePrice { get; set; }
    public int WarrantyMonths { get; set; }
    public int WarrantyKm { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
}

public class SaveWorkshopServiceDto
{
    public string? Code { get; set; }
    public Guid ProductId { get; set; }
    public string? NameAr { get; set; }
    public string? NameEn { get; set; }
    public decimal StandardHours { get; set; }
    public string PricingMethod { get; set; } = string.Empty;
    public decimal? Price { get; set; }
    public int? WarrantyMonths { get; set; }
    public int? WarrantyKm { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public class InspectionTemplateItemDto
{
    public Guid? Id { get; set; }
    public string? Section { get; set; }
    public string TextAr { get; set; } = string.Empty;
    public string? TextEn { get; set; }
    public string ResultType { get; set; } = string.Empty;
    public bool PhotoRequiredOnFail { get; set; }
    public int SortOrder { get; set; }
}

public class InspectionTemplateDto
{
    public Guid Id { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string Usage { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public List<InspectionTemplateItemDto> Items { get; set; } = new();
}

public class SaveInspectionTemplateDto
{
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string Usage { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public List<InspectionTemplateItemDto> Items { get; set; } = new();
}

// ---------- المركبات ----------

public class ServiceVehicleDto
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? Vin { get; set; }
    public string? PlateNumber { get; set; }
    public string Make { get; set; } = string.Empty;
    public string? Model { get; set; }
    public int? Year { get; set; }
    public string? Color { get; set; }
    public string? EngineNumber { get; set; }
    public string FuelType { get; set; } = string.Empty;
    public decimal LastOdometer { get; set; }
    public Guid? ShowroomVehicleId { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
    /// <summary>أمر العمل القائم للمركبة إن وُجد.</summary>
    public Guid? OpenWorkOrderId { get; set; }
    public string? OpenWorkOrderNumber { get; set; }
    public int WorkOrderCount { get; set; }
}

public class SaveServiceVehicleDto
{
    public Guid CustomerId { get; set; }
    public string? Vin { get; set; }
    public string? PlateNumber { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public int? Year { get; set; }
    public string? Color { get; set; }
    public string? EngineNumber { get; set; }
    public string? FuelType { get; set; }
    public decimal? LastOdometer { get; set; }
    /// <summary>سيارة من المعرض: تُكمَّل منها البيانات الفارغة (الهيكل والمحرك والماركة والطراز والسنة واللون واللوحة).</summary>
    public Guid? ShowroomVehicleId { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>سطر في تاريخ المركبة (أوامر عملها بلقطة عميلها).</summary>
public class ServiceVehicleHistoryDto
{
    public Guid WorkOrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public decimal OdometerIn { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? Complaint { get; set; }
    public string? InvoiceNumber { get; set; }
    public bool IsWarrantyJob { get; set; }
    public decimal? OdometerOut { get; set; }
    /// <summary>الأسطر المنفَّذة الموافق عليها (أو باستثناء).</summary>
    public List<string> Work { get; set; } = new();
    /// <summary>الفحوص المكتملة: الاستخدام والقالب وعدد الفاشل.</summary>
    public List<string> Inspections { get; set; } = new();
    public List<WarrantyDto> Warranties { get; set; } = new();
}

// ---------- الاستقبال وأمر العمل ----------

public class SaveDamageMarkDto
{
    public string Area { get; set; } = string.Empty;
    public string DamageType { get; set; } = string.Empty;
    public string? Severity { get; set; }
    public string? Notes { get; set; }
}

public class DamageMarkDto
{
    public Guid Id { get; set; }
    public string Area { get; set; } = string.Empty;
    public string DamageType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
    public int PhotoCount { get; set; }
}

/// <summary>مركبة في الاستقبال: من السجل، أو جديدة تُسجَّل للعميل.</summary>
public class ReceptionVehicleDto
{
    public Guid? VehicleId { get; set; }
    public SaveServiceVehicleDto? NewVehicle { get; set; }
    public decimal OdometerIn { get; set; }
    public decimal? FuelLevelIn { get; set; }
    public string? Complaint { get; set; }
    public string? Accessories { get; set; }
    public string? Priority { get; set; }
    public DateTime? PromisedAt { get; set; }
    public Guid? ServiceAdvisorUserId { get; set; }
    public string? ServiceAdvisorName { get; set; }
    public string? Notes { get; set; }
    public List<SaveDamageMarkDto> Damages { get; set; } = new();
}

public class SaveWorkshopReceptionDto
{
    public Guid CustomerId { get; set; }
    public Guid? WarehouseId { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public string? Notes { get; set; }
    public List<ReceptionVehicleDto> Vehicles { get; set; } = new();
}

/// <summary>تعديل رأس الاستقبال (جهة التواصل والملاحظات).</summary>
public class UpdateWorkshopReceptionDto
{
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public string? Notes { get; set; }
}

public class WorkshopReceptionDto
{
    public Guid Id { get; set; }
    public string ReceptionNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<WorkOrderDto> Orders { get; set; } = new();
}

public class UpdateWorkOrderDto
{
    public decimal OdometerIn { get; set; }
    public decimal? FuelLevelIn { get; set; }
    public string? Complaint { get; set; }
    public string? Accessories { get; set; }
    public string? Priority { get; set; }
    public DateTime? PromisedAt { get; set; }
    public Guid? ServiceAdvisorUserId { get; set; }
    public string? ServiceAdvisorName { get; set; }
    public string? Notes { get; set; }
}

/// <summary>نقل أمر العمل لحالة أو مرحلة: التوقف يتطلب سببه، والرجوع لحالة سابقة يتطلب سبباً نصياً.</summary>
public class ChangeWorkOrderStatusDto
{
    public string Status { get; set; } = string.Empty;
    public Guid? StageId { get; set; }
    public Guid? PauseReasonId { get; set; }
    public string? Reason { get; set; }
}

public class WorkOrderStatusHistoryDto
{
    public Guid Id { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public Guid? StageId { get; set; }
    public string? StageName { get; set; }
    public string? PauseReasonName { get; set; }
    public string? Reason { get; set; }
    public string? ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; }
}

public class WorkOrderDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid ReceptionId { get; set; }
    public string? ReceptionNumber { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public Guid VehicleId { get; set; }
    public string? VehicleLabel { get; set; }
    public string? Vin { get; set; }
    public string? PlateNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public Guid? StageId { get; set; }
    public string? StageName { get; set; }
    public Guid? PauseReasonId { get; set; }
    public string? PauseReasonName { get; set; }
    public string Priority { get; set; } = string.Empty;
    public decimal OdometerIn { get; set; }
    public decimal? OdometerOut { get; set; }
    public decimal? FuelLevelIn { get; set; }
    public string? Complaint { get; set; }
    public string? Accessories { get; set; }
    public Guid? ServiceAdvisorUserId { get; set; }
    public string? ServiceAdvisorName { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? PromisedAt { get; set; }
    public bool IsOverdue { get; set; }
    public DateTime? ReadyAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public string? Notes { get; set; }
    public string? CancelReason { get; set; }
    public bool IsWarrantyJob { get; set; }
    public Guid? WarrantyId { get; set; }
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoicedAt { get; set; }
    public string? DeliveredTo { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public int AttachmentCount { get; set; }
    /// <summary>الحالات المتاحة يدوياً من الحالة الحالية.</summary>
    public List<string> NextStatuses { get; set; } = new();
    public List<DamageMarkDto> Damages { get; set; } = new();
    public List<WorkOrderStatusHistoryDto> History { get; set; } = new();
}

public class WorkshopReasonDto
{
    public string? Reason { get; set; }
}
