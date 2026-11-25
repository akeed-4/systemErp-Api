namespace ERP.Core.DTOs.Shared;

public class CustomerDeliveryLocationDto
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? District { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public string? Notes { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    /// <summary>استُخدم في بيان تسليم: الحذف يعطّله بدلاً من حذفه.</summary>
    public bool IsUsed { get; set; }
}

public class SaveCustomerDeliveryLocationDto
{
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? District { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }
    public string? Notes { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>نتيجة بحث جغرافي من المزود المجاني عبر وكيل الخادم.</summary>
public class GeocodeResultDto
{
    public string DisplayName { get; set; } = string.Empty;
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
}
