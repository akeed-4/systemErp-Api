namespace ERP.Core.DTOs.Accounting;

// ---------- الإعداد: الفئات والأسعار والمعدات القابلة للتأجير ----------

public class RentalCategoryDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public Guid? ParentId { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public int EquipmentCount { get; set; }
}

public class SaveRentalCategoryDto
{
    /// <summary>فارغ = يُولَّد.</summary>
    public string? Code { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public Guid? ParentId { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public class SaveRentalRateDto
{
    /// <summary>فئة أو معدة (واحد منهما).</summary>
    public Guid? CategoryId { get; set; }
    public Guid? EquipmentId { get; set; }
    public string Basis { get; set; } = string.Empty;
    public decimal Rate { get; set; }
    public decimal? OperatorRate { get; set; }
    public decimal IncludedHours { get; set; }
    public decimal OvertimeRate { get; set; }
    public decimal MinimumPeriods { get; set; } = 1;
    /// <summary>فارغ = اليوم.</summary>
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public string? Notes { get; set; }
}

public class RentalRateDto : SaveRentalRateDto
{
    public Guid Id { get; set; }
    public string? CategoryName { get; set; }
    public string? EquipmentCode { get; set; }
    public string? EquipmentName { get; set; }
}

/// <summary>معدة في سجل التأجير: هويتها وحالتها وإعداد تأجيرها (بلا تكاليف المقاولات).</summary>
public class RentalEquipmentDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string Ownership { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? SupplierName { get; set; }
    public decimal MeterHours { get; set; }
    public bool IsRentable { get; set; }
    public Guid? RentalCategoryId { get; set; }
    public string? RentalCategoryName { get; set; }
    public Guid? HomeWarehouseId { get; set; }
    public string? HomeWarehouseName { get; set; }
}

public class UpdateRentalEquipmentDto
{
    public bool IsRentable { get; set; }
    public Guid? RentalCategoryId { get; set; }
    public Guid? HomeWarehouseId { get; set; }
}

// ---------- التوفر والتسعير ----------

/// <summary>
/// فترة تشغل المعدة: <c>assignment</c> إسناد لمشروع مقاولات (مفتوح حتى الإرجاع)، <c>booking</c> حجز مؤقت أو مؤكد، <c>maintenance</c>
/// أمر صيانة، <c>status</c> خارج الخدمة أو مُعادة للمؤجر، <c>supplier_contract</c> خارج فترة عقد إيجارها من المورد.
/// </summary>
public class RentalBlockDto
{
    public Guid EquipmentId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public Guid? ReferenceId { get; set; }
    public DateTime From { get; set; }
    /// <summary>فارغ = مفتوحة (حتى الإرجاع أو الإكمال).</summary>
    public DateTime? To { get; set; }
    /// <summary>للحجز: مؤقت أو مؤكد.</summary>
    public string? Status { get; set; }
}

public class RentalCalendarRowDto
{
    public RentalEquipmentDto Equipment { get; set; } = new();
    public List<RentalBlockDto> Blocks { get; set; } = new();
}

public class RentalQuoteRequestDto
{
    public Guid? EquipmentId { get; set; }
    public Guid? CategoryId { get; set; }
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public string Basis { get; set; } = string.Empty;
    public bool WithOperator { get; set; }
    /// <summary>لفحص التعارض عند تعديل حجز قائم.</summary>
    public Guid? ExcludeBookingId { get; set; }
}

public class RentalChargePartDto
{
    public string Basis { get; set; } = string.Empty;
    public decimal Units { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>تقدير الإيجار للفترة (قبل الضريبة): الفترات الكاملة بالأساس المختار والكسر بأصغر أساس مسعّر، وتعارضات المعدة.</summary>
public class RentalQuoteDto
{
    public decimal Rate { get; set; }
    public decimal OperatorRate { get; set; }
    public List<RentalChargePartDto> Parts { get; set; } = new();
    public decimal RentAmount { get; set; }
    public decimal OperatorAmount { get; set; }
    public decimal Total { get; set; }
    /// <summary>طُبّق الحد الأدنى للفترات.</summary>
    public bool MinimumApplied { get; set; }
    public List<RentalBlockDto> Conflicts { get; set; } = new();
}

// ---------- الحجوزات ----------

public class SaveRentalBookingLineDto
{
    public Guid? EquipmentId { get; set; }
    public Guid? CategoryId { get; set; }
    public int Quantity { get; set; } = 1;
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public string Basis { get; set; } = string.Empty;
    public bool WithOperator { get; set; }
    public string? Notes { get; set; }
}

public class SaveRentalBookingDto
{
    public Guid CustomerId { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? Notes { get; set; }
    public List<SaveRentalBookingLineDto> Lines { get; set; } = new();
}

public class RentalBookingLineDto : SaveRentalBookingLineDto
{
    public Guid Id { get; set; }
    public string? EquipmentCode { get; set; }
    public string? EquipmentName { get; set; }
    public string? CategoryName { get; set; }
    public decimal Rate { get; set; }
    public decimal OperatorRate { get; set; }
    public decimal EstimatedAmount { get; set; }
}

public class RentalBookingDto
{
    public Guid Id { get; set; }
    public string BookingNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public Guid? WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public string Status { get; set; } = string.Empty;
    /// <summary>الحالة الفعلية: المؤقت بعد موعده «منتهٍ».</summary>
    public string EffectiveStatus { get; set; } = string.Empty;
    public DateTime? HoldUntil { get; set; }
    public string? Notes { get; set; }
    public string? CancelReason { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public decimal EstimatedTotal { get; set; }
    public List<RentalBookingLineDto> Lines { get; set; } = new();
}

public class HoldRentalBookingDto
{
    public DateTime HoldUntil { get; set; }
}

public class RentalReasonDto
{
    public string? Reason { get; set; }
}
