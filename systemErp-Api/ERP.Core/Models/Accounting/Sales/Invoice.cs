namespace ERP.Core.Models.Accounting;

/// <summary>فاتورة معارض السيارات (بيع/شراء ومرتجعاتهما): جدول Invoice كما هو، ببنوده وأسطر سياراته ودفعاتها.</summary>
public class Invoice : InvoiceBase<InvoiceItem>
{
    /// <summary>أسطر السيارات (فواتير الشراء/البيع متعددة السيارات). تُشتق منها بنود InvoiceItem.</summary>
    public virtual ICollection<InvoiceVehicleLine> VehicleLines { get; set; } = new List<InvoiceVehicleLine>();
    /// <summary>دفعات السيارات (فاتورة شراء): سطر لكل دفعة، وشواصيها في VehicleLines.</summary>
    public virtual ICollection<InvoiceVehicleBatch> VehicleBatches { get; set; } = new List<InvoiceVehicleBatch>();
}
