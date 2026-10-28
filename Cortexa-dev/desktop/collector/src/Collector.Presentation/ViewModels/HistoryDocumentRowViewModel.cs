using System.Globalization;
using Collector.Application.Candidates;
using Collector.Application.History;
using Collector.Domain.Enums;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collector.Presentation.ViewModels;

public sealed partial class HistoryDocumentRowViewModel : ObservableObject
{
    private readonly Func<HistoryDocumentRowViewModel, Task> _retry;

    public HistoryDocumentRowViewModel(ProcessedDocumentRecord record, Func<HistoryDocumentRowViewModel, Task> retry)
    {
        Record = record;
        _retry = retry;
        Candidates = [.. record.Candidates.Select((candidate, index) => HistoryCandidateLine.From(index + 1, candidate))];
    }

    public ProcessedDocumentRecord Record { get; }

    public string DocumentId => Record.DocumentId;

    public string Name => Record.Filename;

    public IReadOnlyList<HistoryCandidateLine> Candidates { get; }

    public bool IsFailed => Record.Status == ProcessedStatus.Failed;

    public bool IsSucceeded => Record.Status == ProcessedStatus.Succeeded;

    public bool HasCandidates => Candidates.Count > 0;

    public bool ShowNoCandidates => IsSucceeded && !HasCandidates;

    public string StatusText => IsFailed ? HistoryStrings.FailedTag : HistoryStrings.DocumentDone;

    public string FailureText => HistoryStrings.FailureReason(Record.FailureReason);

    public string SourceText => Record.SourcePath;

    public string SizeText => HistoryFormat.Size(Record.SizeBytes);

    public string SourceTypeText => HistoryFormat.SourceType(Record.SourceType);

    public string StatusGlyph => IsFailed ? Glyphs.Error : Glyphs.Success;

    public string ToggleName => IsExpanded ? HistoryStrings.HideCandidates : HistoryStrings.ShowCandidates;

    public string ToggleGlyph => IsExpanded ? Glyphs.ChevronUp : Glyphs.ChevronDown;

    public string ProcessedText => Record.UpdatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string CandidateCountText => HistoryStrings.CandidateCount(Candidates.Count);

    public string RetryName => HistoryStrings.RetryDocument(Name);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial bool IsRetrying { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleName), nameof(ToggleGlyph))]
    public partial bool IsExpanded { get; set; } = true;

    [RelayCommand]
    private void ToggleCandidates() => IsExpanded = !IsExpanded;

    public string RetryLabel => IsRetrying ? HistoryStrings.Retrying : HistoryStrings.Retry;

    partial void OnIsRetryingChanged(bool value) => OnPropertyChanged(nameof(RetryLabel));

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private async Task RetryAsync()
    {
        IsRetrying = true;
        try
        {
            await _retry(this);
        }
        finally
        {
            IsRetrying = false;
        }
    }

    private bool CanRetry() => IsFailed && !IsRetrying;
}

public sealed record HistoryCandidateLine(int Number, string Title, string Summary, string Rationale, string? Quote)
{
    public bool HasQuote => !string.IsNullOrWhiteSpace(Quote);

    public static HistoryCandidateLine From(int number, InventionCandidate candidate) =>
        new(number, candidate.Title, candidate.Summary, candidate.PatentabilityRationale, candidate.Excerpt?.Quote);
}

public static class HistoryFormat
{
    private const double Kilo = 1024;

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / Kilo:0.#} KB",
        _ => $"{bytes / Kilo / Kilo:0.#} MB",
    };

    public static string SourceType(SourceType type) => type switch
    {
        Domain.Enums.SourceType.Local => "Local file",
        Domain.Enums.SourceType.Github => "GitHub",
        Domain.Enums.SourceType.AzureDevops => "Azure DevOps",
        Domain.Enums.SourceType.Ssh => "Remote machine",
        _ => "Cortexa repo",
    };
}
