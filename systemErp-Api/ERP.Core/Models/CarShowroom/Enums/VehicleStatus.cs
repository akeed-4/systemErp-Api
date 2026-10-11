namespace ERP.Core.Models.CarShowroom;

public enum VehicleStatus
{
    Available = 1,
    Reserved = 2,
    Sold = 3,
    /// <summary>مشطوبة بجرد معتمد (مفقودة فعلياً). تخرج من المخزون ولا تُباع، وتُستعاد بجرد لاحق يعثر عليها.</summary>
    WrittenOff = 4
}
