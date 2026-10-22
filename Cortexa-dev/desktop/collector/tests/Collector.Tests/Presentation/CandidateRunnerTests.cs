using Collector.Application.Candidates;
using Collector.Application.Extraction;
using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Application.Settings;
using Collector.Domain.Documents;
using Collector.Domain.Enums;
using Collector.Presentation.Services;
using Collector.Tests.Candidates;
using Collector.Tests.Extraction;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging;

namespace Collector.Tests.Presentation;

public sealed class CandidateRunnerTests : IDisposable
{
    private const int TokenCount = 50;
    private const int TokenLimit = 1000;
    private const int ConfiguredKeys = 2;
    private const int RejectedTokenCount = 5000;
    private const string ExhaustedModel = "gemini-exhausted";
    private const string DocumentText = "The adaptive cache evicts entries by predicted reuse distance.";
    private const string Reply =
        "{\"candidates\":[{\"title\":\"Adaptive cache\",\"summary\":\"s\",\"patentability_rationale\":\"r\",\"excerpt_quote\":\"evicts entries by predicted reuse distance\"}]}";
    private const string SecretMessage = "super-secret-detail-xyz";

    private readonly string _directory = Directory.CreateTempSubdirectory("candidate-runner").FullName;
    private readonly InMemoryDocumentStore _store = new();
    private readonly CountingKeyStore _keys = new();
    private readonly CapturingLogger _logger = new();

    public void Dispose() => Directory.Delete(_directory, true);

    private async Task<string> AddDocumentAsync(string name)
    {
        var path = Path.Combine(_directory, name);
        await File.WriteAllTextAsync(path, DocumentText, TestSupport.Ct);
        var document = await _store.UpsertAsync(SourceType.Local, SourceKind.Paper, path, name, "hash", 1, TestSupport.Ct);
        return document.Id;
    }

    private CandidateRunner Runner(IAiProvider provider, IDocumentStore? store = null, IGeminiModelExhaustion? tracker = null)
    {
        var prompt = CandidatePromptLoader.Load();
        var builder = new CandidatePromptBuilder(prompt, new UntrustedSourceGuard(KnowledgePromptLoader.Load()));
        var preparer = new DocumentPreparer(
            new WholeDocumentReader(new FakePdfTextExtractor(), new FakeDocxTextExtractor(), new FileContentGuard(), new TextNormalizer()),
            builder,
            new GeminiInputLimitCheck(new FakeInputBudget(TokenCount, TokenLimit)));
        var factory = new SingleProviderFactory(provider);
        var runner = new DocumentCandidateRunner(
            preparer,
            factory,
            new ReplyInterpreter(new CandidateParser(), new ExcerptVerifier(new AnchorLocator())),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DocumentCandidateRunner>.Instance);
        var handler = new IdentifyCandidatesHandler(store ?? _store, factory, runner, tracker ?? new Collector.Infrastructure.Ai.GeminiModelExhaustionTracker());
        var settings = new SettingsService(new FakeUserSettingsStore(), new InMemorySecretStore(), _keys);
        return new CandidateRunner(handler, settings, _logger);
    }

    [Fact]
    public async Task RunAsync_CanceledAfterFirstDocument_KeepsPartialResultAndSetsCanceled()
    {
        var ids = new[] { await AddDocumentAsync("a.txt"), await AddDocumentAsync("b.txt") };
        var provider = new ScriptedAiProvider().Then(Completions.Completed(Reply)).Then(Completions.Completed(Reply));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestSupport.Ct);
        var progress = new CancelingProgress(cts);

        var outcome = await Runner(provider).RunAsync(ids, progress, cts.Token);

