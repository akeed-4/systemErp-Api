namespace ERP.Core.DTOs.POS;

public class CloseShiftRequestDto
{
    /// <summary>النقد الفعلي في الدرج عند الإغلاق.</summary>
    public decimal ClosingCashActual { get; set; }
    public string? ClosingNotes { get; set; }
    /// <summary>جرد بالفئات (اختياري). عند إرساله يُحسب النقد الفعلي من مجموعه.</summary>
    public List<CashDenominationDto> Denominations { get; set; } = new();
}
