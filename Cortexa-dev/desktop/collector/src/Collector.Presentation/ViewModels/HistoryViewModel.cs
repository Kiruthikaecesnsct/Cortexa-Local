using System.Collections.ObjectModel;
using Collector.Application.History;
using Collector.Application.Ports;
using Collector.Domain.History;
using Collector.Infrastructure.Options;
using Collector.Presentation.Navigation;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;

namespace Collector.Presentation.ViewModels;

public static class HistoryFocusKeys
{
    public const string BatchList = "BatchList";
    public const string Retry = "BannerAction";
}

public sealed record HistoryServices(
    IBatchHistoryClient Client,
    LocalSourceResolver Resolver,
    ILocalFileLauncher Launcher,
    IClipboard Clipboard,
    TimeProvider Time,
    IOptions<HistoryOptions> Options);

public sealed partial class HistoryViewModel : FocusableViewModel, INavigationAware
{
    private static readonly TimeSpan SelectionDebounce = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan CopiedDuration = TimeSpan.FromSeconds(2);

    private readonly HistoryServices _services;
    private readonly HistoryPoller _poller;
    private readonly INavigationService _navigation;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly KeyedMerge<HistoryBatchRowViewModel, BatchSummary> _batchMerge;
    private CancellationTokenSource _lifetime = new();
    private bool _hasLoaded;
    private bool _listFailed;
    private bool _merging;
    private bool _authFailed;
    private DateTimeOffset? _lastUpdated;
    private RowChange _selectedChange;
    private string? _bannerKey;

    public HistoryViewModel(
        HistoryServices services,
        HistoryPoller poller,
        INavigationService navigation,
        FailedUploadsViewModel failedUploads)
    {
        FailedUploads = failedUploads;
        failedUploads.BatchUploaded += OnBatchUploaded;
        failedUploads.FocusRequested += (_, key) => RequestFocus(key);
        _services = services;
        _poller = poller;
        _navigation = navigation;
        _batchMerge = new KeyedMerge<HistoryBatchRowViewModel, BatchSummary>(
            row => row.BatchId,
            summary => summary.BatchId,
            summary => new HistoryBatchRowViewModel(summary),
            UpdateBatch)
        {
            InsertIndex = InsertIndexFor,
        };
        _candidateMerge = CreateCandidateMerge();
        Batches = [];
        Candidates = [];
        Batches.CollectionChanged += (_, _) => NotifyViewState();
        Candidates.CollectionChanged += (_, _) => NotifyViewState();
    }

    public ObservableCollection<HistoryBatchRowViewModel> Batches { get; }

    public FailedUploadsViewModel FailedUploads { get; }

    public Task LoadTask { get; private set; } = Task.CompletedTask;

    public bool HasActive => Batches.Any(batch => batch.IsActive);

    public string PollingCaption => _lastUpdated is not { } time
        ? string.Empty
        : HasActive ? HistoryStrings.Polling(time, PollSeconds) : HistoryStrings.Updated(time);

    public string CopyName => IsCopied ? ReviewStrings.Copied : HistoryStrings.CopyBatchId;

    private int PollSeconds => _services.Options.Value.PollSeconds;

    [ObservableProperty]
    public partial HistoryBatchRowViewModel? SelectedBatch { get; set; }

    [ObservableProperty]
    public partial BannerViewModel? Banner { get; set; }

