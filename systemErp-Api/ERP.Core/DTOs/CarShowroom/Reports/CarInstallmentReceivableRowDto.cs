namespace ERP.Core.DTOs.CarShowroom;

/// <summary>ذمم البيع بالتقسيط/التمويل. لا يوجد جدول أقساط ولا سندات ربط بالعقد، لذا الرصيد المتبقي = الإجمالي - الدفعة المقدمة.</summary>
public class CarInstallmentReceivableRowDto
{
    public Guid Id { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerNationalId { get; set; } = string.Empty;
    public string VehicleDescription { get; set; } = string.Empty;
    public string Vin { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public decimal TotalContractAmount { get; set; }
    public decimal DownPayment { get; set; }
    public decimal RemainingBalance { get; set; }
    public int InstallmentsCount { get; set; }
    public decimal MonthlyInstallment { get; set; }
    public string BankOrShowroom { get; set; } = string.Empty;
}
