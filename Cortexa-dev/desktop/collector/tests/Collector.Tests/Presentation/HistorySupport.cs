using Collector.Application.History;
using Collector.Application.Ports;
using Collector.Domain.Enums;
using Collector.Domain.History;
using Collector.Domain.Upload;
using Collector.Infrastructure.Options;
using Collector.Presentation.Services;
using Collector.Presentation.ViewModels;
using Collector.Tests.Extraction;
using Collector.Tests.Support;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Collector.Tests.Presentation;

internal static class HistoryData
{
    public static readonly DateTimeOffset Start = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    public static BatchSummary Batch(
        string id,
        string state = "InProgress",
        BatchStage stage = BatchStage.Extracted,
        int minutesAgo = 0) => new()
        {
            BatchId = id,
            BatchName = $"Name {id}",
            CreatedAt = Start.AddMinutes(-minutesAgo),
            State = state,
            Stage = stage,
            ExtractionCompletedCount = 10,
            ExtractionTotalCount = 10,
            EvidenceCompletedCount = 12,
            EmbeddingCompletedCount = 40,
            EmbeddingTotalCount = 40,
            HarvestingCompletedCount = 96,
            HarvestingTotalCount = 140,
            SeedingCompletedCount = 0,
            SeedingTotalCount = 0,
        };

    public static BatchCandidate Candidate(
        string id,
        string engine = "harvesting",
        double? score = 78.5,
        int? patentability = 64,
        IReadOnlyList<CandidateKnowledgeLink>? links = null) => new()
        {
            CandidateId = id,
            Engine = engine,
            Title = $"Title {id}",
            Kind = "data_model",
            EvidenceCount = 3,
            Score = score,
            Patentability = patentability,
            KnowledgeLinks = links ?? [],
        };

    public static CandidateKnowledgeLink Link(string documentId = "doc-1", string itemId = "item-1") => new()
    {
        KnowledgeItem = new LinkedKnowledgeItem
        {
            Id = itemId,
            Kind = KnowledgeKind.Method,
            Title = $"Knowledge {itemId}",
            Summary = "Summary text",
        },
        Source = new CandidateSource { DocumentId = documentId, PageNumber = 4 },
    };

    public static BatchResults Results(string batchId, params BatchCandidate[] candidates) =>
        new() { BatchId = batchId, Candidates = candidates };

    public static BatchHistoryException ServerError() => new(500, false, "server error");

    public static BatchHistoryException AuthError() => new(401, true, "unauthorized");
}

internal sealed class FakeBatchHistoryClient : IBatchHistoryClient
{
    private readonly Dictionary<string, BatchResults?> _results = [];
    private readonly Dictionary<string, Exception> _resultErrors = [];

    public IReadOnlyList<BatchSummary> Batches { get; set; } = [];

    public Exception? ListError { get; set; }

    public int ListCalls { get; private set; }

    public List<string> ResultCalls { get; } = [];

    public TaskCompletionSource? ResultsGate { get; set; }

    public void SetResults(string batchId, BatchResults? results)
    {
        _results[batchId] = results;
        _resultErrors.Remove(batchId);
    }

    public void SetResultError(string batchId, Exception error) => _resultErrors[batchId] = error;

    public Task<IReadOnlyList<BatchSummary>> ListBatchesAsync(CancellationToken cancellationToken)
    {
        ListCalls++;
        return ListError is null ? Task.FromResult(Batches) : Task.FromException<IReadOnlyList<BatchSummary>>(ListError);
    }

    public async Task<BatchResults?> GetResultsAsync(string serverBatchId, CancellationToken cancellationToken)
    {
        ResultCalls.Add(serverBatchId);
        if (ResultsGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        if (_resultErrors.TryGetValue(serverBatchId, out var error))
        {
            throw error;
        }

        return _results.TryGetValue(serverBatchId, out var results) ? results : HistoryData.Results(serverBatchId);
    }
}

internal sealed class FakeFileLauncher : ILocalFileLauncher
{
    public bool Result { get; set; } = true;

    public List<LocalSourceTarget> Launched { get; } = [];

    public bool TryLaunch(LocalSourceTarget target)
    {
        Launched.Add(target);
        return Result;
    }
}

internal sealed class RecordingClipboard : IClipboard
{
    public string? Text { get; private set; }

    public bool TrySetText(string text)
    {
        Text = text;
        return true;
    }
}

internal sealed class HistoryHarness
{
    public const int PollSeconds = 5;

    public HistoryHarness(string? serverBatchId = null)
    {
        Resolver = new LocalSourceResolver(Batches, Documents);
        Poller = new HistoryPoller(Time, Options.Create(new HistoryOptions { PollSeconds = PollSeconds }));
        var services = new HistoryServices(
            Client,
            Resolver,
            Launcher,
            Clipboard,
            Time,
            Options.Create(new HistoryOptions { PollSeconds = PollSeconds }));
        ViewModel = new HistoryViewModel(services, Poller, Navigation);
        ServerBatchId = serverBatchId;
    }

    public string? ServerBatchId { get; }

    public FakeTimeProvider Time { get; } = new(HistoryData.Start);

    public FakeBatchHistoryClient Client { get; } = new();

    public FakeFileLauncher Launcher { get; } = new();

    public RecordingClipboard Clipboard { get; } = new();

    public FakeNavigationService Navigation { get; } = new();

    public InMemoryBatchStore Batches { get; } = new();

    public InMemoryDocumentStore Documents { get; } = new();

    public LocalSourceResolver Resolver { get; }

    public HistoryPoller Poller { get; }

    public HistoryViewModel ViewModel { get; }

    public async Task OpenAsync(params BatchSummary[] batches)
    {
        Client.Batches = batches;
        ViewModel.OnNavigatedTo();
        await ViewModel.LoadTask;
    }

    public async Task SelectAsync(string batchId)
    {
        ViewModel.SelectedBatch = ViewModel.Batches.First(batch => batch.BatchId == batchId);
        Time.Advance(TimeSpan.FromMilliseconds(200));
        await ViewModel.ResultsTask;
    }

    public async Task<string> AddLocalDocumentAsync(string serverBatchId, string path)
    {
        var document = await Documents.UpsertAsync(
            SourceType.Local,
            SourceKind.Paper,
            path,
            Path.GetFileName(path),
            "hash",
            1,
            TestSupport.Ct);
        var created = await Batches.CreateAsync(SqliteTestDatabase.NewBatch(serverBatchId, document.Id), TestSupport.Ct);
        await Batches.MarkUploadedAsync(created.Id, serverBatchId, TestSupport.Ct);
        return DeterministicIds.DocumentId(serverBatchId, Guid.Parse(document.Id));
    }

    public Task TickAsync() => ViewModel.PollOnceAsync(TestSupport.Ct);

    public static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition was not met in time.");
            await Task.Delay(10, TestSupport.Ct);
        }
    }
}
