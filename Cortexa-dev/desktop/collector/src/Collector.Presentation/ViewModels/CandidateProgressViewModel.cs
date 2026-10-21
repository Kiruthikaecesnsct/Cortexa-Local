using System.Windows.Input;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Collector.Presentation.ViewModels;

public sealed class CandidateProgressViewModel : ObservableObject
{
    private readonly CandidateRunState _state;

    public CandidateProgressViewModel(CandidateRunState state)
    {
        _state = state;
        _state.Changed += (_, _) => UiThread.Post(() => OnPropertyChanged(string.Empty));
    }

    public ICommand? CancelCommand { get; set; }

    public bool IsVisible => _state.IsRunning || _state.Result is not null || _state.Failed;

    public bool IsRunning => _state.IsRunning;

    public int Completed => _state.Completed;

    public double Maximum => Math.Max(1, _state.Total);

    public string ProgressText => CandidateStrings.ProgressText(_state.Completed, _state.Total);

    public BannerViewModel? RunBanner => _state.RunBanner;

    public BannerViewModel? TooLargeBanner => _state.TooLargeBanner;
}
