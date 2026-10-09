using System.Collections.Concurrent;
using Collector.Application.Extraction;
using Collector.Application.Knowledge;
using Collector.Domain.Enums;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Collector.Tests.Extraction;

public sealed class ParallelSplitterTests : IDisposable
{
    private const int GatedFileCount = 8;
    private const int ConcurrencyLimit = 2;
    private const int SingleWorker = 1;
    private const string TextBody = "# Heading\nsome body content here for the splitter.";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"collector-splitter-{Guid.NewGuid():N}");
    private readonly FakePdfTextExtractor _pdf = new();
    private readonly InMemoryUnitStore _unitStore = new();
    private readonly WordCountTokenCounter _tokenCounter = new();
    private readonly ExtractionOptions _options = new();
    private readonly TokenEstimator _estimator;
    private readonly ParallelSplitter _splitter;

    public ParallelSplitterTests()
    {
        Directory.CreateDirectory(_directory);
        _estimator = new TokenEstimator(KnowledgePipeline.Builder(), _tokenCounter);
        var service = new ExtractionService(
            _pdf,
            new FakeDocxTextExtractor(),
            _tokenCounter,
            new InMemoryDocumentStore(),
            _unitStore,
            new FileContentGuard(),
            new TextNormalizer(),
            new CodeSplitter(_tokenCounter),
            new UnitBuilder(new HeadingDetector()),
            new DocumentStatusRules(),
            NullLogger<ExtractionService>.Instance);
        _splitter = new ParallelSplitter(service, _estimator, Options.Create(_options), NullLogger<ParallelSplitter>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string WriteFile(string name, string content = TextBody)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the condition.");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    private Task RunAsync(
        IReadOnlyList<SplitFile> files,
        ConcurrentQueue<SplitOutcome> outcomes,
        IProgress<SplitProgress>? progress = null) =>
        RunCancelableAsync(files, outcomes, TestContext.Current.CancellationToken, progress);

    private Task RunCancelableAsync(
        IReadOnlyList<SplitFile> files,
        ConcurrentQueue<SplitOutcome> outcomes,
        CancellationToken cancellationToken,
        IProgress<SplitProgress>? progress = null) =>
        _splitter.RunAsync(new SplitRequest(files, SourceType.Local), progress, outcomes.Enqueue, cancellationToken);

    [Fact]
    public async Task RunAsync_ManyFiles_NeverExceedsTheConfiguredConcurrency()
    {
        _options.MaxParallelSplits = ConcurrencyLimit;
        _pdf.Gate = new TaskCompletionSource();
        var files = Enumerable.Range(0, GatedFileCount).Select(i => new SplitFile(WriteFile($"doc{i}.pdf"))).ToList();
        var outcomes = new ConcurrentQueue<SplitOutcome>();

        var run = RunAsync(files, outcomes);
        await WaitUntilAsync(() => _pdf.Active == ConcurrencyLimit);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        _pdf.Gate.SetResult();
        await run;

        Assert.Equal(ConcurrencyLimit, _pdf.MaxActive);
        Assert.Equal(GatedFileCount, outcomes.Count);
    }

    [Fact]
    public async Task RunAsync_ZeroConfiguredConcurrency_StillProcessesEveryFile()
    {
        _options.MaxParallelSplits = 0;
        var files = new[] { new SplitFile(WriteFile("a.txt")), new SplitFile(WriteFile("b.txt")) };
        var outcomes = new ConcurrentQueue<SplitOutcome>();

        await RunAsync(files, outcomes);

        Assert.Equal(files.Length, outcomes.Count);
    }

    [Fact]
    public async Task RunAsync_Canceled_StopsStartingNewFilesAndKeepsDeliveredOutcomes()
    {
        _options.MaxParallelSplits = SingleWorker;
        _pdf.Gate = new TaskCompletionSource();
        var first = WriteFile("first.txt");
        var blocked = WriteFile("blocked.pdf");
        var files = new[] { new SplitFile(first), new SplitFile(blocked), new SplitFile(WriteFile("third.txt")), new SplitFile(WriteFile("fourth.txt")) };
        var outcomes = new ConcurrentQueue<SplitOutcome>();
        using var cts = new CancellationTokenSource();

        var run = RunCancelableAsync(files, outcomes, cts.Token);
        await _pdf.Entered.Task;
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        var delivered = Assert.Single(outcomes);
        Assert.Equal(first, delivered.Result.SourcePath);
    }

    [Fact]
    public async Task RunAsync_TextFile_ReportsUnitTokenAndPromptTotalsFromTheStoredUnits()
    {
        var path = WriteFile("notes.txt");
        var outcomes = new ConcurrentQueue<SplitOutcome>();

        await RunAsync([new SplitFile(path)], outcomes);

        var outcome = Assert.Single(outcomes);
        var units = _unitStore.ByDocumentId[outcome.Result.DocumentId!];
        Assert.Equal(DocumentStatus.Extracted, outcome.Result.Status);
        Assert.Equal(units.Count, outcome.UnitCount);
        Assert.Equal(units.Sum(unit => unit.TokenCount), outcome.TokenCount);
        Assert.Equal(_estimator.CountPromptTokens(units), outcome.PromptTokens);
        Assert.True(outcome.PromptTokens > 0);
    }

    [Fact]
    public async Task RunAsync_FileWithRepoPath_CarriesTheRepoPathOnTheOutcome()
    {
        const string RepoPath = "docs/notes.txt";
        var outcomes = new ConcurrentQueue<SplitOutcome>();

        await RunAsync([new SplitFile(WriteFile("notes.txt"), RepoPath)], outcomes);

        Assert.Equal(RepoPath, Assert.Single(outcomes).RepoPath);
    }

    [Fact]
    public async Task RunAsync_DuplicatePathsIgnoringCase_SplitsTheFileOnce()
    {
        var path = WriteFile("notes.txt");
        var files = new[] { new SplitFile(path), new SplitFile(path.ToUpperInvariant()), new SplitFile(path) };
        var outcomes = new ConcurrentQueue<SplitOutcome>();

        await RunAsync(files, outcomes);

        Assert.Single(outcomes);
    }

    [Fact]
    public async Task RunAsync_MissingFile_DeliversAFailedOutcomeWithZeroCounts()
    {
        var missing = Path.Combine(_directory, "missing.txt");
        var outcomes = new ConcurrentQueue<SplitOutcome>();

        await RunAsync([new SplitFile(missing)], outcomes);

        var outcome = Assert.Single(outcomes);
        Assert.Equal(DocumentStatus.Failed, outcome.Result.Status);
        Assert.Equal(0, outcome.UnitCount);
        Assert.Equal(0, outcome.TokenCount);
        Assert.Equal(0, outcome.PromptTokens);
    }

    [Fact]
    public async Task RunAsync_Progress_ReportsEveryFileWithTheDistinctTotal()
    {
        var files = new[] { new SplitFile(WriteFile("a.txt")), new SplitFile(WriteFile("b.txt")), new SplitFile(WriteFile("c.txt")) };
        var progress = new ListProgress<SplitProgress>();
        var outcomes = new ConcurrentQueue<SplitOutcome>();

        await RunAsync(files, outcomes, progress);

        Assert.Equal([1, 2, 3], progress.Items.Select(item => item.Done).Order());
        Assert.All(progress.Items, item => Assert.Equal(files.Length, item.Total));
    }
}
