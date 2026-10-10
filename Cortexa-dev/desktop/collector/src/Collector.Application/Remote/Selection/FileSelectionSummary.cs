namespace Collector.Application.Remote.Selection;

public sealed record FileSelectionSummary(
    int SelectedFiles,
    long SelectedBytes,
    int SkippedUnsupported,
    int SkippedTooLarge,
    int SupportedSelected,
    bool OverLimit,
    int UnknownSizeFiles = 0);
