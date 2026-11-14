namespace ERP.Core.DTOs.Shared;

public partial class UpdateTenantDto : CreateTenantDto
{
    /// <summary>المنطقة الزمنية (IANA مثل Asia/Riyadh)؛ فارغ = تبقى الحالية.</summary>
    public string? TimeZoneId { get; set; }
}
