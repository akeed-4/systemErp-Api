namespace ERP.Core.DTOs.POS;

/// <summary>تصحيح بيانات وردية. الإجماليات لا تُعدَّل يدوياً: تتغيّر بعمليات البيع والمرتجعات وحذفها فقط.</summary>
public class UpdatePosShiftRequestDto
{
    public string PosTerminalName { get; set; } = string.Empty;
    public decimal OpeningCash { get; set; }
    /// <summary>للورديات المغلقة: تصحيح النقد الفعلي عند الإغلاق (يُعاد حساب فرق الصندوق).</summary>
    public decimal? ClosingCashActual { get; set; }
    public string? ClosingNotes { get; set; }
}
