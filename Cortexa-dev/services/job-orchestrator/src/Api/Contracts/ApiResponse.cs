namespace Cortexa.JobOrchestrator.Api.Contracts;

public sealed class ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? Message { get; init; }
    public string? ErrorCode { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public Dictionary<string, object>? Details { get; init; }

    public static ApiResponse<T> Ok(T data, string correlationId) =>
        new() { Success = true, Data = data, CorrelationId = correlationId };

    public static ApiResponse<T> Fail(string errorCode, string message, string correlationId) =>
        new() { Success = false, ErrorCode = errorCode, Message = message, CorrelationId = correlationId };
}
