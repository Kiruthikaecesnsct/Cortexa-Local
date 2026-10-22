using Collector.Infrastructure.Ai;

namespace Collector.Tests.Ai;

public sealed class GeminiModelExhaustionTrackerTests
{
    private const string FirstModel = "gemini-first";
    private const string SecondModel = "gemini-second";
    private const int ParallelMarks = 200;

    [Fact]
    public async Task MarkExhausted_ParallelMarksOfTwoModels_BothModelsExhaustedWithoutError()
    {
        var tracker = new GeminiModelExhaustionTracker();

        await Task.WhenAll(Enumerable.Range(0, ParallelMarks)
            .Select(index => Task.Run(() => tracker.MarkExhausted(index % 2 == 0 ? FirstModel : SecondModel), TestContext.Current.CancellationToken)));

        Assert.True(tracker.IsExhausted(FirstModel));
        Assert.True(tracker.IsExhausted(SecondModel));
    }

    [Fact]
    public void IsExhausted_ModelNeverMarked_ReturnsFalse()
    {
        var tracker = new GeminiModelExhaustionTracker();
        tracker.MarkExhausted(FirstModel);

        Assert.False(tracker.IsExhausted(SecondModel));
    }

    [Fact]
    public void Reset_AfterMarks_ClearsEveryModel()
    {
        var tracker = new GeminiModelExhaustionTracker();
        tracker.MarkExhausted(FirstModel);
        tracker.MarkExhausted(SecondModel);

        tracker.Reset();

        Assert.False(tracker.IsExhausted(FirstModel));
        Assert.False(tracker.IsExhausted(SecondModel));
    }
}
