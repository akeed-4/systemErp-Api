namespace ERP.Core.Models.CarShowroom;

/// <summary>حصة سيارة واحدة من سطر مصروف (بالعملة الأساسية). مجموع الحصص يساوي CostAmount للسطر تماماً.</summary>
public class AdditionalChargeAllocation : BaseEntity
{
    public Guid LineId { get; set; }
    public Guid VehicleId { get; set; }
    public decimal Amount { get; set; }
    /// <summary>قُيِّدت الحصة على حساب «سيارات في الطريق» لأن السيارة لم تكن مستلمة وقت الترحيل.</summary>
    public bool PostedInTransit { get; set; }
    /// <summary>نُقلت الحصة بعد ذلك إلى مخزون السيارات عند استلام السيارة.</summary>
    public bool ReclassedToInventory { get; set; }
    /// <summary>السيارة كانت مباعة وقت الترحيل: الحصة حُمّلت على تكلفة المبيعات مباشرة لا على المخزون.</summary>
    public bool PostedToCogs { get; set; }

    public virtual AdditionalChargeLine Line { get; set; } = null!;
}
