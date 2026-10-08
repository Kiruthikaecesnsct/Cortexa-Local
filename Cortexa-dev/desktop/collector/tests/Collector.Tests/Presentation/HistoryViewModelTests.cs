using Collector.Domain.History;
using Collector.Presentation.Navigation;
using Collector.Presentation.Resources;
using Collector.Presentation.ViewModels;
using Collector.Tests.Support;

namespace Collector.Tests.Presentation;

public sealed class HistoryViewModelTests : IDisposable
{
    private readonly HistoryHarness _harness = new();

    private HistoryViewModel ViewModel => _harness.ViewModel;

    public void Dispose() => _harness.Poller.Dispose();

    private static BatchSummary Done(string id, int minutesAgo = 0) =>
        HistoryData.Batch(id, "Completed", BatchStage.Seeded, minutesAgo);

    [Fact]
    public async Task Open_SelectsNewestBatch_ListedNewestFirst_AndLoadsItsCandidates()
    {
        _harness.Client.SetResults("new", HistoryData.Results("new", HistoryData.Candidate("c1")));

        await _harness.OpenAsync(Done("old", minutesAgo: 30), Done("new", minutesAgo: 1), Done("mid", minutesAgo: 10));

        Assert.Equal(["new", "mid", "old"], ViewModel.Batches.Select(batch => batch.BatchId));
        Assert.Equal("new", ViewModel.SelectedBatch!.BatchId);
        Assert.Equal(["new"], _harness.Client.ResultCalls);
        Assert.Equal("c1", Assert.Single(ViewModel.Candidates).CandidateId);
        Assert.True(ViewModel.ShowPanes);
        Assert.False(ViewModel.ShowLoading);
        Assert.True(ViewModel.ShowDetail);
    }

    [Fact]
    public void BeforeFirstLoad_ShowsLoadingOnly()
    {
        Assert.True(ViewModel.ShowLoading);
        Assert.False(ViewModel.ShowPanes);
        Assert.False(ViewModel.ShowEmpty);
    }

    [Fact]
    public async Task Open_NoBatches_ShowsEmptyAndGoToReviewNavigates()
    {
        await _harness.OpenAsync();

        Assert.True(ViewModel.ShowEmpty);
        Assert.False(ViewModel.ShowPanes);
        Assert.False(_harness.Poller.IsRunning);

        ViewModel.GoToReviewCommand.Execute(null);

        Assert.Equal([ScreenKeys.Review], _harness.Navigation.Visited);
    }

    [Fact]
    public async Task Open_FirstLoadFails_ShowsErrorBannerAndNoPanes_RetryRecoversAndFocusesList()
    {
        _harness.Client.ListError = HistoryData.ServerError();
        await _harness.OpenAsync();

        Assert.Equal(BannerSeverity.Error, ViewModel.Banner!.Severity);
        Assert.Equal(HistoryStrings.LoadErrorTitle, ViewModel.Banner.Title);
        Assert.Equal(HistoryStrings.Retry, ViewModel.Banner.ActionText);
        Assert.False(ViewModel.ShowPanes);
        Assert.False(ViewModel.ShowLoading);
        Assert.Equal(HistoryFocusKeys.Retry, ViewModel.PendingFocus);

        _harness.Client.ListError = null;
        _harness.Client.Batches = [Done("a")];
        ViewModel.RetryCommand.Execute(null);
        await HistoryHarness.Eventually(() => ViewModel.ShowPanes);
        await ViewModel.ResultsTask;

        Assert.Null(ViewModel.Banner);
        await HistoryHarness.Eventually(() => ViewModel.PendingFocus == HistoryFocusKeys.BatchList);
    }

    [Fact]
    public async Task Reload_UpdatesRowsInPlace_InsertsNewAtTop_RemovesMissing()
    {
        await _harness.OpenAsync(Done("a", minutesAgo: 5), Done("b", minutesAgo: 10));
        var rowA = ViewModel.Batches[0];

        _harness.Client.Batches =
        [
            Done("a", minutesAgo: 5) with { BatchName = "Renamed" },
            Done("n", minutesAgo: 0),
        ];
        await ViewModel.ReloadAsync(TestSupport.Ct);

        Assert.Equal(["n", "a"], ViewModel.Batches.Select(batch => batch.BatchId));
        Assert.Same(rowA, ViewModel.Batches[1]);
        Assert.Equal("Renamed", rowA.Name);
    }

