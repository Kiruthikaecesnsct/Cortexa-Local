using System.Collections.ObjectModel;
using System.ComponentModel;
using Collector.Domain.History;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Collector.Presentation.ViewModels;

public enum ResultsPhase
{
    Loading,
    Loaded,
    NotFound,
    Failed,
}

public sealed partial class HistoryViewModel
{
    private static readonly string[] ViewStateNames =
    [
        nameof(ShowLoading),
        nameof(ShowPanes),
        nameof(ShowEmpty),
        nameof(ShowPlaceholder),
        nameof(ShowDetail),
        nameof(ShowNotFound),
        nameof(ShowResultsLoading),
        nameof(ShowCandidateList),
        nameof(ShowNoCandidates),
        nameof(NoCandidatesTitle),
        nameof(NoCandidatesBody),
        nameof(CandidateCountText),
        nameof(PollingCaption),
    ];

    private readonly KeyedMerge<CandidateRowViewModel, BatchCandidate> _candidateMerge;
    private readonly Dictionary<string, HashSet<string>> _expanded = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unavailable = new(StringComparer.Ordinal);
    private CancellationTokenSource? _results;
    private HistorySourceContext? _context;
    private string? _activeBatchId;
    private bool _resultsFailed;

    public ObservableCollection<CandidateRowViewModel> Candidates { get; }

    public Task ResultsTask { get; private set; } = Task.CompletedTask;

    public bool ShowLoading => !_hasLoaded && !_listFailed;

    public bool ShowPanes => _hasLoaded && Batches.Count > 0;

    public bool ShowEmpty => _hasLoaded && Batches.Count == 0;

    public bool ShowPlaceholder => ShowPanes && SelectedBatch is null;

    public bool ShowDetail => ShowPanes && SelectedBatch is not null && ResultsState != ResultsPhase.NotFound;

    public bool ShowNotFound => ShowPanes && SelectedBatch is not null && ResultsState == ResultsPhase.NotFound;

    public bool ShowResultsLoading => ResultsState == ResultsPhase.Loading;

    public bool ShowCandidateList => Candidates.Count > 0;

    public bool ShowNoCandidates => ResultsState == ResultsPhase.Loaded && Candidates.Count == 0;

    public string CandidateCountText => HistoryStrings.CandidateCount(Candidates.Count);

    public string NoCandidatesTitle => SelectedBatch?.StateKind switch
    {
        BatchState.Completed => HistoryStrings.NoCandidatesDoneTitle,
        BatchState.Failed or BatchState.Cancelled => HistoryStrings.NoCandidatesStoppedTitle,
        _ => HistoryStrings.NoCandidatesYetTitle,
    };

    public string NoCandidatesBody => SelectedBatch?.StateKind switch
    {
        BatchState.Completed => HistoryStrings.NoCandidatesDoneBody,
        BatchState.Failed or BatchState.Cancelled => HistoryStrings.NoCandidatesStoppedBody(SelectedBatch.StageText),
        _ => HistoryStrings.NoCandidatesYetBody,
    };

    [ObservableProperty]
    public partial ResultsPhase ResultsState { get; set; } = ResultsPhase.Loading;

    private KeyedMerge<CandidateRowViewModel, BatchCandidate> CreateCandidateMerge() =>
        new(
            row => row.CandidateId,
            candidate => candidate.CandidateId,
            CreateCandidate,
            (row, candidate) => row.Update(candidate))
        {
            InsertIndex = CandidateInsertIndex,
        };

    private static int CandidateInsertIndex(IList<CandidateRowViewModel> rows, BatchCandidate candidate)
    {
        if (!CandidateFormat.IsHarvesting(candidate.Engine))
        {
            return rows.Count;
        }

        var last = rows.Count - 1;
        while (last >= 0 && !rows[last].IsHarvesting)
        {
            last--;
        }

        return last + 1;
    }

