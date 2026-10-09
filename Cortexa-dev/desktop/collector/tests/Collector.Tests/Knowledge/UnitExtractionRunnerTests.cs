using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Collector.Tests.Support;

namespace Collector.Tests.Knowledge;

public sealed class UnitExtractionRunnerTests
{
    private const int ExpectedCallsWithDepthCap = 7;
    private const string EightLines = "l1 text\nl2 text\nl3 text\nl4 text\nl5 text\nl6 text\nl7 text\nl8 text";

    private readonly CapturingLogger<UnitExtractionRunner> _logger = new();

    private UnitExtractionRunner Runner(IAiProvider provider) => KnowledgePipeline.Runner(provider, _logger);

    private static string Valid(string title) => TestData.Response(TestData.ItemJson(title));

    [Fact]
    public async Task ExtractAsync_Completed_ReturnsItemsWithModelAndVerdicts()
    {
        var provider = new ScriptedAiProvider().Then(Completions.Completed(Valid("Backoff idea")));
        var unit = TestData.FileUnit("line one\nline two");

        var outcome = await Runner(provider).ExtractAsync(unit, TestData.Document(), KnowledgePipeline.DefaultContext, TestSupport.Ct);

        Assert.Equal(UnitOutcomeStatus.Completed, outcome.Status);
        Assert.Equal(Completions.Model, outcome.Model);
        var item = Assert.Single(outcome.Items);
        Assert.Equal("Backoff idea", item.Title);
        Assert.False(item.EchoVerdict.IsEcho);
    }

    [Fact]
    public async Task ExtractAsync_TitleEchoesIdentifier_ItemCarriesEchoVerdict()
    {
        var provider = new ScriptedAiProvider().Then(Completions.Completed(Valid("ExecuteAsync")));
        var unit = TestData.FileUnit("public Task ExecuteAsync() { }");

        var outcome = await Runner(provider).ExtractAsync(unit, TestData.Document(), KnowledgePipeline.DefaultContext, TestSupport.Ct);

        Assert.Equal(EchoReasons.TitleIsIdentifier, Assert.Single(outcome.Items).EchoVerdict.Reason);
    }

    [Fact]
    public async Task ExtractAsync_Truncated_SplitsAndCombinesBothHalves()
    {
        var provider = new ScriptedAiProvider()
            .Then(Completions.Truncated())
            .Then(Completions.Completed(Valid("First half idea")))
            .Then(Completions.Completed(Valid("Second half idea")));
        var unit = TestData.FileUnit("line one\nline two\nline three\nline four");

        var outcome = await Runner(provider).ExtractAsync(unit, TestData.Document(), KnowledgePipeline.DefaultContext, TestSupport.Ct);

        Assert.Equal(UnitOutcomeStatus.Completed, outcome.Status);
        Assert.Equal(["First half idea", "Second half idea"], outcome.Items.Select(item => item.Title));
        Assert.Equal(3, provider.Requests.Count);
    }

    [Fact]
    public async Task ExtractAsync_AlwaysTruncated_StopsSplittingAtDepthCapAndFails()
    {
        var provider = new FuncAiProvider((_, _) => Task.FromResult(Completions.Truncated()));

        var outcome = await Runner(provider).ExtractAsync(TestData.FileUnit(EightLines), TestData.Document(), KnowledgePipeline.DefaultContext, TestSupport.Ct);

        Assert.Equal(UnitOutcomeStatus.Failed, outcome.Status);
        Assert.Equal(ExpectedCallsWithDepthCap, provider.Calls);
    }

    [Fact]
    public async Task ExtractAsync_TruncatedUnsplittableUnit_FailsAfterOneCall()
    {
        var provider = new ScriptedAiProvider().Then(Completions.Truncated());

        var outcome = await Runner(provider).ExtractAsync(TestData.FileUnit("single line"), TestData.Document(), KnowledgePipeline.DefaultContext, TestSupport.Ct);

        Assert.Equal(UnitOutcomeStatus.Failed, outcome.Status);
        Assert.Single(provider.Requests);
    }

    [Fact]
    public async Task ExtractAsync_Refused_IsSkippedWithModel()
    {
        var provider = new ScriptedAiProvider().Then(Completions.Refused());

        var outcome = await Runner(provider).ExtractAsync(TestData.FileUnit("code"), TestData.Document(), KnowledgePipeline.DefaultContext, TestSupport.Ct);

        Assert.Equal(UnitOutcomeStatus.Skipped, outcome.Status);
        Assert.Equal(Completions.Model, outcome.Model);
    }

