using Collector.Application.Auth;
using Collector.Application.Knowledge;
using Collector.Presentation.Navigation;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using Collector.Presentation.ViewModels;

namespace Collector.Tests.Presentation;

public sealed class KnowledgeRunViewModelTests
{
    private const string DocumentId = "doc-1";
    private const int CompletedUnits = 2;
    private const int TotalUnits = 5;

    private static KnowledgeRunOutcome Completed() => new(KnowledgeRunStatus.Completed, ReviewData.Run([ReviewData.Item("Idea")]));

    private static KnowledgeHarness Ready(SessionState session = SessionState.SignedIn)
    {
        var harness = new KnowledgeHarness(session);
        harness.ViewModel.SetDocuments([DocumentId]);
        return harness;
    }

    [Fact]
    public void CanExtract_NoDocuments_IsFalseWithHelpText()
    {
        var harness = new KnowledgeHarness();

        Assert.False(harness.ViewModel.CanExtract);
        Assert.Equal(ExtractionStrings.ExtractDisabledHelp, harness.ViewModel.ExtractHelp);
        Assert.False(harness.ViewModel.ExtractKnowledgeCommand.CanExecute(null));
    }

    [Fact]
    public void CanExtract_DocumentsSet_IsTrueWithoutHelpText()
    {
        var harness = Ready();

        Assert.True(harness.ViewModel.CanExtract);
        Assert.Null(harness.ViewModel.ExtractHelp);
        Assert.True(harness.ViewModel.ExtractKnowledgeCommand.CanExecute(null));
    }

    [Fact]
    public void CanExtract_WhileParsing_IsFalse()
    {
        var harness = Ready();

        harness.ViewModel.IsParsing = true;

        Assert.False(harness.ViewModel.CanExtract);
        Assert.False(harness.ViewModel.ExtractKnowledgeCommand.CanExecute(null));
    }

    [Fact]
    public async Task ExtractKnowledgeCommand_CompletedSignedInOnExtractScreen_SetsStateAndNavigatesToReview()
    {
        var harness = Ready();
        harness.Runner.Outcome = Completed();

        await harness.ViewModel.ExtractKnowledgeCommand.ExecuteAsync(null);

        Assert.Same(harness.Runner.Outcome.Result, harness.State.Current);
        Assert.Equal([ScreenKeys.Review], harness.Navigation.Visited);
        Assert.Equal([DocumentId], harness.Runner.LastDocumentIds);
    }

    [Fact]
    public async Task ExtractKnowledgeCommand_CompletedSignedInOnAnotherScreen_SetsStateWithoutNavigating()
    {
        var harness = Ready();
        harness.Runner.Outcome = Completed();
        harness.Navigation.CurrentScreen = FakeNavigationService.Screen(ScreenKeys.Settings);

        await harness.ViewModel.ExtractKnowledgeCommand.ExecuteAsync(null);

        Assert.Same(harness.Runner.Outcome.Result, harness.State.Current);
        Assert.Empty(harness.Navigation.Visited);
    }

    [Fact]
    public async Task ExtractKnowledgeCommand_CompletedSignedOut_ShowsSignInBannerWithoutNavigating()
    {
        var harness = Ready(SessionState.SignedOut);
        harness.Runner.Outcome = Completed();

        await harness.ViewModel.ExtractKnowledgeCommand.ExecuteAsync(null);

        var banner = harness.ViewModel.Banner!;
        Assert.Same(harness.Runner.Outcome.Result, harness.State.Current);
        Assert.Equal(BannerSeverity.Info, banner.Severity);
        Assert.Equal(ExtractionStrings.SignIn, banner.ActionText);
        Assert.Empty(harness.Navigation.Visited);
        Assert.Equal(KnowledgeFocusKeys.Banner, harness.ViewModel.PendingFocus);
    }

    [Fact]
    public async Task SignInBannerAction_Executed_NavigatesToSignIn()
    {
        var harness = Ready(SessionState.SignedOut);
        harness.Runner.Outcome = Completed();
        await harness.ViewModel.ExtractKnowledgeCommand.ExecuteAsync(null);

        harness.ViewModel.Banner!.ActionCommand!.Execute(null);

        Assert.Equal([ScreenKeys.SignIn], harness.Navigation.Visited);
    }

    [Fact]
    public async Task ExtractKnowledgeCommand_KeyMissing_ShowsWarningBannerWithOpenSettingsAction()
    {
        var harness = Ready();
        harness.Runner.Outcome = new KnowledgeRunOutcome(KnowledgeRunStatus.KeyMissing);

        await harness.ViewModel.ExtractKnowledgeCommand.ExecuteAsync(null);

        var banner = harness.ViewModel.Banner!;
        Assert.Equal(BannerSeverity.Warning, banner.Severity);
        Assert.Equal(ExtractionStrings.OpenSettings, banner.ActionText);
        Assert.Null(harness.State.Current);
        Assert.Equal(KnowledgeFocusKeys.Banner, harness.ViewModel.PendingFocus);
    }

