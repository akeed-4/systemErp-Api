namespace ERP.Core.DTOs.CarShowroom;

/// <summary>تحقق جماعي من أرقام شاسيه قبل إضافتها لدفعة (استيراد Excel، لصق قائمة).</summary>
public class ValidateChassisRequestDto
{
    public List<string> Vins { get; set; } = new();
    /// <summary>مسودة الفاتورة الجاري تعديلها: شواصيها المحفوظة لا تُعدّ تكراراً.</summary>
    public Guid? InvoiceId { get; set; }
}

/// <summary>مشكلة في صف: index موضعه في القائمة المرسلة، code = required | format | duplicate | exists.</summary>
public class ChassisIssueDto
{
    public int Index { get; set; }
    public string Vin { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

/// <summary>استبدال رقم شاسيه مؤقت بالرقم الحقيقي.</summary>
public class ReplaceChassisDto
{
    public Guid VehicleId { get; set; }
    public string ChassisNumber { get; set; } = string.Empty;
}
