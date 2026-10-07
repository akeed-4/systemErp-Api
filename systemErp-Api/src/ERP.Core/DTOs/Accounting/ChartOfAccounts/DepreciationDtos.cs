namespace ERP.Core.DTOs.Accounting;

/// <summary>طلب معاينة/ترحيل إهلاك فترة شهرية (yyyy-MM). بلا أصول محددة يشمل كل الأصول، أو أصول مركز تكلفة و/أو مستودع (معرض) محدد.</summary>
public class DepreciationRunRequestDto
{
    public string Period { get; set; } = string.Empty;
    public Guid? CostCenterId { get; set; }
    public Guid? WarehouseId { get; set; }
    public List<Guid>? AssetIds { get; set; }
}

/// <summary>نتيجة المعاينة (بلا قيد) أو الترحيل (مع القيد المنشأ).</summary>
public class DepreciationRunDto
{
    public string Period { get; set; } = string.Empty;
    public bool IsPreview { get; set; }
    public Guid? JournalEntryId { get; set; }
    public string? JournalEntryNumber { get; set; }
    public int ReadyCount { get; set; }
    public decimal TotalAmount { get; set; }
    public List<DepreciationLineDto> Lines { get; set; } = new();
}

public class DepreciationLineDto
{
    public Guid FixedAssetId { get; set; }
    public string AssetCode { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public Guid? CostCenterId { get; set; }
    public Guid? WarehouseId { get; set; }
    public decimal Amount { get; set; }
    public decimal BookValueBefore { get; set; }
    public decimal BookValueAfter { get; set; }
    /// <summary>ready (سيُرحَّل) | posted (مرحَّل لهذه الفترة) | blocked (لا يُرحَّل والسبب في Reason).</summary>
    public string Status { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

public class FixedAssetDepreciationDto
{
    public Guid Id { get; set; }
    public Guid FixedAssetId { get; set; }
    public string Period { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public Guid CostCenterId { get; set; }
    public Guid? WarehouseId { get; set; }
    public Guid JournalEntryId { get; set; }
    public string JournalEntryNumber { get; set; } = string.Empty;
    public DateTime PostedAt { get; set; }
    public string? PostedBy { get; set; }
}
