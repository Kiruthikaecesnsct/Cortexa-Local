using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Collector.Presentation.ViewModels;

public sealed class CandidatesPanelViewModel : ObservableObject
{
    private readonly CandidateRunState _state;

    public CandidatesPanelViewModel(CandidateRunState state)
    {
        _state = state;
        Items = [];
        _state.Changed += (_, _) => UiThread.Post(Refresh);
        Refresh();
    }

    public IReadOnlyList<CandidateItemViewModel> Items { get; private set; }

    public BannerViewModel? RunBanner => _state.RunBanner;

    public BannerViewModel? TooLargeBanner => _state.TooLargeBanner;

    public bool HasItems => Items.Count > 0;

    public bool HasContent => HasItems || _state.HasBanner;

    public bool IsVisible => HasContent || ShowEmpty;

    public bool ShowEmpty => !HasItems && _state.Result is not null && _state.RunBanner is null;

    public string CountText => CandidateStrings.CountText(Items.Count);

    private void Refresh()
    {
        Items = [.. (_state.Result?.AllCandidates ?? []).Select(candidate => new CandidateItemViewModel(candidate))];
        OnPropertyChanged(string.Empty);
    }
}
