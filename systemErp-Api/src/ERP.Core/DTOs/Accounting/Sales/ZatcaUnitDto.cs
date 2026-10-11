namespace ERP.Core.DTOs.Accounting;

/// <summary>وحدة إصدار فواتير مربوطة مع هيئة الزكاة باسم مستخدم (بلا أسرارها).</summary>
public class ZatcaUnitDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public ZatcaEnvironment Environment { get; set; }
    public string DeviceSerial { get; set; } = string.Empty;
    public string? Csid { get; set; }
    public bool IsActive { get; set; }
    public DateTime OnboardedAt { get; set; }
    /// <summary>عدد الفواتير التي أرسلتها الوحدة (عدّاد ICV).</summary>
    public long Counter { get; set; }
    /// <summary>وحدة المستخدم صاحب الجلسة.</summary>
    public bool IsMine { get; set; }
}
