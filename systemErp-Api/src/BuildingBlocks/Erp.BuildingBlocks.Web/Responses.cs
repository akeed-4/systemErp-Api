using Microsoft.EntityFrameworkCore;

namespace Erp.BuildingBlocks.Web;

/// <summary>The envelope defined by the frontend (backend-api.models.ts). Errors carry an RFC 7807 problem in both languages.</summary>
public sealed record ApiResponse<T>
{
    public bool Success { get; init; }

    public string? Message { get; init; }

    public T? Data { get; init; }

    public IReadOnlyList<string>? Errors { get; init; }

    public int? StatusCode { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public ErpProblem? Problem { get; init; }
}

public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data, string? message = null, int statusCode = 200) =>
        new() { Success = true, Data = data, Message = message, StatusCode = statusCode };
}

/// <summary>RFC 7807 problem details with Arabic companions for title and detail.</summary>
public sealed record ErpProblem(
    string Type,
    string Title,
    string TitleAr,
    int Status,
    string Detail,
    string DetailAr,
    string Code,
    string? Instance,
    string? TraceId,
    IReadOnlyDictionary<string, string[]>? Errors);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int PageNumber, int PageSize)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasNextPage => PageNumber < TotalPages;

    public bool HasPreviousPage => PageNumber > 1;
}

/// <summary>Query-string paging/filtering parameters (names match the frontend's PaginationParams). Bind with [AsParameters].</summary>
public sealed class PaginationParams
{
    public const int MaxPageSize = 200;

    public int? PageNumber { get; set; }

    public int? PageSize { get; set; }

    public string? SearchTerm { get; set; }

    public string? SortBy { get; set; }

    public bool? IsDescending { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public string? Status { get; set; }

    public int Page => Math.Max(1, PageNumber ?? 1);

    public int Size => Math.Clamp(PageSize ?? 20, 1, MaxPageSize);
}

public static class PagingExtensions
{
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query,
        PaginationParams paging,
        CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip((paging.Page - 1) * paging.Size).Take(paging.Size).ToListAsync(cancellationToken);
        return new PagedResult<T>(items, total, paging.Page, paging.Size);
    }
}
