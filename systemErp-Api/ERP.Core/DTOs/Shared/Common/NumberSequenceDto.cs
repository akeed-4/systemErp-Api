namespace ERP.Core.DTOs.Shared;

public partial class NumberSequenceDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public long LastValue { get; set; }
    public int Padding { get; set; } = 6;
    public Guid Version { get; set; }
}
