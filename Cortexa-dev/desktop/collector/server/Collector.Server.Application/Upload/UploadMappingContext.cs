namespace Collector.Server.Application.Upload;

public sealed record UploadMappingContext(
    UploadCaller Caller,
    string BatchId,
    ModelConfigSnapshot Models,
    string Engine,
    DateTimeOffset CreatedAt);
