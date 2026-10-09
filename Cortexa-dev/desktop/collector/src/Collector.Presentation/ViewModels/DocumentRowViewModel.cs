using System.IO;
using Collector.Application.Extraction;
using Collector.Domain.Enums;
using Collector.Presentation.Resources;

namespace Collector.Presentation.ViewModels;

public sealed class DocumentRowViewModel
{
    private const char RepoSeparator = '/';

    public DocumentRowViewModel(SplitOutcome outcome, string? origin = null)
    {
        var result = outcome.Result;
        SourcePath = result.SourcePath;
        Origin = origin;
        Filename = Path.GetFileName(outcome.RepoPath ?? result.SourcePath);
        Folder = FolderOf(outcome.RepoPath, result.SourcePath);
        DocumentId = result.DocumentId;
        Status = result.Status;
        SkipReasonRaw = result.Reason;
        UnitCount = outcome.UnitCount;
        TokenCount = outcome.TokenCount;
        PromptTokens = outcome.PromptTokens;
    }

    public string SourcePath { get; }

    public string? Origin { get; }

    public string SourceCaption => Origin is null ? SourcePath : $"{Origin} · {Filename}";

    public string Filename { get; }

    public string Folder { get; }

    public string? DocumentId { get; }

    public DocumentStatus Status { get; }

    public int UnitCount { get; }

    public int TokenCount { get; }

    public int PromptTokens { get; }

    public string? SkipReasonRaw { get; }

    public bool IsSkipped => Status is DocumentStatus.Excluded or DocumentStatus.Failed;

    public string StatusLabel => Status switch
    {
        DocumentStatus.Pending => ExtractionStrings.StatusPending,
        DocumentStatus.Extracting => ExtractionStrings.StatusExtracting,
        DocumentStatus.Extracted => ExtractionStrings.StatusAnalyzed,
        DocumentStatus.Failed => ExtractionStrings.StatusFailed,
        DocumentStatus.Excluded => ExtractionStrings.StatusExcluded,
        _ => ExtractionStrings.StatusPending,
    };

    public string AutomationName =>
        ExtractionStrings.DocumentAutomationName(Filename, Folder, UnitCount, TokenCount, StatusLabel);

    public string SkipAutomationName => ExtractionStrings.SkipAutomationName(Filename, SkipReasonText);

    public string RemoveAutomationName => $"{ExtractionStrings.RemoveName} {Filename}";

    public string SkipReasonText => SkipReasonTextFor(Status, SkipReasonRaw);

    private static string FolderOf(string? repoPath, string sourcePath)
    {
        var folder = repoPath is null ? Path.GetDirectoryName(sourcePath) : RepoDirectory(repoPath);
        return string.IsNullOrEmpty(folder) ? ExtractionStrings.FolderRoot : folder;
    }

    private static string RepoDirectory(string repoPath)
    {
        var index = repoPath.LastIndexOf(RepoSeparator);
        return index < 0 ? string.Empty : repoPath[..index];
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