    [Fact]
    public async Task Reload_ReusesCandidateInstances_AndAppendsHarvestingBeforeSeeding()
    {
        _harness.Client.SetResults("a", HistoryData.Results("a", HistoryData.Candidate("h1"), HistoryData.Candidate("s1", "seeding")));
        await _harness.OpenAsync(Done("a"));
        var first = ViewModel.Candidates[0];

        _harness.Client.SetResults("a", HistoryData.Results(
            "a",
            HistoryData.Candidate("s1", "seeding"),
            HistoryData.Candidate("h1", score: 99),
            HistoryData.Candidate("s2", "seeding"),
            HistoryData.Candidate("h2")));
        await ViewModel.ReloadAsync(TestSupport.Ct);

        Assert.Equal(["h1", "h2", "s1", "s2"], ViewModel.Candidates.Select(candidate => candidate.CandidateId));
        Assert.Same(first, ViewModel.Candidates[0]);
        Assert.Equal("99", first.Metrics[0].Text);
    }

    [Fact]
    public async Task Reload_CandidateRemovedByServer_IsRemoved()
    {
        _harness.Client.SetResults("a", HistoryData.Results("a", HistoryData.Candidate("h1"), HistoryData.Candidate("h2")));
        await _harness.OpenAsync(Done("a"));

        _harness.Client.SetResults("a", HistoryData.Results("a", HistoryData.Candidate("h2")));
        await ViewModel.ReloadAsync(TestSupport.Ct);

        Assert.Equal("h2", Assert.Single(ViewModel.Candidates).CandidateId);
    }

    [Fact]
    public async Task SelectedBatchVanishes_SelectsSameIndex_AndRequestsListFocus()
    {
        await _harness.OpenAsync(Done("a", 1), Done("b", 2), Done("c", 3));
        await _harness.SelectAsync("b");
        ViewModel.TakePendingFocus();

        _harness.Client.Batches = [Done("a", 1), Done("c", 3)];
        await ViewModel.ReloadAsync(TestSupport.Ct);

        Assert.Equal("c", ViewModel.SelectedBatch!.BatchId);
        Assert.Equal(HistoryFocusKeys.BatchList, ViewModel.PendingFocus);
    }

    [Fact]
    public async Task SelectedLastBatchVanishes_SelectsNewLast()
    {
        await _harness.OpenAsync(Done("a", 1), Done("b", 2));
        await _harness.SelectAsync("b");

        _harness.Client.Batches = [Done("a", 1)];
        await ViewModel.ReloadAsync(TestSupport.Ct);

        Assert.Equal("a", ViewModel.SelectedBatch!.BatchId);
    }

    [Fact]
    public async Task AllBatchesVanish_ShowsEmpty()
    {
        await _harness.OpenAsync(Done("a"));

        _harness.Client.Batches = [];
        await ViewModel.ReloadAsync(TestSupport.Ct);

        Assert.Null(ViewModel.SelectedBatch);
        Assert.True(ViewModel.ShowEmpty);
        Assert.Empty(ViewModel.Candidates);
    }

    [Fact]
    public async Task Selection_Debounces200ms_AndCancelsPreviousResultsLoad()
    {
        await _harness.OpenAsync(Done("a", 1), Done("b", 2), Done("c", 3));
        _harness.Client.ResultCalls.Clear();

        ViewModel.SelectedBatch = ViewModel.Batches[1];
        _harness.Time.Advance(TimeSpan.FromMilliseconds(150));
        ViewModel.SelectedBatch = ViewModel.Batches[2];
        _harness.Time.Advance(TimeSpan.FromMilliseconds(150));
        Assert.Empty(_harness.Client.ResultCalls);

        _harness.Time.Advance(TimeSpan.FromMilliseconds(50));
        await ViewModel.ResultsTask;

        Assert.Equal(["c"], _harness.Client.ResultCalls);
        Assert.True(ViewModel.ShowCandidateList || ViewModel.ShowNoCandidates);
    }

    [Fact]
    public async Task Selection_ShowsLoadingCandidatesUntilResultsArrive()
    {
        await _harness.OpenAsync(Done("a", 1), Done("b", 2));
        var gate = new TaskCompletionSource();
        _harness.Client.ResultsGate = gate;

        ViewModel.SelectedBatch = ViewModel.Batches[1];
        _harness.Time.Advance(TimeSpan.FromMilliseconds(200));
        await HistoryHarness.Eventually(() => _harness.Client.ResultCalls.Contains("b"));

        Assert.True(ViewModel.ShowResultsLoading);
        Assert.True(ViewModel.ShowDetail);

        gate.SetResult();
        await ViewModel.ResultsTask;

        Assert.False(ViewModel.ShowResultsLoading);
        Assert.True(ViewModel.ShowNoCandidates);
    }

