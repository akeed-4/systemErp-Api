
namespace ERP.Core.Models.Shared;

public class AuditLog : BaseEntity
{
    public string Action { get; set; } = string.Empty;                   // e.g. "INVOICE_CREATED", "ZATCA_SUBMITTED"
    public string EntityName { get; set; } = string.Empty;               // "Invoice", "Voucher", "Account"
    public string? EntityId { get; set; }
    public string PerformedBy { get; set; } = "system";
    /// <summary>معرّف المستخدم المنفّذ (الاسم في PerformedBy قد يتغيّر)؛ null للعمليات الآلية والسجلات القديمة.</summary>
    public Guid? UserId { get; set; }
    public string Details { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? ClientIpAddress { get; set; }
}
