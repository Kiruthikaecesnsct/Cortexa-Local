using Collector.Domain.Upload;
using Collector.Server.Application.Upload.Validation;

namespace Collector.Server.Application.Upload;

public sealed record UploadOutcome(
    KnowledgeUploadResult? Result,
    bool IsReplay,
    IReadOnlyList<UploadValidationError> Errors)
{
    public bool IsInvalid => Result is null;

    public static UploadOutcome Created(KnowledgeUploadResult result) => new(result, false, []);

    public static UploadOutcome Replayed(KnowledgeUploadResult result) => new(result, true, []);

    public static UploadOutcome Invalid(IReadOnlyList<UploadValidationError> errors) => new(null, false, errors);
}