    private CandidateRowViewModel CreateCandidate(BatchCandidate candidate)
    {
        var row = new CandidateRowViewModel(candidate, _context!);
        row.IsExpanded = _activeBatchId is { } id && _expanded.TryGetValue(id, out var open) && open.Contains(row.CandidateId);
        row.PropertyChanged += OnCandidateChanged;
        return row;
    }

    private void OnCandidateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CandidateRowViewModel.IsExpanded) || sender is not CandidateRowViewModel row || _activeBatchId is not { } id)
        {
            return;
        }

        if (!_expanded.TryGetValue(id, out var open))
        {
            open = new HashSet<string>(StringComparer.Ordinal);
            _expanded[id] = open;
        }

        _ = row.IsExpanded ? open.Add(row.CandidateId) : open.Remove(row.CandidateId);
    }

    private void Activate(HistoryBatchRowViewModel? batch, TimeSpan delay)
    {
        _activeBatchId = batch?.BatchId;
        CancelResults();
        Candidates.Clear();
        _resultsFailed = false;
        ResultsState = ResultsPhase.Loading;
        _context = null;
        if (batch is not null)
        {
            _unavailable.Remove(batch.BatchId);
            _services.Resolver.Invalidate(batch.BatchId);
            _context = new HistorySourceContext(batch.BatchId, _services.Resolver, _services.Launcher);
            StartResultsLoad(batch.BatchId, delay);
        }

        RefreshBanner();
        NotifyViewState();
    }

    private void StartResultsLoad(string batchId, TimeSpan delay)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _results = cts;
        ResultsTask = LoadAfterDelayAsync(batchId, delay, cts.Token);
    }

    private void CancelResults()
    {
        var previous = _results;
        _results = null;
        previous?.Cancel();
        previous?.Dispose();
    }

    private async Task LoadAfterDelayAsync(string batchId, TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, _services.Time, cancellationToken);
            }

            await LoadResultsAsync(batchId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SyncResultsAsync(bool manual, bool selectionChanged, CancellationToken cancellationToken)
    {
        if (SelectedBatch is not { } selected)
        {
            return;
        }

        if (selectionChanged)
        {
            await ResultsTask;
            return;
        }

        if (manual)
        {
            _unavailable.Remove(selected.BatchId);
        }

        var due = manual || selected.IsInProgress || _selectedChange != RowChange.None;
        if (due && !_unavailable.Contains(selected.BatchId))
        {
            await LoadResultsAsync(selected.BatchId, cancellationToken);
        }
    }

    private async Task LoadResultsAsync(string batchId, CancellationToken cancellationToken)
    {
        var fetch = await HistoryFetch.ResultsAsync(_services.Client, batchId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await UiThread.InvokeAsync(() => ApplyResults(batchId, fetch));
    }

    private void ApplyResults(string batchId, Fetch<BatchResults> fetch)
    {
        if (!string.Equals(batchId, _activeBatchId, StringComparison.Ordinal))
        {
            return;
        }

        if (fetch.Status == FetchStatus.AuthFailed)
        {
            StopForAuthFailure();
            return;
        }

        _resultsFailed = fetch.Status == FetchStatus.Failed;
        ResultsState = PhaseFor(fetch.Status);
        if (fetch.Status == FetchStatus.Ok)
        {
            _candidateMerge.Apply(Candidates, fetch.Value!.Candidates);
        }

        if (fetch.Status == FetchStatus.NotFound)
        {
            _unavailable.Add(batchId);
            Candidates.Clear();
        }

        RefreshBanner();
        NotifyViewState();
    }

    private ResultsPhase PhaseFor(FetchStatus status) => status switch
    {
        FetchStatus.Ok => ResultsPhase.Loaded,
        FetchStatus.NotFound => ResultsPhase.NotFound,
        _ => ResultsState == ResultsPhase.Loaded ? ResultsPhase.Loaded : ResultsPhase.Failed,
    };

    private void NotifyViewState()
    {
        foreach (var name in ViewStateNames)
        {
            OnPropertyChanged(name);
        }

        OnPropertyChanged(nameof(HasActive));
    }
}
