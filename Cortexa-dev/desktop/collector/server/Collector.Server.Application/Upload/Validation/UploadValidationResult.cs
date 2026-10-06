using Collector.Domain.Upload;

namespace Collector.Server.Application.Upload.Validation;

public sealed record UploadValidationResult(
    KnowledgeUploadRequest? Request,
    IReadOnlyList<UploadValidationError> Errors)
{
    public static UploadValidationResult Valid(KnowledgeUploadRequest request) => new(request, []);

    public static UploadValidationResult Invalid(IReadOnlyList<UploadValidationError> errors) => new(null, errors);
}
