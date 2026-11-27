namespace ERP.Core.DTOs.Accounting;

public class TailoringBoardCardDto
{
    public Guid GarmentId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? StageId { get; set; }
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string GarmentTypeName { get; set; } = string.Empty;
    public Guid GarmentTypeId { get; set; }
    public string? WearerName { get; set; }
    public Guid? EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public DateTime? DueDate { get; set; }
    public string Priority { get; set; } = string.Empty;
    public bool IsLate { get; set; }
    public DateTime? StageStartedAt { get; set; }
    public int OpenModifications { get; set; }
    public DateTime? NextFittingAt { get; set; }
}

public class TailoringWorkerDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class TailoringBoardDto
{
    public List<TailoringStageDto> Stages { get; set; } = new();
    public List<TailoringBoardCardDto> Cards { get; set; } = new();
    public List<TailoringWorkerDto> Workers { get; set; } = new();
}

public class TailoringAssignDto
{
    public List<Guid> GarmentIds { get; set; } = new();
    public Guid? EmployeeId { get; set; }
}

/// <summary>نقل القطعة: إلى مرحلة (<see cref="StageId"/>)، أو إلى «جاهزة» (<see cref="Ready"/>)، مع المنفّذ التالي اختيارياً.</summary>
public class TailoringMoveDto
{
    public Guid? StageId { get; set; }
    public bool Ready { get; set; }
    public Guid? EmployeeId { get; set; }
    public string? Note { get; set; }
}

public class TailoringStageHistoryDto
{
    public string? FromStage { get; set; }
    public string? ToStage { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public string? EmployeeName { get; set; }
    public int DurationMinutes { get; set; }
    public string? Note { get; set; }
    public string? ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; }
}

public class SaveTailoringFittingDto
{
    public Guid GarmentId { get; set; }
    public DateTime ScheduledAt { get; set; }
    public string? Notes { get; set; }
}

public class TailoringFittingResultDto
{
    /// <summary>done | no_show | cancelled</summary>
    public string Status { get; set; } = "done";
    /// <summary>approved | modification (للمنجزة).</summary>
    public string? Result { get; set; }
    public string? Notes { get; set; }
    /// <summary>تعليمات التعديل (إلزامية مع نتيجة «تعديل»).</summary>
    public string? Instructions { get; set; }
    public Guid? EmployeeId { get; set; }
    public DateTime? NextFittingAt { get; set; }
}

public class TailoringFittingDto
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public Guid GarmentId { get; set; }
    public string GarmentCode { get; set; } = string.Empty;
    public DateTime ScheduledAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Result { get; set; }
    public string? Notes { get; set; }
}

public class SaveTailoringModificationDto
{
    public Guid GarmentId { get; set; }
    public string Instructions { get; set; } = string.Empty;
    public Guid? EmployeeId { get; set; }
}

public class TailoringModificationDto
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public Guid GarmentId { get; set; }
    public string GarmentCode { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string? EmployeeName { get; set; }
    public Guid? EmployeeId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
