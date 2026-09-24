namespace ERP.Core.DTOs.POS;

public class OpenShiftRequestDto
{
    public decimal OpeningCash { get; set; }
    public string PosTerminalName { get; set; } = string.Empty;
}
