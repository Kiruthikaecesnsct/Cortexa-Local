using Collector.Domain.Enums;

namespace Collector.Application.Extraction;

public enum SkipReason
{
    TooLarge,
    BinaryContent,
    UnsupportedFormat,
    ParseFailure,
}

public sealed class DocumentStatusRules
{
    public DocumentStatus ForSkip(SkipReason reason) => reason switch
    {
        SkipReason.TooLarge => DocumentStatus.Excluded,
        SkipReason.BinaryContent => DocumentStatus.Excluded,
        SkipReason.UnsupportedFormat => DocumentStatus.Excluded,
        SkipReason.ParseFailure => DocumentStatus.Failed,
        _ => DocumentStatus.Failed,
    };

    public bool CanTransition(DocumentStatus from, DocumentStatus to) => (from, to) switch
    {
        (DocumentStatus.Pending, DocumentStatus.Extracting) => true,
        (DocumentStatus.Pending, DocumentStatus.Excluded) => true,
        (DocumentStatus.Pending, DocumentStatus.Failed) => true,
        (DocumentStatus.Extracting, DocumentStatus.Extracted) => true,
        (DocumentStatus.Extracting, DocumentStatus.Failed) => true,
        (DocumentStatus.Extracting, DocumentStatus.Excluded) => true,
        (DocumentStatus.Failed, DocumentStatus.Extracting) => true,
        (DocumentStatus.Extracted, DocumentStatus.Extracting) => true,
        _ => from == to,
    };
}
