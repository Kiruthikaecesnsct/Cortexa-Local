using System.Text;
using Collector.Application.Candidates;
using Collector.Presentation.Resources;
using Collector.Presentation.ViewModels;

namespace Collector.Tests.Presentation;

public sealed class ReviewCandidatesTests
{
    private const string KnowledgeTitle = "Kept idea";
    private const string SecretCandidateTitle = "Secret candidate title";

    private readonly ReviewHarness _harness = new();

    private void ShowCandidates(params CandidateDocumentResult[] documents) =>
        _harness.CandidateState.Set(CandidateData.Outcome(CandidateData.Result(documents)));

    [Fact]
    public void OnNavigatedTo_NothingToShow_ShowsNoRunPromptAndFocusesGoToExtract()
    {
        _harness.ViewModel.OnNavigatedTo();

        Assert.True(_harness.ViewModel.ShowEmptyCard);
        Assert.False(_harness.ViewModel.Candidates.IsVisible);
        Assert.Equal(ReviewStrings.NoRunTitle, _harness.ViewModel.EmptyTitle);
        Assert.Equal(ReviewFocusKeys.GoToExtract, _harness.ViewModel.PendingFocus);
    }

    [Fact]
    public void OnNavigatedTo_CandidatesFound_ShowsOnlyCandidatesPanelAndFocusesList()
    {
        ShowCandidates(CandidateData.Completed());

        _harness.ViewModel.OnNavigatedTo();

        Assert.False(_harness.ViewModel.ShowEmptyCard);
        Assert.False(_harness.ViewModel.ShowReady);
        Assert.True(_harness.ViewModel.Candidates.IsVisible);
        Assert.True(_harness.ViewModel.Candidates.HasItems);
        Assert.Equal(ReviewFocusKeys.CandidateList, _harness.ViewModel.PendingFocus);
    }

    [Fact]
    public void OnNavigatedTo_RunFinishedWithZeroCandidates_ShowsPanelEmptyState()
    {
        ShowCandidates(CandidateData.Doc(CandidateOutcome.Completed));

        _harness.ViewModel.OnNavigatedTo();

        Assert.True(_harness.ViewModel.Candidates.ShowEmpty);
        Assert.True(_harness.ViewModel.Candidates.IsVisible);
        Assert.False(_harness.ViewModel.ShowEmptyCard);
    }

    [Fact]
    public void OnNavigatedTo_CandidateBannerOnly_ShowsPanelAndFocusesGoToExtract()
    {
        ShowCandidates(CandidateData.Doc(CandidateOutcome.KeyMissing));
        _harness.CandidateState.SetBanners(
            new BannerViewModel(new BannerContent { Severity = BannerSeverity.Warning, Title = "t", Message = "m" }),
            null);

        _harness.ViewModel.OnNavigatedTo();

        Assert.True(_harness.ViewModel.Candidates.IsVisible);
        Assert.False(_harness.ViewModel.ShowEmptyCard);
        Assert.Equal(ReviewFocusKeys.GoToExtract, _harness.ViewModel.PendingFocus);
    }

    [Fact(Skip = "Knowledge extraction is disabled by product decision; no knowledge run reaches the Review screen.")]
    public void OnNavigatedTo_KnowledgeItemsAndCandidates_FocusesFilters()
    {
        ShowCandidates(CandidateData.Completed());

        _harness.Open(ReviewData.Item(KnowledgeTitle));

        Assert.True(_harness.ViewModel.ShowReady);
        Assert.False(_harness.ViewModel.ShowEmptyCard);
        Assert.Equal(ReviewFocusKeys.Filters, _harness.ViewModel.PendingFocus);
    }

    [Fact]
    public void CandidateStateChanged_AfterNavigation_RaisesLayoutNotifications()
    {
        _harness.ViewModel.OnNavigatedTo();
        var raised = new List<string?>();
        _harness.ViewModel.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        ShowCandidates(CandidateData.Completed());

        Assert.Contains(nameof(ReviewViewModel.ShowEmptyCard), raised);
        Assert.False(_harness.ViewModel.ShowEmptyCard);
    }

    [Fact]
    public void Candidates_ItemsFromState_ExposeItemsAndCount()
    {
        ShowCandidates(CandidateData.Completed("a.txt", "One"), CandidateData.Completed("b.txt", "Two"));

        var panel = _harness.ViewModel.Candidates;

        Assert.Equal(2, panel.Items.Count);
        Assert.Equal(CandidateStrings.CountText(2), panel.CountText);
        Assert.False(panel.ShowEmpty);
    }
}
