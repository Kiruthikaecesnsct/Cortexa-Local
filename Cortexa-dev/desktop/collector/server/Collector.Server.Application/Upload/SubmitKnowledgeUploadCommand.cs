using Collector.Domain.Upload;

namespace Collector.Server.Application.Upload;

public sealed record SubmitKnowledgeUploadCommand(
    KnowledgeUploadRequest Request,
    UploadCaller Caller,
    string IdempotencyKey);
