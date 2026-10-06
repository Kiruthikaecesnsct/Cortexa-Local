namespace Collector.Server.Application.Upload.Validation;

public sealed record UploadValidationError(string Field, string Message);
