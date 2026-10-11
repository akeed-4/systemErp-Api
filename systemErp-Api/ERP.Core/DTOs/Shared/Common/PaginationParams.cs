namespace ERP.Core.DTOs.Shared;

/// <summary>يطابق PaginationParams في backend-api.models.ts.</summary>
public class PaginationParams
{
    public const int DefaultPageSize = 100;
    public const int MaxPageSize = 1000;

    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = DefaultPageSize;
    public string? SearchTerm { get; set; }
    public string? SortBy { get; set; }
    public bool IsDescending { get; set; } = true;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Status { get; set; }

    public int NormalizedPage => PageNumber < 1 ? 1 : PageNumber;
    public int NormalizedSize => PageSize < 1 ? DefaultPageSize : Math.Min(PageSize, MaxPageSize);
}
