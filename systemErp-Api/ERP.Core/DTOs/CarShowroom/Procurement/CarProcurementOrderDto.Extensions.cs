namespace ERP.Core.DTOs.CarShowroom;

public partial class CarProcurementOrderDto
{
    /// <summary>قائمة أرقام الشواسيه المستلمة (receivedVinList في الواجهة).</summary>
    public List<string> ReceivedVinList => ReceivedVins.Select(v => v.Vin).ToList();
    /// <summary>أرقام البطاقات الجمركية (customsCardList في الواجهة).</summary>
    public List<string> CustomsCardList => ReceivedVins.Where(v => !string.IsNullOrWhiteSpace(v.CustomsCardNumber)).Select(v => v.CustomsCardNumber!).ToList();
}
