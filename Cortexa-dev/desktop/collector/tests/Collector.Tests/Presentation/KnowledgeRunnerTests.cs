using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Application.Settings;
using Collector.Domain.Enums;
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

    private KnowledgeRunner Runner(IAiProvider provider, IGeminiKeyStore? geminiKeys = null)
    {
        var handler = new ExtractKnowledgeHandler(
            _documents,
            _units,
            KnowledgePipeline.Runner(provider, new CapturingLogger<UnitExtractionRunner>()),
            new KnowledgeMerger(),
            KnowledgePipeline.Prompt,
            LayerPipeline.Runner(provider, new CapturingLogger<LayerExtractionRunner>()),
            new SingleProviderFactory(provider));
        var settings = new SettingsService(new FakeUserSettingsStore(), new InMemorySecretStore(), geminiKeys ?? new FakeGeminiKeyStore());
        return new KnowledgeRunner(handler, settings, NullLogger<KnowledgeRunner>.Instance);
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

    [Fact]
    public async Task RunAsync_GeminiPermanentFailureWithMultipleKeysConfigured_ReportsTheConfiguredKeyCount()
    {
        AddDocument(DocumentId, "one");
        var geminiKeys = new FakeGeminiKeyStore();
        geminiKeys.Keys.Add(new GeminiKey("key-1", "value-1"));
        geminiKeys.Keys.Add(new GeminiKey("key-2", "value-2"));
        var request = new KnowledgeRunRequest([DocumentId], CollectorProvider.Gemini);

        var outcome = await Runner(FailingProvider(AiFailureKind.Permanent), geminiKeys)
            .RunAsync(request, new RecordingProgress<ExtractionProgress>(), TestSupport.Ct);

        Assert.Equal(KnowledgeRunStatus.KeyRejected, outcome.Status);
        Assert.Equal(2, outcome.ConfiguredKeyCount);
    }

    [Fact]
    public async Task RunAsync_GeminiQuotaExceededWithMultipleKeysConfigured_ReportsTheConfiguredKeyCount()
    {
        AddDocument(DocumentId, "one");
        var geminiKeys = new FakeGeminiKeyStore();
        geminiKeys.Keys.Add(new GeminiKey("key-1", "value-1"));
        geminiKeys.Keys.Add(new GeminiKey("key-2", "value-2"));
        geminiKeys.Keys.Add(new GeminiKey("key-3", "value-3"));
        var request = new KnowledgeRunRequest([DocumentId], CollectorProvider.Gemini);

        var outcome = await Runner(FailingProvider(AiFailureKind.QuotaExceeded), geminiKeys)
            .RunAsync(request, new RecordingProgress<ExtractionProgress>(), TestSupport.Ct);

        Assert.Equal(KnowledgeRunStatus.QuotaExceeded, outcome.Status);
        Assert.Equal(3, outcome.ConfiguredKeyCount);
    }

    [Fact]
    public async Task RunAsync_GeminiPermanentFailureWithOnlyOneKeyConfigured_ReportsSingleKeyCount()
    {
        AddDocument(DocumentId, "one");
        var geminiKeys = new FakeGeminiKeyStore();
        geminiKeys.Keys.Add(new GeminiKey("key-1", "value-1"));
        var request = new KnowledgeRunRequest([DocumentId], CollectorProvider.Gemini);

        var outcome = await Runner(FailingProvider(AiFailureKind.Permanent), geminiKeys)
            .RunAsync(request, new RecordingProgress<ExtractionProgress>(), TestSupport.Ct);

        Assert.Equal(KnowledgeRunStatus.KeyRejected, outcome.Status);
        Assert.Equal(1, outcome.ConfiguredKeyCount);
    }

    [Fact]
    public async Task RunAsync_ClaudeProviderFailure_NeverReportsAConfiguredKeyCount()
    {
        AddDocument(DocumentId, "one");
        var geminiKeys = new FakeGeminiKeyStore();
        geminiKeys.Keys.Add(new GeminiKey("key-1", "value-1"));
        geminiKeys.Keys.Add(new GeminiKey("key-2", "value-2"));
        var request = new KnowledgeRunRequest([DocumentId], CollectorProvider.Claude);

        var outcome = await Runner(FailingProvider(AiFailureKind.Permanent), geminiKeys)
            .RunAsync(request, new RecordingProgress<ExtractionProgress>(), TestSupport.Ct);

        Assert.Equal(KnowledgeRunStatus.KeyRejected, outcome.Status);
        Assert.Equal(0, outcome.ConfiguredKeyCount);
    }
}
