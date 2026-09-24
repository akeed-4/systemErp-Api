namespace ERP.Core.DTOs.POS;

/// <summary>
/// تصحيح بيانات العميل على عملية بيع (وفاتورتها). لتعديل الأصناف أو المبالغ: ألغِ العملية (void/حذف) ثم أنشئ بيعاً جديداً.
/// </summary>
public class UpdatePosTransactionRequestDto
{
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string? CustomerTaxNumber { get; set; }
}
