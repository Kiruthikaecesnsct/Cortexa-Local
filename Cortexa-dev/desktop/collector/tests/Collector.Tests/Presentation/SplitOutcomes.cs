using Collector.Application.Extraction;
using Collector.Domain.Enums;

namespace Collector.Tests.Presentation;

internal static class SplitOutcomes
{
    public static SplitOutcome Make(
        string sourcePath,
        DocumentStatus status = DocumentStatus.Extracted,
        string? repoPath = null,
        int units = 0,
        int tokens = 0,
        int promptTokens = 0,
        string? documentId = null,
        string? reason = null) =>
        new(
            new ExtractionResult { SourcePath = sourcePath, DocumentId = documentId, Status = status, Reason = reason },
            repoPath,
            units,
            tokens,
            promptTokens);
}
