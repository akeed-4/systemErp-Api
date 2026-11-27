namespace ERP.Core.DTOs.Accounting;

public class SaveTailoringSettingsDto
{
    public Guid? DefaultWarehouseId { get; set; }
    public int DefaultLeadDays { get; set; } = 7;
    public decimal MinAdvancePercent { get; set; }
    public bool RequireQualityCheck { get; set; } = true;
}

public class TailoringSettingsDto : SaveTailoringSettingsDto
{
    public string? DefaultWarehouseName { get; set; }
}

public class TailoringMeasurementFieldDto
{
    public Guid? Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string? GroupName { get; set; }
    public int SortOrder { get; set; }
    public bool IsRequired { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    /// <summary>number أو select.</summary>
    public string InputType { get; set; } = "number";
    public List<TailoringMeasurementChoiceDto> Choices { get; set; } = new();
    public bool IsActive { get; set; } = true;
}

/// <summary>قيمة حقل قياس من نوع «اختيار» (صفة جسم).</summary>
public class TailoringMeasurementChoiceDto
{
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
}

public class TailoringDesignOptionValueDto
{
    public Guid? Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public decimal PriceDelta { get; set; }
    public int SortOrder { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}

public class TailoringDesignOptionDto
{
    public Guid? Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string InputType { get; set; } = "select";
    public bool IsRequired { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public List<TailoringDesignOptionValueDto> Values { get; set; } = new();
}

/// <summary>نوع اللباس بقالبه كاملاً: الحفظ يزامن الحقول والخيارات وقيمها بمعرّفاتها (الجديد بلا معرّف).</summary>
public class SaveTailoringGarmentTypeDto
{
    /// <summary>فارغ = يولّده الخادم.</summary>
    public string? Code { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string Category { get; set; } = "men";
    public Guid? ProductId { get; set; }
    public decimal BasePrice { get; set; }
    public decimal DefaultFabricMeters { get; set; }
    public string MeasurementUnit { get; set; } = "cm";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }
    public List<TailoringMeasurementFieldDto> Fields { get; set; } = new();
    public List<TailoringDesignOptionDto> Options { get; set; } = new();
}

public class TailoringGarmentTypeDto : SaveTailoringGarmentTypeDto
{
    public Guid Id { get; set; }
    public string? ProductName { get; set; }
    /// <summary>عليه ملفات قياس: المفاتيح لا تتغير والحقل المحذوف يُعطَّل.</summary>
    public bool InUse { get; set; }
}

public class SaveTailoringStageDto
{
    public string? Code { get; set; }
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public int SortOrder { get; set; }
    public bool IsMandatory { get; set; }
    public bool IsQualityCheck { get; set; }
    public decimal DefaultPieceRate { get; set; }
    /// <summary>فارغ = كل الأنواع.</summary>
    public List<Guid> GarmentTypeIds { get; set; } = new();
    public bool IsActive { get; set; } = true;
}

public class TailoringStageDto : SaveTailoringStageDto
{
    public Guid Id { get; set; }
}

// ---------- العملاء والقياسات ----------

public class TailoringCustomerRowDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public int ProfileCount { get; set; }
    public DateTime? LastMeasuredAt { get; set; }
    public decimal CurrentBalance { get; set; }
}

public class SaveTailoringCustomerProfileDto
{
    public string? Preferences { get; set; }
    public string? Notes { get; set; }
}

public class TailoringCustomerDto : SaveTailoringCustomerProfileDto
{
    public Guid CustomerId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string City { get; set; } = string.Empty;
    public string? District { get; set; }
    public decimal CurrentBalance { get; set; }
    public List<TailoringMeasurementProfileDto> Profiles { get; set; } = new();
}

public class SaveTailoringMeasurementProfileDto
{
    public Guid GarmentTypeId { get; set; }
    public string WearerName { get; set; } = string.Empty;
    public string? Relation { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
}

public class TailoringMeasurementProfileDto : SaveTailoringMeasurementProfileDto
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string GarmentTypeName { get; set; } = string.Empty;
    public string? GarmentTypeNameEn { get; set; }
    public int CurrentVersionNo { get; set; }
    public TailoringMeasurementVersionDto? Current { get; set; }
}

public class CreateTailoringMeasurementVersionDto
{
    /// <summary>فارغ = الآن.</summary>
    public DateTime? MeasuredAt { get; set; }
    public Guid? MeasuredByEmployeeId { get; set; }
    public Dictionary<string, decimal?> Values { get; set; } = new();
    /// <summary>صفات الجسم: مفتاح الحقل ← رمز القيمة.</summary>
    public Dictionary<string, string?> Choices { get; set; } = new();
    public string? Notes { get; set; }
}

public class TailoringMeasurementVersionDto
{
    public Guid Id { get; set; }
    public Guid ProfileId { get; set; }
    public int VersionNo { get; set; }
    public DateTime MeasuredAt { get; set; }
    public Guid? MeasuredByEmployeeId { get; set; }
    public string? MeasuredByName { get; set; }
    public string Unit { get; set; } = "cm";
    public Dictionary<string, decimal?> Values { get; set; } = new();
    public Dictionary<string, string?> Choices { get; set; } = new();
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class TailoringMeasurementCompareRowDto
{
    public string Key { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string? NameEn { get; set; }
    public decimal? From { get; set; }
    public decimal? To { get; set; }
    public decimal? Difference { get; set; }
    /// <summary>لحقل «اختيار»: اسما القيمتين بدل الأرقام.</summary>
    public string InputType { get; set; } = "number";
    public string? FromLabel { get; set; }
    public string? ToLabel { get; set; }
    public string? FromLabelEn { get; set; }
    public string? ToLabelEn { get; set; }
}

public class TailoringMeasurementCompareDto
{
    public TailoringMeasurementVersionDto From { get; set; } = new();
    public TailoringMeasurementVersionDto To { get; set; } = new();
    public List<TailoringMeasurementCompareRowDto> Rows { get; set; } = new();
}
