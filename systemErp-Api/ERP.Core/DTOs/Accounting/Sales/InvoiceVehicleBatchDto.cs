using ERP.Core.Models.CarShowroom;

namespace ERP.Core.DTOs.Accounting;

/// <summary>دفعة سيارات في فاتورة شراء. الكمية والإجماليات تُحسب في السرفر من الشواصي وتُتجاهل من العميل.</summary>
public class InvoiceVehicleBatchDto
{
    public Guid Id { get; set; }
    public int LineNo { get; set; }
    public Guid? BrandId { get; set; }
    public Guid? ModelId { get; set; }
    public Guid? TrimId { get; set; }
    public string BrandNameAr { get; set; } = string.Empty;
    public string ModelNameAr { get; set; } = string.Empty;
    public string? TrimNameAr { get; set; }
    public int Year { get; set; }
    public string ColorExterior { get; set; } = string.Empty;
    public string ColorInterior { get; set; } = string.Empty;
    public VatMode VatMode { get; set; } = VatMode.Standard_15;
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public int Quantity { get; set; }
    public decimal TotalBeforeVat { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAfterVat { get; set; }
    /// <summary>إدخال فقط: عدد أرقام الشاسيه المؤقتة التي يولّدها السرفر ويضيفها للدفعة عند الحفظ.</summary>
    public int GenerateCount { get; set; }
    /// <summary>قراءة فقط (فاتورة مرحّلة): ما أُرجع من شواصي الدفعة بمرتجعات مرحّلة وصافي قيمته.</summary>
    public int ReturnedQuantity { get; set; }
    public decimal ReturnedTotal { get; set; }
    /// <summary>شواصي الدفعة: الشاسيه وما يخصّه (اللون، رقم المحرك، السعر الخاص، الملاحظات). تُملأ في قراءة الفاتورة المفردة فقط.</summary>
    public List<InvoiceVehicleLineDto> Chassis { get; set; } = new();
}
