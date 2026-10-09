using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Collector.Presentation.Services;
using Collector.Tests.Extraction;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Collector.Tests.Presentation;

public sealed class KnowledgeRunnerTests
{
    private const string DocumentId = "doc-1";

    private readonly InMemoryDocumentStore _documents = new();
    private readonly InMemoryUnitStore _units = new();

    private void AddDocument(string id, params string[] unitTexts)
    {
        var document = TestData.Document(id);
        _documents.ByPath[document.SourcePath] = document;
        _units.ByDocumentId[id] =
            [.. unitTexts.Select((text, index) => TestData.FileUnit(text, id: $"{id}-u{index}", documentId: id) with { Ordinal = index })];
    }

    private KnowledgeRunner Runner(IAiProvider provider)
    {
        var handler = new ExtractKnowledgeHandler(
            _documents,
            _units,
            KnowledgePipeline.Runner(provider, new CapturingLogger<UnitExtractionRunner>()),
            new KnowledgeMerger(),
            KnowledgePipeline.Prompt,
            LayerPipeline.Runner(provider, new CapturingLogger<LayerExtractionRunner>()),
            new SingleProviderFactory(provider));
        return new KnowledgeRunner(handler, NullLogger<KnowledgeRunner>.Instance);
    }

    private static FuncAiProvider FailingProvider(AiFailureKind kind) =>
        new((_, _) => throw new AiProviderException(kind, "provider said no"));

    [Fact]
    public async Task RunAsync_AllUnitsPermanentFailure_SurfacesKeyRejectedStatus()
    {
        AddDocument(DocumentId, "one");
        var request = new KnowledgeRunRequest([DocumentId]);

        var outcome = await Runner(FailingProvider(AiFailureKind.Permanent))
            .RunAsync(request, new RecordingProgress<ExtractionProgress>(), TestSupport.Ct);

        Assert.Equal(KnowledgeRunStatus.KeyRejected, outcome.Status);
    }

    [Fact]
    public async Task RunAsync_AllUnitsQuotaExceeded_SurfacesQuotaExceededStatus()
    {
        AddDocument(DocumentId, "one");
        var request = new KnowledgeRunRequest([DocumentId]);

        var outcome = await Runner(FailingProvider(AiFailureKind.QuotaExceeded))
            .RunAsync(request, new RecordingProgress<ExtractionProgress>(), TestSupport.Ct);

        Assert.Equal(KnowledgeRunStatus.QuotaExceeded, outcome.Status);
    }

    [Fact]
    public async Task RunAsync_AllUnitsTransientFailure_SurfacesNetworkFailedStatus()
    {
        AddDocument(DocumentId, "one");
        var request = new KnowledgeRunRequest([DocumentId]);

        var outcome = await Runner(FailingProvider(AiFailureKind.Transient))
            .RunAsync(request, new RecordingProgress<ExtractionProgress>(), TestSupport.Ct);

        Assert.Equal(KnowledgeRunStatus.NetworkFailed, outcome.Status);
    }
}