    [Theory]
    [InlineData(AiFailureKind.Permanent)]
    [InlineData(AiFailureKind.Transient)]
    public async Task ExtractAsync_ProviderError_FailsWithoutRetry(AiFailureKind kind)
    {
        var provider = new ScriptedAiProvider().Throw(new AiProviderException(kind, "provider said no"));

        var outcome = await Runner(provider).ExtractAsync(TestData.FileUnit("code"), TestData.Document(), KnowledgePipeline.DefaultContext, TestSupport.Ct);

        Assert.Equal(UnitOutcomeStatus.Failed, outcome.Status);
        Assert.Equal(kind, outcome.FailureKind);
        Assert.Single(provider.Requests);
    }

    [Fact]
    public async Task ExtractAsync_TruncatedHalvesFailWithDifferentKinds_CombinedKeepsPermanentOverQuotaExceeded()
    {
        var provider = new ScriptedAiProvider()
            .Then(Completions.Truncated())
            .Throw(new AiProviderException(AiFailureKind.QuotaExceeded, "quota"))
            .Throw(new AiProviderException(AiFailureKind.Permanent, "key rejected"));
        var unit = TestData.FileUnit("line one\nline two\nline three\nline four");

        var outcome = await Runner(provider).ExtractAsync(unit, TestData.Document(), KnowledgePipeline.DefaultContext, TestSupport.Ct);

        Assert.Equal(UnitOutcomeStatus.Failed, outcome.Status);
        Assert.Equal(AiFailureKind.Permanent, outcome.FailureKind);
    }

    [Fact]
    public async Task ExtractAsync_MissingApiKey_PropagatesException()
    {
        var provider = new ScriptedAiProvider().Throw(new AiProviderException(AiFailureKind.MissingApiKey, "no key"));

        var exception = await Assert.ThrowsAsync<AiProviderException>(
            () => Runner(provider).ExtractAsync(TestData.FileUnit("code"), TestData.Document(), KnowledgePipeline.DefaultContext, TestSupport.Ct));

        Assert.Equal(AiFailureKind.MissingApiKey, exception.Kind);
    }

    [Fact]
    public async Task ExtractAsync_UnparseableOutput_Fails()
    {
        var provider = new ScriptedAiProvider().Then(Completions.Completed("this is not json"));

        var outcome = await Runner(provider).ExtractAsync(TestData.FileUnit("code"), TestData.Document(), KnowledgePipeline.DefaultContext, TestSupport.Ct);

        Assert.Equal(UnitOutcomeStatus.Failed, outcome.Status);
    }

    [Fact]
    public async Task ExtractAsync_EmptyItems_CompletesWithNoItems()
    {
        var provider = new ScriptedAiProvider().Then(Completions.Completed("{\"items\":[]}"));

        var outcome = await Runner(provider).ExtractAsync(TestData.FileUnit("code"), TestData.Document(), KnowledgePipeline.DefaultContext, TestSupport.Ct);

        Assert.Equal(UnitOutcomeStatus.Completed, outcome.Status);
        Assert.Empty(outcome.Items);
    }

    [Fact]
    public async Task ExtractAsync_AnyScenario_NeverLogsPromptUnitTextOrModelOutput()
    {
        const string UnitSecret = "UNIT-SECRET-PAYLOAD";
        const string ModelSecret = "MODEL-SECRET-OUTPUT";
        const string ProviderSecret = "PROVIDER-SECRET-MESSAGE";
        var droppedLayer = TestData.Response(TestData.ItemJson(ModelSecret, kind: "layer"));
        var provider = new ScriptedAiProvider()
            .Then(Completions.Completed($"{ModelSecret} not json"))
            .Then(Completions.Completed(droppedLayer))
            .Then(Completions.Refused())
            .Throw(new AiProviderException(AiFailureKind.Permanent, ProviderSecret))
            .Then(Completions.Truncated());
        var runner = Runner(provider);
        var unit = TestData.FileUnit(UnitSecret);

        for (var run = 0; run < 5; run++)
        {
            await runner.ExtractAsync(unit, TestData.Document(), KnowledgePipeline.DefaultContext, TestSupport.Ct);
        }

        var logged = string.Join('\n', _logger.Entries);
        Assert.NotEmpty(_logger.Entries);
        Assert.DoesNotContain(UnitSecret, logged, StringComparison.Ordinal);
        Assert.DoesNotContain(ModelSecret, logged, StringComparison.Ordinal);
        Assert.DoesNotContain(ProviderSecret, logged, StringComparison.Ordinal);
        Assert.DoesNotContain(KnowledgePipeline.Prompt.SystemText, logged, StringComparison.Ordinal);
    }
}
