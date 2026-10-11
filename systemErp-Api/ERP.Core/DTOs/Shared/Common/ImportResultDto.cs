namespace ERP.Core.DTOs.Shared;

/// <summary>نتيجة صف واحد ضمن استيراد جماعي (Excel import وغيره).</summary>
public class ImportRowResult
{
    public int RowNumber { get; set; }
    public bool Success { get; set; }
    public Guid? Id { get; set; }
    public string? Error { get; set; }
}

/// <summary>ملخص استيراد جماعي: يُستخدم من كل خدمة تدعم ImportAsync (Products، Vehicles...).</summary>
public class ImportResultDto
{
    public int TotalRows { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public List<ImportRowResult> Results { get; set; } = new();
}
