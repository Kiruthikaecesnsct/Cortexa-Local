using Collector.Domain.Upload;
using Collector.Server.Application.Upload.Validation;

namespace Collector.Server.Application.Upload;

public enum UploadOutcomeKind
{
    Created,
    Replayed,
    Invalid,
    Conflict
}

public sealed record UploadOutcome(
    UploadOutcomeKind Kind,
    KnowledgeUploadResult? Result,
    IReadOnlyList<UploadValidationError> Errors)
{
    public bool IsReplay => Kind == UploadOutcomeKind.Replayed;

    public bool IsInvalid => Kind == UploadOutcomeKind.Invalid;

    public bool IsConflict => Kind == UploadOutcomeKind.Conflict;

    public static UploadOutcome Created(KnowledgeUploadResult result) => new(UploadOutcomeKind.Created, result, []);

    public static UploadOutcome Replayed(KnowledgeUploadResult result) => new(UploadOutcomeKind.Replayed, result, []);

    public static UploadOutcome Invalid(IReadOnlyList<UploadValidationError> errors) =>
        new(UploadOutcomeKind.Invalid, null, errors);

    public static UploadOutcome Conflict() => new(UploadOutcomeKind.Conflict, null, []);
}
