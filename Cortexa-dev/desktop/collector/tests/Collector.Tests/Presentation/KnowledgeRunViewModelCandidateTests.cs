using Collector.Application.Auth;
using Collector.Application.Candidates;
using Collector.Presentation.Navigation;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using Collector.Presentation.ViewModels;

namespace Collector.Tests.Presentation;

public sealed class KnowledgeRunViewModelCandidateTests
{
    private const string DocumentId = "doc-1";

    private static CandidateRunOutcome WithCandidate() =>
        CandidateData.Outcome(CandidateData.Result(CandidateData.Completed()));

    private static KnowledgeHarness Ready(SessionState session = SessionState.SignedIn)
    {
        var harness = new KnowledgeHarness(session);
        harness.ViewModel.SetDocuments([DocumentId]);
        return harness;
    }

    [Fact]
    public async Task ExtractKnowledgeCommand_Run_RunsOnlyTheCandidatePass()
    {
        var harness = Ready();

        await harness.ViewModel.ExtractKnowledgeCommand.ExecuteAsync(null);

        Assert.Equal(0, harness.Runner.Calls);
        Assert.Null(harness.State.Current);
        Assert.Equal(1, harness.CandidateRunner.Calls);
        Assert.Equal([DocumentId], harness.CandidateRunner.LastDocumentIds);
    }

    [Fact]
    public async Task ExtractKnowledgeCommand_CandidatesFound_NavigatesToReviewOnce()
    {
        var harness = Ready();
        harness.CandidateRunner.Outcome = WithCandidate();

        await harness.ViewModel.ExtractKnowledgeCommand.ExecuteAsync(null);

        Assert.Single(harness.CandidateState.Result!.AllCandidates);
        Assert.Null(harness.ViewModel.Banner);
        Assert.Equal([ScreenKeys.Review], harness.Navigation.Visited);
    }

    [Fact]
    public async Task ExtractKnowledgeCommand_CandidateBannerOnly_NavigatesToReview()
    {
        var harness = Ready();
        harness.CandidateRunner.Outcome = CandidateRunOutcome.Failed();

        await harness.ViewModel.ExtractKnowledgeCommand.ExecuteAsync(null);

        Assert.True(harness.CandidateState.Failed);
        Assert.Equal(CandidateStrings.FailedTitle, harness.CandidateState.RunBanner!.Title);
        Assert.Null(harness.ViewModel.Banner);
        Assert.Equal([ScreenKeys.Review], harness.Navigation.Visited);
    }

    [Fact]
    public async Task ExtractKnowledgeCommand_NoCandidatesAndNoBanner_StaysOnProcess()
    {
        var harness = Ready();
        harness.CandidateRunner.Outcome = CandidateData.Outcome(CandidateData.Result(CandidateData.Doc(CandidateOutcome.Completed)));

        await harness.ViewModel.ExtractKnowledgeCommand.ExecuteAsync(null);

        Assert.Empty(harness.Navigation.Visited);
        Assert.Null(harness.ViewModel.Banner);
        Assert.NotNull(harness.CandidateState.Result);
    }

    [Fact]
    public async Task ExtractKnowledgeCommand_OnAnotherScreen_DoesNotNavigate()
    {
        var harness = Ready();
        harness.CandidateRunner.Outcome = WithCandidate();
        harness.Navigation.CurrentScreen = FakeNavigationService.Screen(ScreenKeys.Settings);

        await harness.ViewModel.ExtractKnowledgeCommand.ExecuteAsync(null);

        Assert.Empty(harness.Navigation.Visited);
    }

    [Fact]
    public async Task ExtractKnowledgeCancelCommand_DuringCandidatePass_KeepsFinishedCandidatesAndNavigates()
    {
        var harness = Ready();
        harness.CandidateRunner.Gate = new TaskCompletionSource<CandidateRunOutcome>();
        harness.CandidateRunner.CanceledOutcome = CandidateData.Outcome(
            CandidateData.Result(CandidateData.Completed("a.txt"), CandidateData.Doc(CandidateOutcome.Canceled, "b.txt")),
            canceled: true);
        var run = harness.ViewModel.ExtractKnowledgeCommand.ExecuteAsync(null);

        harness.ViewModel.ExtractKnowledgeCancelCommand.Execute(null);
        await run;

        Assert.True(harness.CandidateState.Canceled);
        Assert.Single(harness.CandidateState.Result!.AllCandidates);
        Assert.Equal(CandidateStrings.CanceledTitle, harness.CandidateState.RunBanner!.Title);
        Assert.Equal([ScreenKeys.Review], harness.Navigation.Visited);
    }