    [ObservableProperty]
    public partial string StageAnnouncement { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CopyName))]
    public partial bool IsCopied { get; set; }

    public void OnNavigatedTo()
    {
        _lifetime.Dispose();
        _lifetime = new CancellationTokenSource();
        LoadTask = RunLoadAsync(_lifetime.Token);
    }

    public void OnNavigatedFrom()
    {
        _poller.Stop();
        _lifetime.Cancel();
        CancelResults();
    }

    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await _gate.WaitAsync(linked.Token);
        _authFailed = false;
        try
        {
            await RefreshCoreAsync(manual: true, linked.Token);
        }
        finally
        {
            _gate.Release();
        }
    }

    partial void OnSelectedBatchChanged(HistoryBatchRowViewModel? value)
    {
        NotifyViewState();
        if (value is null && _merging)
        {
            return;
        }

        if (!string.Equals(value?.BatchId, _activeBatchId, StringComparison.Ordinal))
        {
            Activate(value, _merging ? TimeSpan.Zero : SelectionDebounce);
        }
    }

    [RelayCommand]
    private Task RefreshAsync(CancellationToken cancellationToken) => ReloadAsync(cancellationToken);

    [RelayCommand]
    private async Task RetryAsync(CancellationToken cancellationToken)
    {
        await ReloadAsync(cancellationToken);
        if (Banner is null)
        {
            RequestFocus(HistoryFocusKeys.BatchList);
        }
    }

    [RelayCommand]
    private void GoToReview() => _navigation.NavigateTo(ScreenKeys.Review);

    [RelayCommand]
    private async Task CopyBatchIdAsync()
    {
        var id = SelectedBatch?.BatchId;
        if (string.IsNullOrEmpty(id) || !_services.Clipboard.TrySetText(id))
        {
            return;
        }

        IsCopied = true;
        await Task.Delay(CopiedDuration, _services.Time);
        IsCopied = false;
    }

    private async Task RunLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.WhenAll(FailedUploads.LoadAsync(cancellationToken), ReloadAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnBatchUploaded() => LoadTask = ReloadQuietlyAsync(_lifetime.Token);

    private async Task ReloadQuietlyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ReloadAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            await RefreshCoreAsync(manual: false, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RefreshCoreAsync(bool manual, CancellationToken cancellationToken)
    {
        var fetch = await HistoryFetch.BatchesAsync(_services.Client, cancellationToken);
        if (!await AcceptListAsync(fetch))
        {
            return;
        }

        var selectionChanged = false;
        await UiThread.InvokeAsync(() => selectionChanged = ApplyBatches(fetch.Value!));
        await SyncResultsAsync(manual, selectionChanged, cancellationToken);
        UpdatePolling();
    }

    private async Task<bool> AcceptListAsync(Fetch<IReadOnlyList<BatchSummary>> fetch)
    {
        if (fetch.Status == FetchStatus.AuthFailed)
        {
            StopForAuthFailure();
            return false;
        }

        await UiThread.InvokeAsync(() =>
        {
            _listFailed = fetch.Status != FetchStatus.Ok;
            RefreshBanner();
            NotifyViewState();
        });
        return !_listFailed;
    }

    private bool ApplyBatches(IReadOnlyList<BatchSummary> summaries)
    {
        var previousId = _activeBatchId;
        var previousIndex = Batches.ToList().FindIndex(batch => batch.BatchId == previousId);
        _selectedChange = RowChange.None;
        _merging = true;
        try
        {
            _batchMerge.Apply(Batches, summaries.OrderByDescending(summary => summary.CreatedAt));
            ReconcileSelection(previousId, previousIndex);
        }
        finally
        {
            _merging = false;
        }

        _hasLoaded = true;
        _lastUpdated = _services.Time.GetLocalNow();
        AnnounceChange();
        RefreshBanner();
        NotifyViewState();
        return !string.Equals(previousId, _activeBatchId, StringComparison.Ordinal);
    }

    private void UpdateBatch(HistoryBatchRowViewModel row, BatchSummary summary)
    {
        var change = row.Update(summary);
        if (string.Equals(row.BatchId, _activeBatchId, StringComparison.Ordinal))
        {
            _selectedChange = change;
        }
    }

    private static int InsertIndexFor(IList<HistoryBatchRowViewModel> rows, BatchSummary summary)
    {
        var index = 0;
        while (index < rows.Count && rows[index].CreatedAt >= summary.CreatedAt)
        {
            index++;
        }

        return index;
    }

    private void ReconcileSelection(string? previousId, int previousIndex)
    {
        var current = Batches.FirstOrDefault(batch => batch.BatchId == previousId);
        if (current is not null)
        {
            SelectedBatch = current;
            return;
        }

        if (Batches.Count == 0)
        {
            SelectedBatch = null;
            Activate(null, TimeSpan.Zero);
            return;
        }

        SelectedBatch = Batches[previousId is null ? 0 : Math.Min(previousIndex, Batches.Count - 1)];
        if (previousId is not null)
        {
            RequestFocus(HistoryFocusKeys.BatchList);
        }
    }

    private void AnnounceChange()
    {
        if (_selectedChange != RowChange.None && SelectedBatch is { } batch && AnnouncementFor(batch, _selectedChange) is { } text)
        {
            StageAnnouncement = text;
        }
    }

    private static string? AnnouncementFor(HistoryBatchRowViewModel batch, RowChange change) => batch.StateKind switch
    {
        BatchState.Completed when change.HasFlag(RowChange.State) => HistoryStrings.AnnounceCompleted(batch.Name),
        BatchState.Failed when change.HasFlag(RowChange.State) => HistoryStrings.AnnounceFailed(batch.Name, batch.StageText),
        _ when change.HasFlag(RowChange.Stage) => HistoryStrings.AnnounceStage(batch.Name, batch.StageText),
        _ => null,
    };

    private void UpdatePolling()
    {
        if (HasActive && !_authFailed)
        {
            _poller.Start(() => HasActive, PollOnceAsync);
            return;
        }

        _poller.Stop();
    }

    private void StopForAuthFailure()
    {
        _authFailed = true;
        _poller.Stop();
    }

    private void RefreshBanner()
    {
        if (!_hasLoaded && _listFailed)
        {
            if (ShowBanner("load", BannerSeverity.Error, HistoryStrings.LoadErrorTitle, HistoryStrings.LoadErrorBody))
            {
                RequestFocus(HistoryFocusKeys.Retry);
            }

            return;
        }

        if (_listFailed || _resultsFailed)
        {
            ShowBanner($"refresh|{_lastUpdated:O}|{HasActive}", BannerSeverity.Warning, HistoryStrings.RefreshErrorTitle, RefreshErrorBody());
            return;
        }

        _bannerKey = null;
        Banner = null;
    }

    private string RefreshErrorBody() =>
        _lastUpdated is not { } time
            ? string.Empty
            : HasActive ? HistoryStrings.RefreshErrorBody(time, PollSeconds) : HistoryStrings.RefreshErrorBodyStatic(time);

    private bool ShowBanner(string key, BannerSeverity severity, string title, string message)
    {
        if (string.Equals(_bannerKey, key, StringComparison.Ordinal))
        {
            return false;
        }

        _bannerKey = key;
        Banner = new BannerViewModel(new BannerContent
        {
            Severity = severity,
            Title = title,
            Message = message,
            ActionText = HistoryStrings.Retry,
            ActionCommand = RetryCommand,
        });
        return true;
    }
}
