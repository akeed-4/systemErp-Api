using ERP.Domain.Common;
using ERP.Domain.Enums;

namespace ERP.Domain.Entities;

/// <summary>
/// عقد بيع سيارة - يمر بدورة مبيعات معتمدة من 5 مراحل (<see cref="SalesContractStatus"/>).
/// </summary>
public class CarSalesContract : BaseEntity
{
    public string ContractNumber { get; set; } = string.Empty;
    public CarSalesCycleType CycleType { get; set; }
    public DateTime Date { get; set; }

    public BuyerType BuyerType { get; set; }
    public string BuyerName { get; set; } = string.Empty;
    public string BuyerNationalIdOrCr { get; set; } = string.Empty;
    public string BuyerPhone { get; set; } = string.Empty;
    public string? BuyerEmail { get; set; }
    public string? BuyerAddress { get; set; }

    public string? FinancingBankName { get; set; }
    public decimal? DownPaymentAmount { get; set; }
    public decimal? FinancedAmount { get; set; }

    public Guid VehicleId { get; set; }
    public string Vin { get; set; } = string.Empty;
    public string VehicleDescription { get; set; } = string.Empty;

    public decimal CostPrice { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalWithVat { get; set; }

    public string PaymentMethod { get; set; } = "cash";
    public SalesContractStatus Status { get; set; } = SalesContractStatus.Draft;
    public string? Notes { get; set; }

    public string? HandoverProtocolNumber { get; set; }
    public string? SalespersonName { get; set; }

    public void UpdateStatus(SalesContractStatus status)
    {
        Status = status;
    }

    public void UpdateHandover(string? handoverProtocolNumber, string? notes)
    {
        HandoverProtocolNumber = handoverProtocolNumber;
        if (!string.IsNullOrEmpty(notes))
        {
            Notes = notes;
        }
    }
}