    [Fact]
    public async Task ExtractKnowledgeCommand_WhileRunning_TogglesIsRunningAndFocusesCandidateCancel()
    {
        var harness = Ready();
        harness.CandidateRunner.Gate = new TaskCompletionSource<CandidateRunOutcome>();

        var run = harness.ViewModel.ExtractKnowledgeCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.IsRunning);
        Assert.False(harness.ViewModel.CanExtract);
        Assert.Equal(KnowledgeFocusKeys.Cancel, harness.ViewModel.PendingFocus);
        harness.ViewModel.ExtractKnowledgeCancelCommand.Execute(null);
        await run;
        Assert.False(harness.ViewModel.IsRunning);
    }

    [Fact]
    public async Task ExtractKnowledgeCommand_NewRunStarts_ClearsStaleKnowledgeAndCandidateState()
    {
        var harness = Ready();
        harness.State.Set(ReviewData.Run([ReviewData.Item("Stale")]));
        harness.CandidateState.Set(WithCandidate());
        harness.CandidateRunner.Gate = new TaskCompletionSource<CandidateRunOutcome>();

        var run = harness.ViewModel.ExtractKnowledgeCommand.ExecuteAsync(null);

        Assert.Null(harness.State.Current);
        Assert.Null(harness.CandidateState.Result);
        Assert.False(harness.CandidateState.HasOutput);
        harness.ViewModel.ExtractKnowledgeCancelCommand.Execute(null);
        await run;
    }

    [Fact]
    public async Task ExtractKnowledgeCommand_SignedOutWithCandidates_ShowsSignInBannerWithoutNavigating()
    {
        var harness = Ready(SessionState.SignedOut);
        harness.CandidateRunner.Outcome = WithCandidate();

        await harness.ViewModel.ExtractKnowledgeCommand.ExecuteAsync(null);

        Assert.Equal(ExtractionStrings.SignIn, harness.ViewModel.Banner!.ActionText);
        Assert.Equal(KnowledgeFocusKeys.Banner, harness.ViewModel.PendingFocus);
        Assert.Empty(harness.Navigation.Visited);
    }

    [Fact]
    public async Task SignInBannerAction_Executed_NavigatesToSignIn()
    {
        var harness = Ready(SessionState.SignedOut);
        harness.CandidateRunner.Outcome = WithCandidate();
        await harness.ViewModel.ExtractKnowledgeCommand.ExecuteAsync(null);

        harness.ViewModel.Banner!.ActionCommand!.Execute(null);

        Assert.Equal([ScreenKeys.SignIn], harness.Navigation.Visited);
    }

    [Fact]
    public async Task DismissCommand_SignInBannerShown_ClearsIt()
    {
        var harness = Ready(SessionState.SignedOut);
        harness.CandidateRunner.Outcome = WithCandidate();
        await harness.ViewModel.ExtractKnowledgeCommand.ExecuteAsync(null);

        harness.ViewModel.Banner!.DismissCommand!.Execute(null);

        Assert.Null(harness.ViewModel.Banner);
    }

    [Fact]
    public async Task ExtractKnowledgeCommand_SignedOutCandidateBannerOnly_ShowsNoSignInBanner()
    {
        var harness = Ready(SessionState.SignedOut);
        harness.CandidateRunner.Outcome = CandidateRunOutcome.Failed();

        await harness.ViewModel.ExtractKnowledgeCommand.ExecuteAsync(null);

        Assert.Null(harness.ViewModel.Banner);
        Assert.Empty(harness.Navigation.Visited);
    }

    [Fact]
    public void CancelCommand_CandidateProgress_IsWiredToKnowledgeCancel()
    {
        var harness = Ready();

        Assert.Same(harness.ViewModel.ExtractKnowledgeCancelCommand, harness.ViewModel.CandidateProgress.CancelCommand);
    }
}
