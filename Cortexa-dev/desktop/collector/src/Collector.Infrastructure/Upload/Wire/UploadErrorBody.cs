namespace Collector.Infrastructure.Upload.Wire;

internal sealed record UploadErrorBody(string? Error, string? Message, IReadOnlyList<UploadErrorDetail>? Errors);

internal sealed record UploadErrorDetail(string? Field, string? Message);