    [Fact]
    public async Task ExpandedCandidates_SurviveSwitchingBatches()
    {
        _harness.Client.SetResults("a", HistoryData.Results("a", HistoryData.Candidate("h1"), HistoryData.Candidate("h2")));
        await _harness.OpenAsync(Done("a", 1), Done("b", 2));
        ViewModel.Candidates[1].IsExpanded = true;

        await _harness.SelectAsync("b");
        Assert.Empty(ViewModel.Candidates);
        await _harness.SelectAsync("a");

        Assert.False(ViewModel.Candidates[0].IsExpanded);
        Assert.True(ViewModel.Candidates[1].IsExpanded);
    }

    [Fact]
    public async Task ResolverCache_InvalidatedOnlyWhenSelectionChanges()
    {
        var documentId = await _harness.AddLocalDocumentAsync("a", "C:/work/paper.pdf");
        _harness.Client.SetResults("a", HistoryData.Results("a", HistoryData.Candidate("h1", links: [HistoryData.Link(documentId)])));
        await _harness.OpenAsync(Done("a", 1), Done("b", 2));
        await ViewModel.Candidates[0].Links[0].Resolution;
        var calls = _harness.Batches.FindByServerBatchIdCalls;

        _harness.Client.SetResults("a", HistoryData.Results("a", HistoryData.Candidate("h1", links: [HistoryData.Link(documentId), HistoryData.Link(documentId, "item-2")])));
        await ViewModel.ReloadAsync(TestSupport.Ct);
        await ViewModel.Candidates[0].Links[1].Resolution;
        Assert.Equal(calls, _harness.Batches.FindByServerBatchIdCalls);

        await _harness.SelectAsync("b");
        await _harness.SelectAsync("a");
        await ViewModel.Candidates[0].Links[0].Resolution;

        Assert.True(_harness.Batches.FindByServerBatchIdCalls > calls);
    }

    [Fact]
    public async Task Poll_ReloadsListAndResultsOfSelectedInProgressBatch()
    {
        await _harness.OpenAsync(HistoryData.Batch("a"), Done("b", 5));
        _harness.Client.ResultCalls.Clear();
        _harness.Client.Batches = [HistoryData.Batch("a") with { EmbeddingCompletedCount = 30 }, Done("b", 5)];

        await _harness.TickAsync();

        Assert.Equal(2, _harness.Client.ListCalls);
        Assert.Equal(["a"], _harness.Client.ResultCalls);
        Assert.Equal("Extracted · 30 of 40", ViewModel.Batches[0].StageLine);
    }

    [Fact]
    public async Task Poll_DoesNotReloadResultsOfSettledSelectedBatch()
    {
        await _harness.OpenAsync(HistoryData.Batch("a", minutesAgo: 5), Done("b", 1));
        Assert.Equal("b", ViewModel.SelectedBatch!.BatchId);
        _harness.Client.ResultCalls.Clear();

        await _harness.TickAsync();

        Assert.Empty(_harness.Client.ResultCalls);
        Assert.Equal(2, _harness.Client.ListCalls);
    }

    [Fact]
    public async Task Poll_StopsWhenNothingIsActive_AndCaptionDropsTheInterval()
    {
        await _harness.OpenAsync(HistoryData.Batch("a"));
        Assert.True(_harness.Poller.IsRunning);
        Assert.StartsWith("Updating every 5 seconds", ViewModel.PollingCaption);

        _harness.Client.Batches = [Done("a")];
        await _harness.TickAsync();

        Assert.False(_harness.Poller.IsRunning);
        Assert.StartsWith("Last updated", ViewModel.PollingCaption);
    }

    [Fact]
    public async Task Poll_BatchCompletes_ReloadsResultsOnceMoreAndAnnouncesIt()
    {
        await _harness.OpenAsync(HistoryData.Batch("a"));
        _harness.Client.ResultCalls.Clear();
        _harness.Client.SetResults("a", HistoryData.Results("a", HistoryData.Candidate("h1")));
        _harness.Client.Batches = [Done("a")];

        await _harness.TickAsync();

        Assert.Equal(["a"], _harness.Client.ResultCalls);
        Assert.Single(ViewModel.Candidates);
        Assert.Equal("Name a completed", ViewModel.StageAnnouncement);
    }

