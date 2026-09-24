namespace ERP.Core.Models.CarShowroom;

/// <summary>سنة صنع لفئة (Trim) معيّنة - CarYearModel في الواجهة.</summary>
public class CarYearModel : BaseEntity
{
    public Guid TrimId { get; set; }
    public int Year { get; set; }
}
