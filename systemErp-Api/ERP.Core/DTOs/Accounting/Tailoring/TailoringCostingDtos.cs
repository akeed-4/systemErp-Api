namespace ERP.Core.DTOs.Accounting;

public class IssueTailoringMaterialDto
{
    public Guid OrderId { get; set; }
    public Guid? GarmentId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? WarehouseId { get; set; }
    public decimal Quantity { get; set; }
    public DateTime? Date { get; set; }
    public string? Notes { get; set; }
}

public class ReturnTailoringMaterialDto
{
    public Guid IssueId { get; set; }
    public decimal Quantity { get; set; }
    public DateTime? Date { get; set; }
    public string? Notes { get; set; }
}

public class TailoringMaterialDto
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid? GarmentId { get; set; }
    public string? GarmentCode { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? Unit { get; set; }
    public Guid WarehouseId { get; set; }
    public string? WarehouseName { get; set; }
    public string Kind { get; set; } = string.Empty;
    public Guid? ReturnOfId { get; set; }
    public decimal Quantity { get; set; }
    public decimal ReturnableQuantity { get; set; }
    /// <summary>بصلاحية رؤية التكلفة فقط.</summary>
    public decimal? UnitCost { get; set; }
    public decimal? TotalCost { get; set; }
    public DateTime Date { get; set; }
    public string? Notes { get; set; }
    public string? CreatedBy { get; set; }
}

public class SaveTailoringLaborDto
{
    public Guid GarmentId { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid? StageId { get; set; }
    public decimal Amount { get; set; }
    public DateTime? Date { get; set; }
    public string? Notes { get; set; }
}

public class TailoringLaborDto
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid GarmentId { get; set; }
    public string GarmentCode { get; set; } = string.Empty;
    public Guid? StageId { get; set; }
    public string? StageName { get; set; }
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime Date { get; set; }
    public bool IsManual { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string? CancelReason { get; set; }
}

public class TailoringWorkerSummaryDto
{
    public Guid EmployeeId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Pieces { get; set; }
    public decimal Amount { get; set; }
    public int OpenPieces { get; set; }
    public List<TailoringWorkerRateDto> Rates { get; set; } = new();
}

public class TailoringWorkerRateDto
{
    public Guid StageId { get; set; }
    public decimal Rate { get; set; }
}

public class SaveTailoringWorkerRatesDto
{
    public List<TailoringWorkerRateDto> Rates { get; set; } = new();
}

public class TailoringProfitabilityDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    /// <summary>الإيراد قبل الضريبة (صافي الأمر).</summary>
    public decimal Revenue { get; set; }
    public decimal MaterialsCost { get; set; }
    public decimal LaborCost { get; set; }
    public decimal TotalCost { get; set; }
    public decimal Profit { get; set; }
    public decimal MarginPercent { get; set; }
    public List<TailoringMaterialDto> Materials { get; set; } = new();
    public List<TailoringLaborDto> Labor { get; set; } = new();
}
