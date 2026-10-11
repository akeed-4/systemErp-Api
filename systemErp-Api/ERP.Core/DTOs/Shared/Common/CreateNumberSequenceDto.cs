namespace ERP.Core.DTOs.Shared;

public partial class CreateNumberSequenceDto
{
    public string Key { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public long LastValue { get; set; }
    public int Padding { get; set; } = 6;
    public Guid Version { get; set; }
}
