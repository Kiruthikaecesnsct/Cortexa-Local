using System.IO;
using Collector.Application.Extraction;
using Collector.Domain.Enums;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Collector.Presentation.ViewModels;

public sealed partial class DocumentRowViewModel(string sourcePath, string? origin = null) : ObservableObject
{
    public string SourcePath { get; } = sourcePath;

    public string? Origin { get; } = origin;

    public string SourceCaption => Origin is null ? SourcePath : $"{Origin} · {Filename}";

    public string Filename { get; } = Path.GetFileName(sourcePath);

    public string? DocumentId { get; private set; }

    public bool IsSkipped => Status is DocumentStatus.Excluded or DocumentStatus.Failed;

    public string StatusLabel => Status switch
    {
        DocumentStatus.Pending => ExtractionStrings.StatusPending,
        DocumentStatus.Extracting => ExtractionStrings.StatusExtracting,
        DocumentStatus.Extracted => ExtractionStrings.StatusExtracted,
        DocumentStatus.Failed => ExtractionStrings.StatusFailed,
        DocumentStatus.Excluded => ExtractionStrings.StatusExcluded,
        _ => ExtractionStrings.StatusPending,
    };

    public string AutomationName => ExtractionStrings.DocumentAutomationName(Filename, StatusLabel, UnitCount);

    public string SkipAutomationName => ExtractionStrings.SkipAutomationName(Filename, SkipReasonText);

    public string RemoveAutomationName => $"{ExtractionStrings.RemoveName} {Filename}";

    public string SkipReasonText => SkipReasonTextFor(Status, SkipReasonRaw);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel), nameof(AutomationName), nameof(IsSkipped), nameof(SkipReasonText), nameof(SkipAutomationName))]
    public partial DocumentStatus Status { get; set; } = DocumentStatus.Pending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    public partial int UnitCount { get; set; }

    [ObservableProperty]
    public partial int TokenCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SkipReasonText), nameof(SkipAutomationName))]
    public partial string? SkipReasonRaw { get; set; }

    public void ApplyResult(ExtractionResult result)
    {
        DocumentId = result.DocumentId;
        Status = result.Status;
        SkipReasonRaw = result.Reason;
    }

    public void ApplyUnitTotals(int unitCount, int tokenCount)
    {
        UnitCount = unitCount;
        TokenCount = tokenCount;
    }

    public static string SkipReasonTextFor(DocumentStatus status, string? reason)
    {
        if (status == DocumentStatus.Excluded && Enum.TryParse<SkipReason>(reason, out var skipReason))
        {
            return skipReason switch
            {
                SkipReason.TooLarge => ExtractionStrings.SkipTooLarge,
                SkipReason.BinaryContent => ExtractionStrings.SkipBinaryContent,
                SkipReason.UnsupportedFormat => ExtractionStrings.SkipUnsupportedFormat,
                _ => ExtractionStrings.SkipParseFailure,
            };
        }

        return status == DocumentStatus.Failed ? ExtractionStrings.SkipParseFailure : string.Empty;
    }
}
