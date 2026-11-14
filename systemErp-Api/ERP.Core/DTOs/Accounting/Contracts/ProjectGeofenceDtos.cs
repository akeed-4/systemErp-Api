namespace ERP.Core.DTOs.Accounting.Contracts;

/// <summary>نطاق الحضور الميداني لمشروع المقاولات: موقعه ونصف قطره وأضعف دقة مقبولة وتفعيله.</summary>
public class ProjectGeofenceDto
{
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public int RadiusMeters { get; set; }
    public int MaxAccuracyMeters { get; set; }
    public bool GpsAttendanceEnabled { get; set; }
    /// <summary>reject | exception؛ فارغ = يبقى الحالي.</summary>
    public string? OutOfRangePolicy { get; set; }
}
