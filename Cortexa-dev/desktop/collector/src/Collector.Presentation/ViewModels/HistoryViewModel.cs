using System.Collections.ObjectModel;
using System.IO;
using Collector.Application.Auth;
using Collector.Application.Candidates;
using Collector.Application.History;
using Collector.Application.Ports;
using Collector.Domain.Enums;
using Collector.Presentation.Navigation;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collector.Presentation.ViewModels;

public sealed record HistoryServices(
    IProcessedDocumentRepository Repository,
    ICandidateRunner Runner,
    ISessionState Session);

public sealed partial class HistoryViewModel(HistoryServices services, INavigationService navigation)
    : FocusableViewModel, INavigationAware
{
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(20);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource _lifetime = new();

    public ObservableCollection<HistoryDocumentRowViewModel> Documents { get; } = [];

    public Task LoadTask { get; private set; } = Task.CompletedTask;

    public bool ShowEmpty => !IsLoading && !HasLoadError && Documents.Count == 0;

    public bool ShowList => Documents.Count > 0;

    public int TotalCount => Documents.Count;

    public int SucceededCount => Documents.Count(row => row.IsSucceeded);

    public int FailedCount => Documents.Count(row => row.IsFailed);

    public int CandidateTotal => Documents.Sum(row => row.Candidates.Count);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmpty))]
    public partial bool HasLoadError { get; set; }

    [ObservableProperty]
    public partial BannerViewModel? Banner { get; set; }

    [ObservableProperty]
    public partial string StageAnnouncement { get; set; } = string.Empty;

    public void OnNavigatedTo()
    {
        _lifetime.Dispose();
        _lifetime = new CancellationTokenSource();
        LoadTask = LoadAsync(_lifetime.Token);
    }

    public void OnNavigatedFrom() => _lifetime.Cancel();

    [RelayCommand]
    private Task RefreshAsync(CancellationToken cancellationToken) => LoadAsync(cancellationToken);

    [RelayCommand]
    private void GoToReview() => navigation.NavigateTo(ScreenKeys.Review);

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            IsLoading = true;
            await FetchAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            IsLoading = false;
            _gate.Release();
        }
    }

    private async Task FetchAsync(CancellationToken cancellationToken)
    {
        var userId = UserKey.From(services.Session.UserEmail);
        if (userId is null)
        {
            ShowError(HistoryStrings.SignInRequired);
            return;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(LoadTimeout);
            var records = await services.Repository.ListAsync(userId, timeout.Token);
            Apply(records);
        }
        catch (ProcessedDocumentStoreException exception)
        {
            ShowError(exception.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            ShowError(HistoryStrings.LoadTimedOut);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ShowError(HistoryStrings.LoadFailed);
        }
    }

    private void Apply(IReadOnlyList<ProcessedDocumentRecord> records)
    {
        Documents.Clear();
        foreach (var record in records)
        {
            Documents.Add(new HistoryDocumentRowViewModel(record, RetryDocumentAsync, DeleteDocumentAsync));
        }

        HasLoadError = false;
        Banner = null;
        NotifySummaryChanged();
    }

    private void NotifySummaryChanged()
    {
        OnPropertyChanged(nameof(ShowList));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(SucceededCount));
        OnPropertyChanged(nameof(FailedCount));
        OnPropertyChanged(nameof(CandidateTotal));
    }

    private void ShowError(string message)
    {
        HasLoadError = true;
        Banner = new BannerViewModel(new BannerContent
        {
            Severity = BannerSeverity.Error,
            Title = HistoryStrings.LoadErrorTitle,
            Message = message,
            ActionText = HistoryStrings.Retry,
            ActionCommand = RefreshCommand,
        });
    }

    private static string RetryMessage(HistoryDocumentRowViewModel row, CandidateRunOutcome outcome)
    {
        var result = outcome.Result?.Documents.FirstOrDefault(document => document.DocumentId == row.DocumentId);
        return result switch
        {
            null => HistoryStrings.RetryFailed(row.Name),
            { Outcome: CandidateOutcome.Completed } => HistoryStrings.RetryDone(row.Name),
            { Reason: HistoryStrings.DocumentNotFoundReason } => HistoryStrings.SourceMissing(row.Name),
            _ => HistoryStrings.RetryFailedBecause(row.Name, result.Reason ?? result.Outcome.ToString()),
        };
    }

    private async Task DeleteDocumentAsync(HistoryDocumentRowViewModel row)
    {
        var userId = UserKey.From(services.Session.UserEmail);
        if (userId is null)
        {
            StageAnnouncement = HistoryStrings.SignInRequired;
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(LoadTimeout);
            await services.Repository.DeleteAsync(userId, row.DocumentId, timeout.Token);
        }
        catch (Exception exception) when (exception is ProcessedDocumentStoreException or OperationCanceledException)
        {
            StageAnnouncement = HistoryStrings.DeleteFailed(row.Name);
            return;
        }

        Documents.Remove(row);
        NotifySummaryChanged();
        StageAnnouncement = HistoryStrings.Deleted(row.Name);
    }

    private async Task RetryDocumentAsync(HistoryDocumentRowViewModel row)
    {
        if (row.Record.SourceType == SourceType.Local && !File.Exists(row.Record.SourcePath))
        {
            StageAnnouncement = HistoryStrings.SourceMissing(row.Name);
            return;
        }

        var outcome = await services.Runner.RunAsync([row.DocumentId], new Progress<CandidateProgress>(), CancellationToken.None);
        StageAnnouncement = RetryMessage(row, outcome);
        await LoadAsync(_lifetime.Token);
    }
}
