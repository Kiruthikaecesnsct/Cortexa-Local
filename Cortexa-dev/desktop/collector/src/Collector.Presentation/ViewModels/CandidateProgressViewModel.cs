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

    public double OverallFraction => _state.OverallFraction;

    public double Maximum => 1;

    public string StageCaption => CandidateStrings.StageCaption(_state.ActiveStage, _state.Completed, _state.Total);

    public BannerViewModel? RunBanner => _state.RunBanner;

    public BannerViewModel? TooLargeBanner => _state.TooLargeBanner;
}
