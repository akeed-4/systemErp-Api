namespace ERP.Core.DTOs.Shared;

public class ZatcaConnectionTestResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int LatencyMs { get; set; }
    public DateTime TestedAt { get; set; } = DateTime.UtcNow;
    public List<string> MissingFields { get; set; } = new();
}
