using ERP.Domain.Common;
using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

public class CarProcurementOrderItem : BaseEntity
{
    public Guid OrderId { get; set; }
    public string BrandName { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string? TrimName { get; set; }
    public int Year { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total => Quantity * UnitPrice;

    /// <summary>أرقام الشواسي (VIN) المستلمة لهذا البند، مفصولة بفاصلة.</summary>
    public string? AssignedVins { get; set; }

    public virtual CarProcurementOrder Order { get; set; } = null!;
}

/// <summary>
/// أمر توريد وشراء سيارات - يمر بدورة الشراء المعتمدة من 7 مراحل (<see cref="ProcurementStage"/>).
/// </summary>
public class CarProcurementOrder : BaseEntity
{
    public string OrderNumber { get; set; } = string.Empty;
    public ProcurementStage Stage { get; set; } = ProcurementStage.Requisition;
    public DateTime Date { get; set; }

    public Guid SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;

    public string PaymentType { get; set; } = "cash";
    public decimal Subtotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }

    public ProcurementOrderStatus Status { get; set; } = ProcurementOrderStatus.Draft;
    public string? Notes { get; set; }

    public virtual ICollection<CarProcurementOrderItem> Items { get; set; } = new List<CarProcurementOrderItem>();

    public void UpdateStageAndStatus(ProcurementStage stage, ProcurementOrderStatus status)
    {
        Stage = stage;
        Status = status;
    }
}
