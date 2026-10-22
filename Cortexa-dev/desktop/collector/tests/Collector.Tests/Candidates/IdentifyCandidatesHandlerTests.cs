using Collector.Application.Candidates;
using Collector.Application.Extraction;
using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Collector.Domain.Enums;
using Collector.Tests.Extraction;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Collector.Tests.Candidates;

public sealed class IdentifyCandidatesHandlerTests : IDisposable
{
    private const int TokenCount = 50;
    private const int TokenLimit = 1000;
    private const string DocumentText = "The adaptive cache evicts entries by predicted reuse distance.";
    private const int RejectedTokenCount = 5000;
    private const string ExhaustedModel = "gemini-exhausted";
    private const string Quote = "evicts entries by predicted reuse distance";

    private readonly string _directory = Directory.CreateTempSubdirectory("identify").FullName;
    private readonly InMemoryDocumentStore _store = new();

    public void Dispose() => Directory.Delete(_directory, true);

    private static string Reply(string quote) =>
        "{\"candidates\":[{\"title\":\"Adaptive cache\",\"summary\":\"s\",\"patentability_rationale\":\"r\",\"excerpt_quote\":\"" + quote + "\"}]}";

    private async Task<string> AddDocumentAsync(string name)
    {
        var path = Path.Combine(_directory, name);
        await File.WriteAllTextAsync(path, DocumentText, TestSupport.Ct);
        var document = await _store.UpsertAsync(SourceType.Local, SourceKind.Paper, path, name, "hash", 1, TestSupport.Ct);
        return document.Id;
    }

    private IdentifyCandidatesHandler Handler(IAiProvider provider, int concurrency = 1, int tokens = TokenCount, IGeminiModelExhaustion? tracker = null)
    {
        var prompt = CandidatePromptLoader.Load();
        var builder = new CandidatePromptBuilder(prompt, new UntrustedSourceGuard(KnowledgePromptLoader.Load()));
        var preparer = new DocumentPreparer(
            new WholeDocumentReader(new FakePdfTextExtractor(), new FakeDocxTextExtractor(), new FileContentGuard(), new TextNormalizer()),
            builder,
            new GeminiInputLimitCheck(new FakeInputBudget(tokens, TokenLimit)));
        var runner = new DocumentCandidateRunner(
            preparer,
            new SingleProviderFactory(provider, concurrency),
            new ReplyInterpreter(new CandidateParser(), new ExcerptVerifier(new AnchorLocator())),
            NullLogger<DocumentCandidateRunner>.Instance);
        return new IdentifyCandidatesHandler(_store, new SingleProviderFactory(provider, concurrency), runner, tracker ?? new Collector.Infrastructure.Ai.GeminiModelExhaustionTracker());
    }

    [Fact]
    public async Task IdentifyAsync_OneFailureKind_OthersStillComplete()
    {
        var ids = new[] { await AddDocumentAsync("a.txt"), await AddDocumentAsync("b.txt"), await AddDocumentAsync("c.txt") };
        var provider = new ScriptedAiProvider()
            .Then(Completions.Completed(Reply(Quote)))
            .Throw(new AiProviderException(AiFailureKind.QuotaExceeded, "quota"))
            .Then(Completions.Completed(Reply(Quote)));

        var result = await Handler(provider).IdentifyAsync(new CandidateRunRequest(ids), null, TestSupport.Ct);

        Assert.Equal(
            [CandidateOutcome.Completed, CandidateOutcome.Quota, CandidateOutcome.Completed],
            result.Documents.Select(document => document.Outcome));
        Assert.Equal(2, result.AllCandidates.Count);
        Assert.Equal(Completions.Model, result.Model);
        var completed = result.Documents[0];
        Assert.Equal(TokenCount, completed.TokenCount);
        Assert.Equal(TokenLimit, completed.TokenLimit);
        Assert.True(completed.Candidates[0].Excerpt!.Verified);
    }

    [Fact]
    public async Task IdentifyAsync_QuoteNotInSource_KeepsCandidateUnverified()
    {
        var id = await AddDocumentAsync("a.txt");
        var provider = new ScriptedAiProvider().Then(Completions.Completed(Reply("text that is not in the document")));

        var result = await Handler(provider).IdentifyAsync(new CandidateRunRequest([id]), null, TestSupport.Ct);

        var candidate = Assert.Single(result.AllCandidates);
        Assert.False(candidate.Excerpt!.Verified);
        Assert.Null(candidate.Excerpt.Offset);
    }

    [Fact]
    public async Task IdentifyAsync_Cancelled_KeepsFinishedAndMarksRestCanceled()
    {
        var ids = new[] { await AddDocumentAsync("a.txt"), await AddDocumentAsync("b.txt"), await AddDocumentAsync("c.txt") };
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestSupport.Ct);
        var provider = new FuncAiProvider((_, _) =>
        {
            source.Cancel();
            return Task.FromResult(Completions.Completed(Reply(Quote)));
        });

        var result = await Handler(provider).IdentifyAsync(new CandidateRunRequest(ids), null, source.Token);

        Assert.Equal(
            [CandidateOutcome.Completed, CandidateOutcome.Canceled, CandidateOutcome.Canceled],
            result.Documents.Select(document => document.Outcome));
        Assert.Single(result.Documents[0].Candidates);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task IdentifyAsync_MissingDocument_FailedNotFound()
    {
        var provider = new ScriptedAiProvider();

        var result = await Handler(provider).IdentifyAsync(new CandidateRunRequest(["missing", "missing"]), null, TestSupport.Ct);

        var document = Assert.Single(result.Documents);
        Assert.Equal(CandidateOutcome.Failed, document.Outcome);
        Assert.Equal("document not found", document.Reason);
        Assert.Empty(provider.Requests);
    }