        Assert.True(outcome.Canceled);
        Assert.False(outcome.IsFailed);
        Assert.Equal([CandidateOutcome.Completed, CandidateOutcome.Canceled], outcome.Result!.Documents.Select(document => document.Outcome));
        Assert.Single(outcome.Result.AllCandidates);
    }

    [Fact]
    public async Task RunAsync_NotCanceled_CanceledFlagIsFalse()
    {
        var ids = new[] { await AddDocumentAsync("a.txt") };
        var provider = new ScriptedAiProvider().Then(Completions.Completed(Reply));

        var outcome = await Runner(provider).RunAsync(ids, new Progress<CandidateProgress>(), TestSupport.Ct);

        Assert.False(outcome.Canceled);
        Assert.Single(outcome.Result!.AllCandidates);
    }

    [Fact]
    public async Task RunAsync_AllDocumentsCompleted_DoesNotReadKeyCount()
    {
        var ids = new[] { await AddDocumentAsync("a.txt") };
        var provider = new ScriptedAiProvider().Then(Completions.Completed(Reply));

        var outcome = await Runner(provider).RunAsync(ids, new Progress<CandidateProgress>(), TestSupport.Ct);

        Assert.Equal(0, _keys.Reads);
        Assert.Equal(0, outcome.ConfiguredKeyCount);
    }

    [Fact]
    public async Task RunAsync_NetworkFailureOnly_DoesNotReadKeyCount()
    {
        var ids = new[] { await AddDocumentAsync("a.txt") };
        var provider = new ScriptedAiProvider().Throw(new AiProviderException(AiFailureKind.Transient, "offline"));

        await Runner(provider).RunAsync(ids, new Progress<CandidateProgress>(), TestSupport.Ct);

        Assert.Equal(0, _keys.Reads);
    }

    [Theory]
    [InlineData(AiFailureKind.QuotaExceeded, null)]
    [InlineData(AiFailureKind.Permanent, 401)]
    public async Task RunAsync_KeyRelatedFailure_ReadsConfiguredKeyCount(AiFailureKind kind, int? status)
    {
        _keys.Keys.AddRange([new GeminiKey("k1", "AIza-one-1111"), new GeminiKey("k2", "AIza-two-2222")]);
        var ids = new[] { await AddDocumentAsync("a.txt") };
        var provider = new ScriptedAiProvider().Throw(new AiProviderException(kind, "denied", null, status));

        var outcome = await Runner(provider).RunAsync(ids, new Progress<CandidateProgress>(), TestSupport.Ct);

        Assert.Equal(ConfiguredKeys, outcome.ConfiguredKeyCount);
        Assert.Equal(1, _keys.Reads);
    }

    [Fact]
    public async Task RunAsync_UnexpectedException_ReturnsFailedOutcome()
    {
        var provider = new ScriptedAiProvider();

        var outcome = await Runner(provider, new ThrowingStore()).RunAsync(["x"], new Progress<CandidateProgress>(), TestSupport.Ct);

        Assert.True(outcome.IsFailed);
        Assert.Null(outcome.Result);
        Assert.False(outcome.Canceled);
        Assert.Equal(0, outcome.ConfiguredKeyCount);
    }

    [Fact]
    public async Task RunAsync_UnexpectedException_LogsTypeButNotMessage()
    {
        var provider = new ScriptedAiProvider();

        await Runner(provider, new ThrowingStore()).RunAsync(["x"], new Progress<CandidateProgress>(), TestSupport.Ct);

        var entry = Assert.Single(_logger.Entries);
        Assert.Contains(nameof(InvalidOperationException), entry, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretMessage, entry, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ProviderReportsInputTooLarge_DocumentTooLargeWithCountAndLimitAndNoKeyRead()
    {
        var ids = new[] { await AddDocumentAsync("a.txt") };
        var provider = new ScriptedAiProvider().Throw(new AiInputTooLargeException(RejectedTokenCount, TokenLimit, "gemini-fallback"));

        var outcome = await Runner(provider).RunAsync(ids, new Progress<CandidateProgress>(), TestSupport.Ct);

        var document = Assert.Single(outcome.Result!.Documents);
        Assert.Equal(CandidateOutcome.TooLarge, document.Outcome);
        Assert.Equal(RejectedTokenCount, document.TokenCount);
        Assert.Equal(TokenLimit, document.TokenLimit);
        Assert.Equal(0, _keys.Reads);
    }

    [Fact]
    public async Task RunAsync_TrackerHasExhaustedModels_ResetsItAtRunStart()
    {
        var ids = new[] { await AddDocumentAsync("a.txt") };
        var tracker = new Collector.Infrastructure.Ai.GeminiModelExhaustionTracker();
        tracker.MarkExhausted(ExhaustedModel);
        var provider = new ScriptedAiProvider().Then(Completions.Completed(Reply));

        await Runner(provider, tracker: tracker).RunAsync(ids, new Progress<CandidateProgress>(), TestSupport.Ct);

        Assert.False(tracker.IsExhausted(ExhaustedModel));
    }

    private sealed class CancelingProgress(CancellationTokenSource source) : IProgress<CandidateProgress>
    {
        public void Report(CandidateProgress value) => source.Cancel();
    }

    private sealed class ThrowingStore : IDocumentStore
    {
        public Task<CollectorDocument> UpsertAsync(
            SourceType sourceType,
            SourceKind sourceKind,
            string sourcePath,
            string filename,
            string contentHash,
            long sizeBytes,
            CancellationToken cancellationToken) => throw new InvalidOperationException(SecretMessage);

        public Task<CollectorDocument?> GetAsync(string documentId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(SecretMessage);

        public Task UpdateStatusAsync(string documentId, DocumentStatus status, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(SecretMessage);
    }

    private sealed class CountingKeyStore : IGeminiKeyStore
    {
        public List<GeminiKey> Keys { get; } = [];

        public int Reads { get; private set; }

        public Task<GeminiKeyList> GetKeysAsync(CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(new GeminiKeyList([.. Keys]));
        }

        public Task<GeminiKey> AddKeyAsync(string rawKey, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RemoveKeyAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ReorderAsync(IReadOnlyList<string> orderedIds, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CapturingLogger : ILogger<CandidateRunner>
    {
        public List<string> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add(formatter(state, exception) + exception);
        }
    }
}
