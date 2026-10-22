using Collector.Application.Candidates;
using Collector.Presentation.Navigation;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.Input;

namespace Collector.Presentation.ViewModels;

public sealed partial class KnowledgeRunViewModel
{
    private async Task RunCandidatePassAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var state = _passes.CandidateState;
        var request = new CandidateRunRequest(_documentIds);
        state.Begin(request.DistinctDocumentIds.Count);
        var progress = new SyncProgress<CandidateProgress>(state.Report);
        var outcome = await _passes.Candidates.RunAsync(_documentIds, progress, cancellationToken);
        state.Set(outcome);
        var banners = CandidateBannerFactory.Create(state, CandidateActions(state));
        state.SetBanners(banners.Run, banners.TooLarge);
    }

    private CandidateBannerActions CandidateActions(CandidateRunState state) => new(
        new RelayCommand(() => _navigation.Settings.Open(SettingsSection.ProviderKeys)),
        state.DismissRunBannerCommand,
        state.DismissTooLargeBannerCommand);
}
