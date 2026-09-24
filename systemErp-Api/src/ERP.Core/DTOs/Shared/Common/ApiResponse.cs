namespace ERP.Core.DTOs.Shared;

/// <summary>مغلّف الاستجابة الموحّد - يطابق ApiResponse في backend-api.models.ts.</summary>
public class ApiResponse<T>
{
    public bool Success { get; set; } = true;
    public string? Message { get; set; }
    public T? Data { get; set; }
    public List<string>? Errors { get; set; }
    public int StatusCode { get; set; } = 200;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public static ApiResponse<T> Ok(T data, string? message = null) => new() { Data = data, Message = message };
    public static ApiResponse<T> Fail(int statusCode, string message, List<string>? errors = null) =>
        new() { Success = false, StatusCode = statusCode, Message = message, Errors = errors };
}
