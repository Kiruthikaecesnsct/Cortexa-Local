using Collector.Presentation.Resources;
using Collector.Presentation.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace Collector.Tests.Presentation;

public sealed class IntakeProgressViewModelTests
{
    private const int SplitDone = 3;
    private const int SplitTotal = 10;
    private const int FetchDone = 4;
    private const int FetchTotal = 8;
    private const string CurrentPath = "docs/guide.md";

    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-07T00:00:00Z"));
    private readonly IntakeProgressViewModel _intake;

    public IntakeProgressViewModelTests()
    {
        _intake = new IntakeProgressViewModel(_time);
    }

    [Fact]
    public void Begin_WhenInactive_ActivatesAndReturnsALiveToken()
    {
        var token = _intake.Begin();

        Assert.True(_intake.IsActive);
        Assert.True(token.CanBeCanceled);
        Assert.False(token.IsCancellationRequested);
        Assert.Equal(token, _intake.Token);
    }

    [Fact]
    public void Begin_WhileActive_ReturnsTheSameToken()
    {
        var first = _intake.Begin();

        var second = _intake.Begin();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Token_WhenInactive_IsNone()
    {
        Assert.Equal(CancellationToken.None, _intake.Token);
    }

    [Fact]
    public void CancelCommand_BeforeBegin_CannotExecute()
    {
        Assert.False(_intake.CancelCommand.CanExecute(null));
    }

    [Fact]
    public void CancelCommand_WhileActive_CancelsTheSharedToken()
    {
        var token = _intake.Begin();

        Assert.True(_intake.CancelCommand.CanExecute(null));
        _intake.CancelCommand.Execute(null);

        Assert.True(token.IsCancellationRequested);
        Assert.True(_intake.Begin().IsCancellationRequested);
    }

    [Fact]
    public void End_AfterCancel_ResetsStateAndNextBeginGetsAFreshToken()
    {
        var canceled = _intake.Begin();
        _intake.CancelCommand.Execute(null);

        _intake.End();
        var fresh = _intake.Begin();

        Assert.True(canceled.IsCancellationRequested);
        Assert.False(fresh.IsCancellationRequested);
    }

    [Fact]
    public void End_WhileActive_ClearsProgressAndDisablesCancel()
    {
        _intake.Begin();
        _intake.ReportSplit(SplitDone, SplitTotal, CurrentPath);
        _time.Advance(IntakeTiming.FlushInterval);

        _intake.End();

        Assert.False(_intake.IsActive);
        Assert.Equal(CancellationToken.None, _intake.Token);
        Assert.Equal(string.Empty, _intake.SplitText);
        Assert.Equal(string.Empty, _intake.CurrentFile);
        Assert.True(_intake.IsIndeterminate);
        Assert.False(_intake.CancelCommand.CanExecute(null));
    }

    [Fact]
    public void ReportSplit_BeforeTheTick_DoesNotRender()
    {
        _intake.Begin();

        _intake.ReportSplit(SplitDone, SplitTotal, CurrentPath);

        Assert.Equal(string.Empty, _intake.SplitText);
    }

    [Fact]
    public void ReportSplit_AfterTheTick_RendersTextAndDeterminateProgress()
    {
        _intake.Begin();

        _intake.ReportSplit(SplitDone, SplitTotal, CurrentPath);
        _time.Advance(IntakeTiming.FlushInterval);

        Assert.Equal(ExtractionStrings.IntakeSplit(SplitDone, SplitTotal), _intake.SplitText);
        Assert.Equal(ExtractionStrings.IntakeCurrent(CurrentPath), _intake.CurrentFile);
        Assert.Equal(SplitDone, _intake.ProgressValue);
        Assert.Equal(SplitTotal, _intake.ProgressMaximum);
        Assert.False(_intake.IsIndeterminate);
        Assert.True(_intake.HasSplitText);
        Assert.True(_intake.HasCurrentFile);
    }

    [Fact]
    public void Report_ManyUpdatesInOneInterval_RendersOnlyTheLatest()
    {
        _intake.Begin();

        _intake.ReportSplit(1, SplitTotal, "first.md");
        _intake.ReportSplit(SplitDone, SplitTotal, CurrentPath);
        _time.Advance(IntakeTiming.FlushInterval);

        Assert.Equal(ExtractionStrings.IntakeSplit(SplitDone, SplitTotal), _intake.SplitText);
        Assert.Equal(ExtractionStrings.IntakeCurrent(CurrentPath), _intake.CurrentFile);
    }

    [Fact]
    public void ReportFetch_AfterTheTick_RendersFetchedText()
    {
        _intake.Begin();

        _intake.ReportFetch(FetchDone, FetchTotal, CurrentPath, isPaused: false);
        _time.Advance(IntakeTiming.FlushInterval);

        Assert.Equal(ExtractionStrings.IntakeFetched(FetchDone, FetchTotal), _intake.FetchedText);
        Assert.Equal(FetchDone, _intake.ProgressValue);
        Assert.Equal(FetchTotal, _intake.ProgressMaximum);
    }

    [Fact]
    public void ReportFetch_Paused_RendersThePausedText()
    {
        _intake.Begin();

        _intake.ReportFetch(FetchDone, FetchTotal, null, isPaused: true);
        _time.Advance(IntakeTiming.FlushInterval);

        Assert.Equal(ExtractionStrings.IntakePaused(FetchDone, FetchTotal), _intake.FetchedText);
    }

    [Fact]
    public void ReportFetch_NoTotalYet_ShowsReadingTreeAndStaysIndeterminate()
    {
        _intake.Begin();

        _intake.ReportFetch(0, 0, null, isPaused: false);
        _time.Advance(IntakeTiming.FlushInterval);

        Assert.Equal(ExtractionStrings.IntakeReadingTree, _intake.FetchedText);
        Assert.True(_intake.IsIndeterminate);
    }

    [Fact]
    public void ReportSplit_AfterFetch_SwitchesProgressToTheSplitPhase()
    {
        _intake.Begin();
        _intake.ReportFetch(FetchTotal, FetchTotal, null, isPaused: false);
        _time.Advance(IntakeTiming.FlushInterval);

        _intake.ReportSplit(SplitDone, SplitTotal, null);
        _time.Advance(IntakeTiming.FlushInterval);

        Assert.Equal(SplitDone, _intake.ProgressValue);
        Assert.Equal(SplitTotal, _intake.ProgressMaximum);
        Assert.Equal(ExtractionStrings.IntakeFetched(FetchTotal, FetchTotal), _intake.FetchedText);
    }

    [Fact]
    public void Tick_AfterEnd_DoesNotRenderStaleProgress()
    {
        _intake.Begin();
        _intake.ReportSplit(SplitDone, SplitTotal, CurrentPath);
        _intake.End();

        _time.Advance(IntakeTiming.FlushInterval);

        Assert.Equal(string.Empty, _intake.SplitText);
    }

    [Fact]
    public void Dispose_WhileActive_EndsTheRun()
    {
        _intake.Begin();

        _intake.Dispose();

        Assert.False(_intake.IsActive);
    }
}
