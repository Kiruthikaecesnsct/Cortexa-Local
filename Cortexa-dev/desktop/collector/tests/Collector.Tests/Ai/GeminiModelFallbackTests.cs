using Collector.Application.Ports;
using Collector.Infrastructure.Ai;
using Collector.Infrastructure.Options;
using Collector.Tests.Candidates;
using Collector.Tests.Support;

namespace Collector.Tests.Ai;

public sealed class GeminiModelFallbackTests
{
    private const string Primary = "gemini-primary";
    private const string SecondModel = "gemini-second";
    private const string ThirdModel = "gemini-third";
    private const int TokenLimit = 1000;
    private const int OverLimitCount = TokenLimit + 1;

    private static GeminiModelFallback Fallback(
        GeminiProviderOptions options,
        IGeminiModelExhaustion? exhaustion = null,
        IGeminiInputBudget? budget = null) =>
        new(
            Microsoft.Extensions.Options.Options.Create(options),
            exhaustion ?? new GeminiModelExhaustionTracker(),
            budget ?? new FakeInputBudget(0, TokenLimit));

    private static GeminiProviderOptions Options(params string[] fallbacks) =>
        new() { Model = Primary, FallbackModels = [.. fallbacks] };

    [Fact]
    public void Sequence_ConfiguredFallbacks_PrimaryFirstThenFallbacksInOrder()
    {
        var steps = Fallback(Options(SecondModel, ThirdModel)).Sequence(GeminiWire.Request);

        Assert.Equal(
            [new GeminiModelStep(Primary, false), new GeminiModelStep(SecondModel, true), new GeminiModelStep(ThirdModel, true)],
            steps);
    }

    [Fact]
    public void Sequence_RequestNamesModel_UsesRequestModelAsPrimary()
    {
        var steps = Fallback(Options(SecondModel)).Sequence(GeminiWire.Request with { Model = ThirdModel });

        Assert.Equal([new GeminiModelStep(ThirdModel, false), new GeminiModelStep(SecondModel, true)], steps);
    }

    [Fact]
    public void Sequence_PrimaryRepeatedInFallbacks_SkipsTheRepeat()
    {
        var steps = Fallback(Options(Primary, SecondModel, SecondModel)).Sequence(GeminiWire.Request);

        Assert.Equal([new GeminiModelStep(Primary, false), new GeminiModelStep(SecondModel, true)], steps);
    }

    [Fact]
    public void Sequence_NoFallbacks_ReturnsOnlyPrimary()
    {
        var steps = Fallback(Options()).Sequence(GeminiWire.Request);

        Assert.Equal([new GeminiModelStep(Primary, false)], steps);
    }

    [Fact]
    public void IsUsable_NoFallbacksAndTrackerMarked_ReturnsTrueWithoutReadingTracker()
    {
        var exhaustion = new RecordingExhaustion();
        exhaustion.MarkExhausted(Primary);

        var usable = Fallback(Options(), exhaustion).IsUsable(Primary);

        Assert.True(usable);
        Assert.Equal(0, exhaustion.Reads);
    }

    [Fact]
    public void MarkExhausted_NoFallbacks_DoesNothing()
    {
        var exhaustion = new RecordingExhaustion();

        Fallback(Options(), exhaustion).MarkExhausted(Primary);

        Assert.Equal(0, exhaustion.Marks);
    }

    [Fact]
    public void IsUsable_FallbacksConfiguredAndModelMarked_ReturnsFalse()
    {
        var tracker = new GeminiModelExhaustionTracker();
        var fallback = Fallback(Options(SecondModel), tracker);

        fallback.MarkExhausted(Primary);

        Assert.False(fallback.IsUsable(Primary));
        Assert.True(fallback.IsUsable(SecondModel));
    }

    [Fact]
    public async Task EnsureFitsAsync_CountOverLimit_ThrowsWithCountAndLimit()
    {
        var fallback = Fallback(Options(SecondModel), budget: new FakeInputBudget(OverLimitCount, TokenLimit));

        var exception = await Assert.ThrowsAsync<AiInputTooLargeException>(
            () => fallback.EnsureFitsAsync(GeminiWire.Request, SecondModel, TestSupport.Ct));

        Assert.Equal(OverLimitCount, exception.TokenCount);
        Assert.Equal(TokenLimit, exception.Limit);
        Assert.Equal(SecondModel, exception.Model);
    }

    [Fact]
    public async Task EnsureFitsAsync_CountAtLimit_Completes()
    {
        var fallback = Fallback(Options(SecondModel), budget: new FakeInputBudget(TokenLimit, TokenLimit));

        var exception = await Record.ExceptionAsync(() => fallback.EnsureFitsAsync(GeminiWire.Request, SecondModel, TestSupport.Ct));

        Assert.Null(exception);
    }

    private sealed class RecordingExhaustion : IGeminiModelExhaustion
    {
        private readonly HashSet<string> _models = [];

        public int Reads { get; private set; }

        public int Marks { get; private set; }

        public bool IsExhausted(string model)
        {
            Reads++;
            return _models.Contains(model);
        }

        public void MarkExhausted(string model)
        {
            Marks++;
            _models.Add(model);
        }

        public void Reset() => _models.Clear();
    }
}
