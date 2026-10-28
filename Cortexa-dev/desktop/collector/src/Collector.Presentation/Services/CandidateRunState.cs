using System.Windows.Input;
using Collector.Application.Candidates;
using Collector.Presentation.ViewModels;
using CommunityToolkit.Mvvm.Input;

namespace Collector.Presentation.Services;

public sealed class CandidateRunState
{
    public CandidateRunState()
    {
        DismissRunBannerCommand = new RelayCommand(() => SetBanners(null, TooLargeBanner));
        DismissTooLargeBannerCommand = new RelayCommand(() => SetBanners(RunBanner, null));
    }

    public bool IsRunning { get; private set; }

    public int Completed { get; private set; }

    public int Total { get; private set; }

    public CandidateRunResult? Result { get; private set; }

    public int ConfiguredKeyCount { get; private set; }

    public bool Canceled { get; private set; }

    public bool Failed { get; private set; }

    public BannerViewModel? RunBanner { get; private set; }

    public BannerViewModel? TooLargeBanner { get; private set; }

    public ICommand DismissRunBannerCommand { get; }

    public ICommand DismissTooLargeBannerCommand { get; }

    public bool HasBanner => RunBanner is not null || TooLargeBanner is not null;

    public bool HasOutput => HasBanner || Result?.AllCandidates.Count > 0;

    public bool HasFinishedRun => Result is not null && !Canceled && !Failed;

    public event EventHandler? Changed;

    public void Begin(int documentCount = 0)
    {
        Reset();
        IsRunning = true;
        Total = documentCount;
        RaiseChanged();
    }

    public void Report(CandidateProgress progress)
    {
        Completed = progress.Completed;
        Total = progress.Total;
        RaiseChanged();
    }

    public void Set(CandidateRunOutcome outcome)
    {
        IsRunning = false;
        Result = outcome.Result;
        ConfiguredKeyCount = outcome.ConfiguredKeyCount;
        Canceled = outcome.Canceled;
        Failed = outcome.IsFailed;
        RaiseChanged();
    }

    public void SetBanners(BannerViewModel? run, BannerViewModel? tooLarge)
    {
        RunBanner = run;
        TooLargeBanner = tooLarge;
        RaiseChanged();
    }

    public void Clear()
    {
        Reset();
        RaiseChanged();
    }

    private void Reset()
    {
        IsRunning = false;
        Completed = 0;
        Total = 0;
        Result = null;
        ConfiguredKeyCount = 0;
        Canceled = false;
        Failed = false;
        RunBanner = null;
        TooLargeBanner = null;
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
