namespace ERP.Core.DTOs.Accounting;

/// <summary>مركز تكلفة اختاره المستخدم لحساب في قيد الفاتورة: كل أسطر الحساب في القيد تُرحَّل على هذا المركز.</summary>
public class JournalCostCenterDto
{
    public string AccountCode { get; set; } = string.Empty;
    public Guid CostCenterId { get; set; }
}
