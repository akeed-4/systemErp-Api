namespace ERP.Core.DTOs.POS;

/// <summary>تصحيح سبب المرتجع فقط؛ الكميات والمبالغ تُصحَّح بحذف المرتجع وإنشاء آخر.</summary>
public class UpdatePosReturnRequestDto
{
    public string ReturnReason { get; set; } = string.Empty;
}
