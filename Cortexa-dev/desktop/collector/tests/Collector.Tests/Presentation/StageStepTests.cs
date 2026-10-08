using Collector.Domain.History;
using Collector.Presentation.Resources;
using Collector.Presentation.ViewModels;

namespace Collector.Tests.Presentation;

public sealed class StageStepTests
{
    private static BatchSummary Batch(
        string state,
        BatchStage stage,
        int harvestingTotal = 140,
        int seedingTotal = 0) => HistoryData.Batch("b1", state, stage) with
        {
            HarvestingTotalCount = harvestingTotal,
            SeedingTotalCount = seedingTotal,
        };

    private static StepStatus[] Statuses(BatchSummary batch) =>
        [.. Enumerable.Range(0, BatchProgress.StepTotal).Select(index => BatchProgress.StatusOf(batch, index))];

    [Fact]
    public void Queued_FirstStepIsCurrentAndWaiting()
    {
        var batch = Batch("Queued", BatchStage.Ingested);

        Assert.Equal(StepStatus.Current, BatchProgress.StatusOf(batch, 0));
        Assert.Equal(HistoryStrings.StepWaiting, BatchProgress.CountText(batch, 0, StepStatus.Current));
        Assert.Equal(StepStatus.Pending, BatchProgress.StatusOf(batch, 1));
        Assert.Equal(StepStatus.Pending, BatchProgress.StatusOf(batch, 2));
    }

    [Fact]
    public void InProgress_StepAfterStageIsCurrent_EarlierAreDone()
    {
        var batch = Batch("InProgress", BatchStage.Scored);

        Assert.Equal(
            [StepStatus.Done, StepStatus.Done, StepStatus.Done, StepStatus.Current, StepStatus.NotUsed],
            Statuses(batch));
    }

    [Fact]
    public void InProgress_OptionalStepWithTotal_IsPendingUntilReached()
    {
        var batch = Batch("InProgress", BatchStage.Extracted, seedingTotal: 8);

        Assert.Equal(
            [StepStatus.Done, StepStatus.Done, StepStatus.Current, StepStatus.Pending, StepStatus.Pending],
            Statuses(batch));
    }

    [Fact]
    public void Completed_StepWithZeroTotal_IsNotUsedEvenWhenStageIsPast()
    {
        var batch = Batch("Completed", BatchStage.Seeded, harvestingTotal: 0, seedingTotal: 5);

        Assert.Equal(
            [StepStatus.Done, StepStatus.Done, StepStatus.Done, StepStatus.NotUsed, StepStatus.Done],
            Statuses(batch));
        Assert.Equal(HistoryStrings.StepNotUsed, BatchProgress.CountText(batch, 3, StepStatus.NotUsed));
    }

    [Fact]
    public void Completed_AllStepsWithTotals_AreDone()
    {
        var batch = Batch("Completed", BatchStage.Seeded, seedingTotal: 5);

        Assert.All(Statuses(batch), status => Assert.Equal(StepStatus.Done, status));
    }

    [Fact]
    public void Failed_StepAfterStageIsFailedAndStopped()
    {
        var batch = Batch("Failed", BatchStage.Extracted, seedingTotal: 3);

        Assert.Equal(StepStatus.Failed, BatchProgress.StatusOf(batch, 2));
        Assert.Equal(HistoryStrings.StepStopped, BatchProgress.CountText(batch, 2, StepStatus.Failed));
        Assert.Equal(StepStatus.Pending, BatchProgress.StatusOf(batch, 3));
        Assert.Equal(HistoryStrings.StepNotStarted, BatchProgress.CountText(batch, 3, StepStatus.Pending));
    }

    [Fact]
    public void Cancelled_StepAfterStageIsCancelled()
    {
        var batch = Batch("Cancelled", BatchStage.Ingested);

        Assert.Equal(StepStatus.Cancelled, BatchProgress.StatusOf(batch, 1));
        Assert.Equal(HistoryStrings.StepCancelled, BatchProgress.CountText(batch, 1, StepStatus.Cancelled));
    }

    [Fact]
    public void UnknownState_DoneUpToStage_PendingAfter()
    {
        var batch = Batch("Archived", BatchStage.Extracted, seedingTotal: 3);

        Assert.Equal(
            [StepStatus.Done, StepStatus.Done, StepStatus.Pending, StepStatus.Pending, StepStatus.Pending],
            Statuses(batch));
    }

    [Fact]
    public void CountText_ShowsReceivedCountsAndEvidence()
    {
        var batch = Batch("Completed", BatchStage.Seeded, seedingTotal: 5);

        Assert.Equal(HistoryStrings.StepReceived, BatchProgress.CountText(batch, 0, StepStatus.Done));
        Assert.Equal("10 of 10", BatchProgress.CountText(batch, 1, StepStatus.Done));
        Assert.Equal("40 of 40 · 12 evidence", BatchProgress.CountText(batch, 2, StepStatus.Done));
        Assert.Equal("96 of 140", BatchProgress.CountText(batch, 3, StepStatus.Done));
    }

    [Fact]
    public void StepViewModel_Update_ChangesInPlaceAndNamesStatus()
    {
        var step = new StageStepViewModel(BatchStage.Extracted);

        step.Update(Batch("InProgress", BatchStage.Ingested));

        Assert.Equal(StepStatus.Current, step.Status);
        Assert.Equal("Extracted, in progress, 10 of 10", step.AutomationName);

        step.Update(Batch("Completed", BatchStage.Seeded, seedingTotal: 1));

        Assert.Equal(StepStatus.Done, step.Status);
        Assert.Equal("Extracted, complete, 10 of 10", step.AutomationName);
    }

    [Fact]
    public void StageLine_InProgress_ShowsStageWithNextStepCounts()
    {
        Assert.Equal("Scored · 96 of 140", BatchProgress.StageLine(Batch("InProgress", BatchStage.Scored)));
    }

    [Fact]
    public void StageLine_InProgressWithoutNextTotal_ShowsStageOnly()
    {
        var batch = Batch("InProgress", BatchStage.Scored, harvestingTotal: 0);

        Assert.Equal("Scored", BatchProgress.StageLine(batch));
    }

    [Fact]
    public void StageLine_Failed_NamesStageReached()
    {
        Assert.Equal("Failed after Extracted", BatchProgress.StageLine(Batch("Failed", BatchStage.Extracted)));
    }

    [Fact]
    public void StageLine_Completed_IsStageName()
    {
        Assert.Equal("Seeded", BatchProgress.StageLine(Batch("Completed", BatchStage.Seeded)));
    }

    [Theory]
    [InlineData("Queued", BatchState.Queued)]
    [InlineData("InProgress", BatchState.InProgress)]
    [InlineData("in_progress", BatchState.InProgress)]
    [InlineData("IN-PROGRESS", BatchState.InProgress)]
    [InlineData("Completed", BatchState.Completed)]
    [InlineData("failed", BatchState.Failed)]
    [InlineData("Canceled", BatchState.Cancelled)]
    [InlineData("Cancelled", BatchState.Cancelled)]
    [InlineData("paused", BatchState.Unknown)]
    [InlineData("", BatchState.Unknown)]
    public void ParseState_NormalizesServerText(string raw, BatchState expected)
    {
        Assert.Equal(expected, BatchProgress.ParseState(raw));
    }

    [Fact]
    public void StateText_UnknownState_KeepsRawText()
    {
        Assert.Equal("paused", BatchProgress.StateText(BatchState.Unknown, "paused"));
    }
}
