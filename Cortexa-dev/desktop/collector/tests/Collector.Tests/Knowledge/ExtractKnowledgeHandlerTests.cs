using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Collector.Domain.Documents;
using Collector.Domain.Extraction;
using Collector.Tests.Extraction;
using Collector.Tests.Support;

namespace Collector.Tests.Knowledge;

public sealed class ExtractKnowledgeHandlerTests
{
    private const string FailMarker = "[FAIL]";
    private const int UnitDelayMilliseconds = 15;
    private const int ManyUnits = 12;

    private readonly InMemoryDocumentStore _documents = new();
    private readonly InMemoryUnitStore _units = new();

    private void AddDocument(string id, params string[] unitTexts)
    {
        var document = TestData.Document(id);
        _documents.ByPath[document.SourcePath] = document;
        _units.ByDocumentId[id] = [.. unitTexts.Select((text, index) => TestData.FileUnit(text, id: $"{id}-u{index}", documentId: id) with { Ordinal = index })];
    }

    private void AddDocumentWithFiles(string id, params string[] filePaths)
    {
        var document = TestData.Document(id);
        _documents.ByPath[document.SourcePath] = document;
        _units.ByDocumentId[id] =
        [
            .. filePaths.Select((filePath, index) =>
                TestData.FileUnit($"text {index}", id: $"{id}-u{index}", documentId: id, filePath: filePath) with { Ordinal = index }),
        ];
    }

    private static FuncAiProvider FolderAwareProvider() => new(async (request, _) =>
    {
        await Task.CompletedTask;
        return request.UserText.Contains("\"folder\"", StringComparison.Ordinal)
            ? Completions.Completed($"{{\"items\":[{{\"kind\":\"layer\",\"title\":\"Layer idea\",\"summary\":\"A module role.\",\"details\":\"\"}}]}}")
            : Completions.Completed(TestData.Response(TestData.ItemJson($"File idea {Guid.NewGuid():N}")));
    });

    private ExtractKnowledgeHandler Handler(IAiProvider provider, int concurrency = 1) => new(
        _documents,
        _units,
        KnowledgePipeline.Runner(provider, new CapturingLogger<UnitExtractionRunner>()),
        new KnowledgeMerger(),
        KnowledgePipeline.Prompt,
        LayerPipeline.Runner(provider, new CapturingLogger<LayerExtractionRunner>()),
        new SingleProviderFactory(provider, concurrency));

    private static FuncAiProvider EchoProvider(Func<AiRequest, CancellationToken, Task<AiCompletion>>? inner = null) =>
        new(async (request, token) =>
        {
            if (inner is not null)
            {
                return await inner(request, token);
            }

            return request.UserText.Contains(FailMarker, StringComparison.Ordinal)
                ? throw new AiProviderException(AiFailureKind.Permanent, "boom")
                : Completions.Completed(TestData.Response(TestData.ItemJson($"Idea {Guid.NewGuid():N}")));
        });