    [Fact]
    public async Task Poll_StageAdvances_AnnouncesNewStage_CountChangesDoNot()
    {
        await _harness.OpenAsync(HistoryData.Batch("a"));
        Assert.Equal(string.Empty, ViewModel.StageAnnouncement);

        _harness.Client.Batches = [HistoryData.Batch("a") with { ExtractionCompletedCount = 11 }];
        await _harness.TickAsync();
        Assert.Equal(string.Empty, ViewModel.StageAnnouncement);

        _harness.Client.Batches = [HistoryData.Batch("a", stage: BatchStage.Scored)];
        await _harness.TickAsync();
        Assert.Equal("Name a reached Scored", ViewModel.StageAnnouncement);
    }

    [Fact]
    public async Task Poll_BatchFails_AnnouncesFailureAfterStage()
    {
        await _harness.OpenAsync(HistoryData.Batch("a"));

        _harness.Client.Batches = [HistoryData.Batch("a", "Failed")];
        await _harness.TickAsync();

        Assert.Equal("Name a failed after Extracted", ViewModel.StageAnnouncement);
    }

    [Fact]
    public async Task Poll_AuthFailureOnList_StopsPollerWithoutBanner()
    {
        await _harness.OpenAsync(HistoryData.Batch("a"));
        Assert.True(_harness.Poller.IsRunning);

        _harness.Client.ListError = HistoryData.AuthError();
        await _harness.TickAsync();

        Assert.False(_harness.Poller.IsRunning);
        Assert.Null(ViewModel.Banner);
    }

    [Fact]
    public async Task Poll_AuthFailureOnResults_StopsPoller()
    {
        await _harness.OpenAsync(HistoryData.Batch("a"));
        _harness.Client.SetResultError("a", HistoryData.AuthError());

        await _harness.TickAsync();

        Assert.False(_harness.Poller.IsRunning);
        Assert.Null(ViewModel.Banner);
    }

    [Fact]
    public async Task Poll_ServerError_KeepsDataShowsStableWarningAndClearsOnSuccess()
    {
        _harness.Client.SetResults("a", HistoryData.Results("a", HistoryData.Candidate("h1")));
        await _harness.OpenAsync(HistoryData.Batch("a"));

        _harness.Client.ListError = HistoryData.ServerError();
        await _harness.TickAsync();
        var banner = ViewModel.Banner;
        await _harness.TickAsync();

        Assert.NotNull(banner);
        Assert.Equal(BannerSeverity.Warning, banner.Severity);
        Assert.Equal(HistoryStrings.RefreshErrorTitle, banner.Title);
        Assert.Contains("Trying again in 5 seconds", banner.Message);
        Assert.Same(banner, ViewModel.Banner);
        Assert.True(_harness.Poller.IsRunning);
        Assert.Single(ViewModel.Candidates);
        Assert.Single(ViewModel.Batches);

        _harness.Client.ListError = null;
        await _harness.TickAsync();

        Assert.Null(ViewModel.Banner);
    }

    [Fact]
    public async Task ResultsServerError_KeepsCandidatesAndShowsWarning()
    {
        _harness.Client.SetResults("a", HistoryData.Results("a", HistoryData.Candidate("h1")));
        await _harness.OpenAsync(HistoryData.Batch("a"));

        _harness.Client.SetResultError("a", HistoryData.ServerError());
        await _harness.TickAsync();

        Assert.Equal(BannerSeverity.Warning, ViewModel.Banner!.Severity);
        Assert.Single(ViewModel.Candidates);
        Assert.False(ViewModel.ShowNotFound);
    }

    [Fact]
    public async Task Results404_ShowsNotFound_StopsPollingThatBatch_ManualRefreshRetries()
    {
        _harness.Client.SetResults("a", null);
        await _harness.OpenAsync(HistoryData.Batch("a"));

        Assert.True(ViewModel.ShowNotFound);
        Assert.False(ViewModel.ShowDetail);
        Assert.Single(_harness.Client.ResultCalls);

        await _harness.TickAsync();
        Assert.Single(_harness.Client.ResultCalls);

        _harness.Client.SetResults("a", HistoryData.Results("a", HistoryData.Candidate("h1")));
        await ViewModel.ReloadAsync(TestSupport.Ct);

        Assert.False(ViewModel.ShowNotFound);
        Assert.Single(ViewModel.Candidates);
    }

    [Fact]
    public async Task Results404_LeavesBatchListRow()
    {
        _harness.Client.SetResults("a", null);

        await _harness.OpenAsync(Done("a"));

        Assert.Single(ViewModel.Batches);
        Assert.True(ViewModel.ShowPanes);
    }

    [Fact]
    public async Task SecondOpen_DoesNotShowSpinnerAgain()
    {
        await _harness.OpenAsync(Done("a"));
        ViewModel.OnNavigatedFrom();
        var gate = new TaskCompletionSource();
        _harness.Client.ResultsGate = gate;

        ViewModel.OnNavigatedTo();

        Assert.False(ViewModel.ShowLoading);
        Assert.True(ViewModel.ShowPanes);
        gate.SetResult();
        await ViewModel.LoadTask;
    }

