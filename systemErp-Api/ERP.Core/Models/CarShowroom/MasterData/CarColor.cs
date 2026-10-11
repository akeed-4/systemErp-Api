namespace ERP.Core.Models.CarShowroom;

/// <summary>لون (خارجي أو داخلي) - يجمع قائمتي الألوان في الواجهة.</summary>
public class CarColor : BaseEntity
{
    public string NameAr { get; set; } = string.Empty;
    public string? Hex { get; set; }
    /// <summary>true = لون هيكل خارجي، false = لون مقصورة داخلي.</summary>
    public bool IsExterior { get; set; } = true;
}