    [Theory]
    [InlineData(Collector.Domain.Enums.CollectorProvider.Gemini, "Gemini")]
    [InlineData(Collector.Domain.Enums.CollectorProvider.Claude, "Claude")]
    public async Task ExtractKnowledgeCommand_KeyMissing_BannerNamesSelectedProvider(
        Collector.Domain.Enums.CollectorProvider provider,
        string name)
    {
        var harness = Ready();
        harness.Runner.Outcome = new KnowledgeRunOutcome(KnowledgeRunStatus.KeyMissing, Provider: provider);

        await harness.ViewModel.ExtractKnowledgeCommand.ExecuteAsync(null);

        Assert.Equal($"Add your {name} key in Settings.", harness.ViewModel.Banner!.Title);
        Assert.Contains(name, harness.ViewModel.Banner!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeyMissingBannerAction_Executed_NavigatesToSettings()
    {
        var harness = Ready();
        harness.Runner.Outcome = new KnowledgeRunOutcome(KnowledgeRunStatus.KeyMissing);
        await harness.ViewModel.ExtractKnowledgeCommand.ExecuteAsync(null);

        harness.ViewModel.Banner!.ActionCommand!.Execute(null);

        Assert.Equal([ScreenKeys.Settings], harness.Navigation.Visited);
    }

    [Fact]
    public async Task ExtractKnowledgeCommand_Failed_ShowsErrorBannerAndKeepsState()
    {
        var harness = Ready();
        harness.Runner.Outcome = new KnowledgeRunOutcome(KnowledgeRunStatus.Failed);

        await harness.ViewModel.ExtractKnowledgeCommand.ExecuteAsync(null);

        Assert.Equal(BannerSeverity.Error, harness.ViewModel.Banner!.Severity);
        Assert.Null(harness.State.Current);
        Assert.Empty(harness.Navigation.Visited);
    }

    [Fact]
    public async Task ExtractKnowledgeCommand_Canceled_ShowsInfoBannerAndKeepsPreviousState()
    {
        var harness = Ready();
        var previous = ReviewData.Run([ReviewData.Item("Previous")]);
        harness.State.Set(previous);
        harness.Runner.Gate = new TaskCompletionSource<KnowledgeRunOutcome>();
        var run = harness.ViewModel.ExtractKnowledgeCommand.ExecuteAsync(null);

        harness.ViewModel.ExtractKnowledgeCancelCommand.Execute(null);
        await run;

        Assert.Equal(BannerSeverity.Info, harness.ViewModel.Banner!.Severity);
        Assert.Same(previous, harness.State.Current);
        Assert.False(harness.ViewModel.IsRunning);
        Assert.Equal(KnowledgeFocusKeys.Banner, harness.ViewModel.PendingFocus);
    }

    [Fact]
    public async Task ExtractKnowledgeCommand_WhileRunning_TogglesIsRunningAndFocusesCancel()
    {
        var harness = Ready();
        harness.Runner.Gate = new TaskCompletionSource<KnowledgeRunOutcome>();

        var run = harness.ViewModel.ExtractKnowledgeCommand.ExecuteAsync(null);

        Assert.True(harness.ViewModel.IsRunning);
        Assert.False(harness.ViewModel.CanExtract);
        Assert.Equal(KnowledgeFocusKeys.Cancel, harness.ViewModel.PendingFocus);
        harness.Runner.Gate.SetResult(new KnowledgeRunOutcome(KnowledgeRunStatus.Failed));
        await run;
        Assert.False(harness.ViewModel.IsRunning);
    }

    [Fact]
    public async Task Progress_Reported_UpdatesUnitsAndText()
    {
        var harness = Ready();
        harness.Runner.Gate = new TaskCompletionSource<KnowledgeRunOutcome>();
        var run = harness.ViewModel.ExtractKnowledgeCommand.ExecuteAsync(null);

        harness.Runner.Progress!.Report(new ExtractionProgress(CompletedUnits, TotalUnits));

        Assert.Equal(CompletedUnits, harness.ViewModel.CompletedUnits);
        Assert.Equal(TotalUnits, harness.ViewModel.TotalUnits);
        Assert.Equal(ExtractionStrings.RunProgress(CompletedUnits, TotalUnits), harness.ViewModel.ProgressText);
        harness.Runner.Gate.SetResult(new KnowledgeRunOutcome(KnowledgeRunStatus.Failed));
        await run;
    }

    [Fact]
    public async Task DismissCommand_BannerShown_ClearsIt()
    {
        var harness = Ready();
        harness.Runner.Outcome = new KnowledgeRunOutcome(KnowledgeRunStatus.Failed);
        await harness.ViewModel.ExtractKnowledgeCommand.ExecuteAsync(null);

        harness.ViewModel.Banner!.DismissCommand!.Execute(null);

        Assert.Null(harness.ViewModel.Banner);
    }
}
