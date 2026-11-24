namespace ERP.Core.DTOs.Shared;

public class ZatcaConnectionTestResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int LatencyMs { get; set; }
    /// <summary>وصل رد HTTP من بوابة الهيئة (DNS وTCP وTLS سليمة).</summary>
    public bool Reached { get; set; }
    /// <summary>رمز HTTP الذي ردّت به بوابة الهيئة على طلب الفحص (400 متوقع)، أو null عند عدم الوصول.</summary>
    public int? HttpStatus { get; set; }
    public DateTime TestedAt { get; set; } = DateTime.UtcNow;
    public List<string> MissingFields { get; set; } = new();
}