    [Fact]
    public async Task IdentifyAsync_OverInputLimit_TooLargeWithoutProviderCall()
    {
        var id = await AddDocumentAsync("a.txt");
        var provider = new ScriptedAiProvider();

        var result = await Handler(provider, tokens: TokenLimit + 1).IdentifyAsync(new CandidateRunRequest([id]), null, TestSupport.Ct);

        var document = Assert.Single(result.Documents);
        Assert.Equal(CandidateOutcome.TooLarge, document.Outcome);
        Assert.Equal(TokenLimit + 1, document.TokenCount);
        Assert.Equal(TokenLimit, document.TokenLimit);
        Assert.Empty(provider.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    public async Task IdentifyAsync_UnparseableReply_FailedNotCompleted(string reply)
    {
        var id = await AddDocumentAsync("a.txt");
        var provider = new ScriptedAiProvider().Then(Completions.Completed(reply));

        var result = await Handler(provider).IdentifyAsync(new CandidateRunRequest([id]), null, TestSupport.Ct);

        Assert.Equal(CandidateOutcome.Failed, Assert.Single(result.Documents).Outcome);
    }

    [Fact]
    public async Task IdentifyAsync_EmptyCandidatesReply_CompletedWithNone()
    {
        var id = await AddDocumentAsync("a.txt");
        var provider = new ScriptedAiProvider().Then(Completions.Completed("{\"candidates\":[]}"));

        var result = await Handler(provider).IdentifyAsync(new CandidateRunRequest([id]), null, TestSupport.Ct);

        var document = Assert.Single(result.Documents);
        Assert.Equal(CandidateOutcome.Completed, document.Outcome);
        Assert.Empty(document.Candidates);
    }

    [Fact]
    public async Task IdentifyAsync_RequestCarriesNoFileNameAndNoModelOverride()
    {
        var id = await AddDocumentAsync("secret-name.txt");
        var provider = new ScriptedAiProvider().Then(Completions.Completed("{\"candidates\":[]}"));

        await Handler(provider).IdentifyAsync(new CandidateRunRequest([id]), null, TestSupport.Ct);

        var request = Assert.Single(provider.Requests);
        Assert.Null(request.Model);
        Assert.DoesNotContain("secret-name", request.UserText, StringComparison.Ordinal);
        Assert.Contains(DocumentText, request.UserText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IdentifyAsync_ReportsProgressForEachDocument()
    {
        var ids = new[] { await AddDocumentAsync("a.txt"), await AddDocumentAsync("b.txt") };
        var provider = new ScriptedAiProvider()
            .Then(Completions.Completed("{\"candidates\":[]}"))
            .Then(Completions.Completed("{\"candidates\":[]}"));
        var progress = new RecordingProgress<CandidateProgress>();

        await Handler(provider).IdentifyAsync(new CandidateRunRequest(ids), progress, TestSupport.Ct);

        Assert.Equal(2, progress.Reports.Count);
        Assert.All(progress.Reports, report => Assert.Equal(ids.Length, report.Total));
    }

    [Fact]
    public async Task IdentifyAsync_ProviderReportsInputTooLarge_TooLargeWithCountAndLimitOthersUnchanged()
    {
        var ids = new[] { await AddDocumentAsync("a.txt"), await AddDocumentAsync("b.txt"), await AddDocumentAsync("c.txt") };
        var provider = new ScriptedAiProvider()
            .Then(Completions.Completed(Reply(Quote)))
            .Throw(new AiInputTooLargeException(RejectedTokenCount, TokenLimit, "gemini-fallback"))
            .Then(Completions.Completed(Reply(Quote)));

        var result = await Handler(provider).IdentifyAsync(new CandidateRunRequest(ids), null, TestSupport.Ct);

        Assert.Equal(
            [CandidateOutcome.Completed, CandidateOutcome.TooLarge, CandidateOutcome.Completed],
            result.Documents.Select(document => document.Outcome));
        Assert.Equal(RejectedTokenCount, result.Documents[1].TokenCount);
        Assert.Equal(TokenLimit, result.Documents[1].TokenLimit);
        Assert.Equal(2, result.AllCandidates.Count);
    }

    [Fact]
    public async Task IdentifyAsync_AllModelsOutOfQuota_QuotaOutcomeForEveryDocument()
    {
        var ids = new[] { await AddDocumentAsync("a.txt"), await AddDocumentAsync("b.txt") };
        var provider = new FuncAiProvider((_, _) =>
            throw new AiProviderException(AiFailureKind.QuotaExceeded, "All configured Gemini models are out of quota for this run."));

        var result = await Handler(provider).IdentifyAsync(new CandidateRunRequest(ids), null, TestSupport.Ct);

        Assert.All(result.Documents, document => Assert.Equal(CandidateOutcome.Quota, document.Outcome));
    }

    [Fact]
    public async Task IdentifyAsync_TrackerHasExhaustedModels_ResetsItAtRunStart()
    {
        var id = await AddDocumentAsync("a.txt");
        var tracker = new Collector.Infrastructure.Ai.GeminiModelExhaustionTracker();
        tracker.MarkExhausted(ExhaustedModel);
        var provider = new ScriptedAiProvider().Then(Completions.Completed(Reply(Quote)));

        await Handler(provider, tracker: tracker).IdentifyAsync(new CandidateRunRequest([id]), null, TestSupport.Ct);

        Assert.False(tracker.IsExhausted(ExhaustedModel));
    }
}
