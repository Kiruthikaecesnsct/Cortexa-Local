using Collector.Application.Remote;
using Collector.Domain.Enums;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Collector.Presentation.ViewModels;

public sealed partial class RemoteSourceViewModel
{
    private RemoteFetchProgress? _lastProgress;
    private bool _isPaused;
    private BannerViewModel? _rateBanner;

    public bool IsPaused => _isPaused;

    private bool CanFetch() =>
        AreControlsEnabled
        && !IsLoadingBranches
        && SelectedRepository is { IsTooBig: false }
        && SelectedBranch is not null;

    [RelayCommand(CanExecute = nameof(CanFetch), IncludeCancelCommand = true)]
    private async Task FetchAsync(CancellationToken cancellationToken)
    {
        var request = new RemoteFetchRequest(SelectedRepository!.Repository, SelectedBranch!.Name);
        BeginFetch(request.Repository.Provider);
        RemoteFetchResult? result = null;
        try
        {
            var progress = new SyncProgress<RemoteFetchProgress>(OnProgress);
            result = await _deps.Fetcher.FetchAsync(request, progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            ShowOutcome(_banners.Canceled());
        }
        catch (RemoteSourceException ex)
        {
            ShowFailure(ex.Kind, RetryDelay(ex.ResetAt), () => FetchCommand.ExecuteAsync(null));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected failure fetching {Repository}.", request.Repository.FullName);
            ShowFailure(RemoteFailureKind.Upstream, null, () => FetchCommand.ExecuteAsync(null));
        }
        finally
        {
            EndFetch();
        }

        if (result is not null)
        {
            CompleteFetch(request, result);
        }
    }

    [RelayCommand(CanExecute = nameof(CanFetch))]
    private Task FetchAgainAsync() => FetchCommand.ExecuteAsync(null);

    [RelayCommand(CanExecute = nameof(AreControlsEnabled))]
    private void ChangeRepository()
    {
        Summary = null;
        Banner = null;
        RequestFocus(RemoteFocusKeys.Repositories);
    }

    public void ApplyRateStatus(RateLimitStatus status)
    {
        if (!IsFetching || status.Provider != SelectedSource)
        {
            return;
        }

        if (status.IsPaused && status.PausedUntil is { } until)
        {
            BeginPause(status.Provider, until);
            return;
        }

        EndPause();
    }

    private void OnRateStatusChanged(object? sender, RateLimitStatus status) =>
        UiThread.Post(() => ApplyRateStatus(status));

    private TimeSpan? RetryDelay(DateTimeOffset? resetAt) => resetAt is { } at ? at - _time.GetUtcNow() : null;

    private void BeginFetch(SourceType provider)
    {
        Banner = null;
        Summary = null;
        _lastProgress = null;
        RequestFocus(RemoteFocusKeys.Cancel);
        IsFetching = true;
        RefreshProgress();
        ApplyRateStatus(_deps.RateLimits.GetStatus(provider));
    }

    private void EndFetch()
    {
        IsFetching = false;
        _countdown.Stop();
        _isPaused = false;
        _rateBanner = null;
        OnPropertyChanged(nameof(IsPaused));
    }

    private void OnProgress(RemoteFetchProgress progress)
    {
        if (!IsFetching)
        {
            return;
        }

        _lastProgress = progress;
        RefreshProgress();
    }

    private void RefreshProgress()
    {
        if (_lastProgress is not { Phase: not RemoteFetchPhase.ReadingTree } progress)
        {
            IsProgressIndeterminate = true;
            ProgressText = RemoteSourceStrings.ReadingTree;
            return;
        }

        IsProgressIndeterminate = false;
        ProgressMaximum = Math.Max(1, progress.Total);
        ProgressValue = progress.Processed;
        ProgressText = _isPaused
            ? RemoteSourceStrings.Paused(progress.Processed, progress.Total)
            : RemoteSourceStrings.Progress(progress.Processed, progress.Total);
    }

    private void BeginPause(SourceType provider, DateTimeOffset until)
    {
        if (!_isPaused)
        {
            _isPaused = true;
            _rateBanner = new BannerViewModel(_banners.Paused(provider, until - _time.GetUtcNow()));
            Banner = _rateBanner;
            OnPropertyChanged(nameof(IsPaused));
            RefreshProgress();
        }

        _countdown.Start(until, OnCountdownTick);
    }

    private void EndPause()
    {
        if (!_isPaused)
        {
            return;
        }

        _isPaused = false;
        _countdown.Stop();
        _rateBanner = null;
        Banner = new BannerViewModel(_banners.Resumed());
        OnPropertyChanged(nameof(IsPaused));
        RefreshProgress();
    }

    private void OnCountdownTick(TimeSpan remaining)
    {
        if (_rateBanner is not null)
        {
            _rateBanner.Message = RemoteSourceStrings.RateMessage(CountdownFormatter.Format(remaining));
        }
    }

    private void CompleteFetch(RemoteFetchRequest request, RemoteFetchResult result)
    {
        var repository = request.Repository;
        if (result.LocalPaths.Count == 0)
        {
            ShowOutcome(_banners.NoFiles(repository.FullName, request.Branch));
            return;
        }

        Summary = BuildSummary(request, result);
        Banner = result.Truncated
            ? new BannerViewModel(_banners.Truncated(result.Source, result.Downloaded + result.CacheHits))
            : null;
        var origin = $"{repository.FullName} @ {request.Branch}";
        FilesFetched?.Invoke(this, new RemoteFilesFetchedEventArgs(result.LocalPaths, result.Source, origin));
    }

    private static RemoteFetchSummary BuildSummary(RemoteFetchRequest request, RemoteFetchResult result)
    {
        var commit = result.CommitSha.Length > 7 ? result.CommitSha[..7] : result.CommitSha;
        var counts = RemoteSourceStrings.Summary(result.Downloaded, result.CacheHits, result.SkippedByFilter, result.TooLarge);
        return new RemoteFetchSummary(
            request.Repository.FullName,
            $"{request.Branch} · {commit} · {counts}",
            request.Repository.IsPrivate);
    }
}