    private static ExtractionRunRequest Request(params string[] ids) => new(ids);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ExtractAsync_Concurrency_NeverExceedsOption(int concurrency)
    {
        var inFlight = 0;
        var maxInFlight = 0;
        var provider = EchoProvider(async (_, token) =>
        {
            var now = Interlocked.Increment(ref inFlight);
            InterlockedMax(ref maxInFlight, now);
            await Task.Delay(UnitDelayMilliseconds, token);
            Interlocked.Decrement(ref inFlight);
            return Completions.Completed("{\"items\":[]}");
        });
        AddDocument("doc-a", Enumerable.Range(0, ManyUnits).Select(index => $"unit {index}").ToArray());

        await Handler(provider, concurrency).ExtractAsync(Request("doc-a"), null, TestSupport.Ct);

        Assert.InRange(maxInFlight, 1, concurrency);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref target);
        }
        while (value > current && Interlocked.CompareExchange(ref target, value, current) != current);
    }

    [Fact]
    public async Task ExtractAsync_Progress_ReportsEveryUnitUpToTotal()
    {
        AddDocument("doc-a", "one", "two", "three");
        var progress = new RecordingProgress<ExtractionProgress>();

        await Handler(EchoProvider()).ExtractAsync(Request("doc-a"), progress, TestSupport.Ct);

        Assert.Equal(3, progress.Reports.Count);
        Assert.All(progress.Reports, report => Assert.Equal(3, report.TotalUnits));
        Assert.Equal(3, progress.Reports.Max(report => report.CompletedUnits));
    }

    [Fact]
    public async Task ExtractAsync_SameTitleInOneDocument_MergesItems()
    {
        var provider = EchoProvider((_, _) => Task.FromResult(Completions.Completed(TestData.Response(TestData.ItemJson("Shared idea")))));
        AddDocument("doc-a", "one", "two");
        AddDocument("doc-b", "three");

        var result = await Handler(provider).ExtractAsync(Request("doc-a", "doc-b"), null, TestSupport.Ct);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(["doc-a", "doc-b"], result.Items.Select(item => item.DocumentId).Order());
    }

    [Fact]
    public async Task ExtractAsync_AllUnitsFail_RunIsFailedWithFailedDocuments()
    {
        AddDocument("doc-a", FailMarker + " one", FailMarker + " two");
        AddDocument("doc-b", FailMarker + " three");

        var result = await Handler(EchoProvider()).ExtractAsync(Request("doc-a", "doc-b"), null, TestSupport.Ct);

        Assert.True(result.IsFailed);
        Assert.Equal(["doc-a", "doc-b"], result.FailedDocumentIds.Order());
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task ExtractAsync_AllUnitsFailPermanent_RunCarriesPermanentFailureKind()
    {
        var provider = EchoProvider((_, _) => throw new AiProviderException(AiFailureKind.Permanent, "key rejected"));
        AddDocument("doc-a", "one");

        var result = await Handler(provider).ExtractAsync(Request("doc-a"), null, TestSupport.Ct);

        Assert.True(result.IsFailed);
        Assert.Equal(AiFailureKind.Permanent, result.FailureKind);
    }

    [Fact]
    public async Task ExtractAsync_AllUnitsFailQuotaExceeded_RunCarriesQuotaExceededFailureKind()
    {
        var provider = EchoProvider((_, _) => throw new AiProviderException(AiFailureKind.QuotaExceeded, "quota hit"));
        AddDocument("doc-a", "one");

        var result = await Handler(provider).ExtractAsync(Request("doc-a"), null, TestSupport.Ct);

        Assert.True(result.IsFailed);
        Assert.Equal(AiFailureKind.QuotaExceeded, result.FailureKind);
    }

    [Fact]
    public async Task ExtractAsync_PartialFailure_CountsFailedUnitsAndOnlyFullyFailedDocuments()
    {
        AddDocument("doc-a", "fine one", FailMarker + " bad");
        AddDocument("doc-b", FailMarker + " all bad");
        AddDocument("doc-c", "fine two");

        var result = await Handler(EchoProvider()).ExtractAsync(Request("doc-a", "doc-b", "doc-c"), null, TestSupport.Ct);

        Assert.False(result.IsFailed);
        Assert.Equal(4, result.TotalUnits);
        Assert.Equal(2, result.FailedUnits);
        Assert.Equal(["doc-b"], result.FailedDocumentIds);
    }

    [Fact]
    public async Task ExtractAsync_RefusedUnits_CountAsSkipped()
    {
        var provider = EchoProvider((_, _) => Task.FromResult(Completions.Refused()));
        AddDocument("doc-a", "one", "two");

        var result = await Handler(provider).ExtractAsync(Request("doc-a"), null, TestSupport.Ct);

        Assert.Equal(2, result.SkippedUnits);
        Assert.False(result.IsFailed);
    }

    [Fact]
    public async Task ExtractAsync_Completed_ReportsProviderModelAndPromptVersion()
    {
        AddDocument("doc-a", "one");

        var result = await Handler(EchoProvider()).ExtractAsync(Request("doc-a"), null, TestSupport.Ct);

        Assert.Equal(Completions.Model, result.Model);
        Assert.Equal(KnowledgePipeline.Prompt.Version, result.PromptVersion);
        Assert.Equal(Collector.Domain.Enums.CollectorProvider.Claude, result.Provider);
    }

    [Fact]
    public async Task ExtractAsync_UnknownDocumentAndDuplicates_AreSkippedOrDeduplicated()
    {
        AddDocument("doc-a", "one");

        var result = await Handler(EchoProvider()).ExtractAsync(Request("doc-a", "doc-a", "missing"), null, TestSupport.Ct);

        Assert.Equal(1, result.TotalUnits);
    }

    [Fact]
    public async Task ExtractAsync_CancelledToken_ThrowsOperationCanceled()
    {
        AddDocument("doc-a", "one", "two");
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Handler(EchoProvider()).ExtractAsync(Request("doc-a"), null, source.Token));
    }

    [Fact]
    public async Task ExtractAsync_FolderWithThreeOrMoreFiles_AppendsLayerItems()
    {
        AddDocumentWithFiles("doc-a", "src/app/a.cs", "src/app/b.cs", "src/app/c.cs");

        var result = await Handler(FolderAwareProvider()).ExtractAsync(Request("doc-a"), null, TestSupport.Ct);

        Assert.Equal(3, result.Items.Count(item => item.Kind != Collector.Domain.Enums.KnowledgeKind.Layer));
        var layerItem = Assert.Single(result.Items, item => item.Kind == Collector.Domain.Enums.KnowledgeKind.Layer);
        Assert.Equal(Collector.Domain.Enums.UnitKind.Module, layerItem.UnitKind);
        Assert.Equal("src/app/", layerItem.Source.FilePath);
        Assert.Equal("layer.v1", layerItem.PromptVersion);
    }

    [Fact]
    public async Task ExtractAsync_FolderWithFewerThanThreeFiles_NoLayerItems()
    {
        AddDocumentWithFiles("doc-a", "src/app/a.cs", "src/app/b.cs");

        var result = await Handler(FolderAwareProvider()).ExtractAsync(Request("doc-a"), null, TestSupport.Ct);

        Assert.DoesNotContain(result.Items, item => item.Kind == Collector.Domain.Enums.KnowledgeKind.Layer);
    }

    [Fact]
    public async Task ExtractAsync_LayerPass_ExtendsProgressTotalBeyondFileUnits()
    {
        AddDocumentWithFiles("doc-a", "src/app/a.cs", "src/app/b.cs", "src/app/c.cs");
        var progress = new RecordingProgress<ExtractionProgress>();

        await Handler(FolderAwareProvider()).ExtractAsync(Request("doc-a"), progress, TestSupport.Ct);

        Assert.Equal(4, progress.Reports.Count);
        Assert.Equal(4, progress.Reports.Max(report => report.TotalUnits));
        Assert.Equal(4, progress.Reports.Max(report => report.CompletedUnits));
    }

    [Fact]
    public async Task ExtractAsync_MissingApiKey_FailsWholeRunImmediately()
    {
        var provider = EchoProvider((_, _) => throw new AiProviderException(AiFailureKind.MissingApiKey, "no key"));
        AddDocument("doc-a", "one", "two", "three", "four");

        var exception = await Assert.ThrowsAsync<AiProviderException>(
            () => Handler(provider).ExtractAsync(Request("doc-a"), null, TestSupport.Ct));

        Assert.Equal(AiFailureKind.MissingApiKey, exception.Kind);
        Assert.Equal(1, provider.Calls);
    }
}