    [Fact]
    public async Task NavigatedFrom_StopsPollerAndCancelsInFlightResults()
    {
        await _harness.OpenAsync(HistoryData.Batch("a"), Done("b", 5));
        var gate = new TaskCompletionSource();
        _harness.Client.ResultsGate = gate;
        ViewModel.SelectedBatch = ViewModel.Batches[1];
        _harness.Time.Advance(TimeSpan.FromMilliseconds(200));
        await HistoryHarness.Eventually(() => _harness.Client.ResultCalls.Contains("b"));

        ViewModel.OnNavigatedFrom();
        await ViewModel.ResultsTask;

        Assert.False(_harness.Poller.IsRunning);
        Assert.True(ViewModel.ShowResultsLoading);
    }

    [Fact]
    public async Task PollerTimer_DrivesRefreshOnTheConfiguredInterval()
    {
        await _harness.OpenAsync(HistoryData.Batch("a"));

        _harness.Time.Advance(TimeSpan.FromSeconds(HistoryHarness.PollSeconds));

        await HistoryHarness.Eventually(() => _harness.Client.ListCalls >= 2);
    }

    [Fact]
    public async Task BatchRow_ExposesDisplayFieldsAndAutomationName()
    {
        await _harness.OpenAsync(HistoryData.Batch("a", "InProgress", BatchStage.Scored));
        var row = ViewModel.Batches[0];

        Assert.Equal("Name a", row.Name);
        Assert.Equal(HistoryData.Start.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture), row.CreatedText);
        Assert.Equal("Scored · 96 of 140", row.StageLine);
        Assert.Equal(HistoryStrings.StateInProgress, row.StateText);
        Assert.Equal(BatchState.InProgress, row.StateKind);
        Assert.StartsWith("Name a, In progress, stage Scored, Scored · 96 of 140, uploaded ", row.AutomationName);
    }

    [Fact]
    public async Task BatchRow_UnknownState_ShowsRawTextNeutral()
    {
        await _harness.OpenAsync(HistoryData.Batch("a", "Archiving"));

        Assert.Equal("Archiving", ViewModel.Batches[0].StateText);
        Assert.Equal(BatchState.Unknown, ViewModel.Batches[0].StateKind);
        Assert.False(_harness.Poller.IsRunning);
    }

    [Fact]
    public async Task BatchRow_InProgress_ShowsDeterminateProgressForActiveStep()
    {
        await _harness.OpenAsync(HistoryData.Batch("a", "InProgress", BatchStage.Scored));
        var row = ViewModel.Batches[0];

        Assert.True(row.ShowProgress);
        Assert.Equal(140, row.ProgressMax);
        Assert.Equal(96, row.ProgressValue);
        Assert.Equal("Harvested progress", row.ProgressName);

        row.Update(Done("a"));
        Assert.False(row.ShowProgress);
    }

    [Theory]
    [InlineData("Queued", "No candidates yet")]
    [InlineData("InProgress", "No candidates yet")]
    [InlineData("Completed", "No candidates found")]
    [InlineData("Failed", "No candidates")]
    [InlineData("Cancelled", "No candidates")]
    public async Task NoCandidates_MessageFollowsBatchState(string state, string title)
    {
        await _harness.OpenAsync(HistoryData.Batch("a", state, BatchStage.Extracted));

        Assert.True(ViewModel.ShowNoCandidates);
        Assert.Equal(title, ViewModel.NoCandidatesTitle);
    }

    [Fact]
    public async Task NoCandidates_Stopped_NamesStageReached()
    {
        await _harness.OpenAsync(HistoryData.Batch("a", "Failed", BatchStage.Extracted));

        Assert.Equal("This batch stopped after the Extracted stage, so it produced no candidates.", ViewModel.NoCandidatesBody);
    }

    [Fact]
    public async Task CopyBatchId_CopiesThenShowsCopiedForTwoSeconds()
    {
        await _harness.OpenAsync(Done("a"));

        var copy = ViewModel.CopyBatchIdCommand.ExecuteAsync(null);
        Assert.Equal("a", _harness.Clipboard.Text);
        Assert.Equal(ReviewStrings.Copied, ViewModel.CopyName);

        _harness.Time.Advance(TimeSpan.FromSeconds(2));
        await copy;

        Assert.Equal(HistoryStrings.CopyBatchId, ViewModel.CopyName);
    }
}
