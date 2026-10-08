using System.Globalization;
using Collector.Domain.History;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Collector.Presentation.ViewModels;

[Flags]
public enum RowChange
{
    None = 0,
    Counts = 1,
    Stage = 2,
    State = 4,
}

public sealed partial class HistoryBatchRowViewModel : ObservableObject
{
    private BatchSummary _summary;

    public HistoryBatchRowViewModel(BatchSummary summary)
    {
        _summary = summary;
        BatchId = summary.BatchId;
        Steps = [.. Enum.GetValues<BatchStage>().Select(stage => new StageStepViewModel(stage))];
        Name = string.Empty;
        CreatedText = string.Empty;
        StateText = string.Empty;
        StateGlyph = string.Empty;
        StageText = string.Empty;
        StageLine = string.Empty;
        AutomationName = string.Empty;
        UploadedText = string.Empty;
        ProgressName = string.Empty;
        Apply(summary);
    }

    public string BatchId { get; }

    public string HelpText => string.Empty;

    public IReadOnlyList<StageStepViewModel> Steps { get; }

    public DateTimeOffset CreatedAt => _summary.CreatedAt;

    public BatchStage Stage => _summary.Stage;

    public bool IsActive => BatchProgress.IsActive(StateKind);

    public bool IsInProgress => StateKind == BatchState.InProgress;

    public bool IsCompleted => StateKind == BatchState.Completed;

    public bool IsStopped => StateKind is BatchState.Failed or BatchState.Cancelled;

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string CreatedText { get; set; }

    [ObservableProperty]
    public partial string UploadedText { get; set; }

    [ObservableProperty]
    public partial BatchState StateKind { get; set; }

    [ObservableProperty]
    public partial string StateText { get; set; }

    [ObservableProperty]
    public partial string StateGlyph { get; set; }

    [ObservableProperty]
    public partial string StageText { get; set; }

    [ObservableProperty]
    public partial string StageLine { get; set; }

    [ObservableProperty]
    public partial string AutomationName { get; set; }

    [ObservableProperty]
    public partial bool ShowProgress { get; set; }

    [ObservableProperty]
    public partial double ProgressMax { get; set; }

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial string ProgressName { get; set; }

    public RowChange Update(BatchSummary summary)
    {
        if (summary == _summary)
        {
            return RowChange.None;
        }

        var change = Diff(_summary, summary);
        _summary = summary;
        Apply(summary);
        return change;
    }

    private static RowChange Diff(BatchSummary old, BatchSummary updated)
    {
        var change = RowChange.Counts;
        if (old.Stage != updated.Stage)
        {
            change |= RowChange.Stage;
        }

        if (!string.Equals(old.State, updated.State, StringComparison.Ordinal))
        {
            change |= RowChange.State;
        }

        return change;
    }

    private void Apply(BatchSummary summary)
    {
        Name = summary.BatchName;
        CreatedText = summary.CreatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        UploadedText = HistoryStrings.Uploaded(summary.CreatedAt.ToLocalTime());
        StateKind = BatchProgress.ParseState(summary.State);
        StateText = BatchProgress.StateText(StateKind, summary.State);
        StateGlyph = BatchProgress.StateGlyph(StateKind);
        StageText = HistoryStrings.StageName(summary.Stage);
        StageLine = BatchProgress.StageLine(summary);
        AutomationName = HistoryStrings.BatchAutomationName(Name, StateText, StageText, StageLine, CreatedText);
        foreach (var step in Steps)
        {
            step.Update(summary);
        }

        ApplyProgress(summary);
        OnPropertyChanged(nameof(CreatedAt));
        OnPropertyChanged(nameof(Stage));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsInProgress));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(IsStopped));
    }

    private void ApplyProgress(BatchSummary summary)
    {
        var counts = BatchProgress.ActiveCounts(summary);
        ShowProgress = counts is not null;
        ProgressMax = Math.Max(counts?.Total ?? 0, 1);
        ProgressValue = Math.Clamp(counts?.Done ?? 0, 0, ProgressMax);
        ProgressName = HistoryStrings.ProgressName(HistoryStrings.StageName(BatchProgress.ActiveStage(summary) ?? summary.Stage));
    }
}
