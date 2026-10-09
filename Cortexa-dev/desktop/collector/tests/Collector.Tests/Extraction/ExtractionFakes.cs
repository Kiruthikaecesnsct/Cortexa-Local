using Collector.Application.Extraction;
using Collector.Application.Ports;
using Collector.Domain.Documents;
using Collector.Domain.Enums;
using Collector.Domain.Extraction;

namespace Collector.Tests.Extraction;

internal sealed class FakePdfTextExtractor : IPdfTextExtractor
{
    public ParsedDocument Result { get; set; } = new() { Text = string.Empty };

    public Exception? Failure { get; set; }

    public TaskCompletionSource? Gate { get; set; }

    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Active => Volatile.Read(ref _active);

    public int MaxActive => Volatile.Read(ref _maxActive);

    private int _active;
    private int _maxActive;

    public async Task<ParsedDocument> ExtractAsync(string filePath, CancellationToken cancellationToken)
    {
        TrackEntry();
        try
        {
            if (Gate is not null)
            {
                await Gate.Task.WaitAsync(cancellationToken);
            }

            return Failure is null ? Result : throw Failure;
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }
    }

    private void TrackEntry()
    {
        var active = Interlocked.Increment(ref _active);
        int seen;
        do
        {
            seen = Volatile.Read(ref _maxActive);
        }
        while (active > seen && Interlocked.CompareExchange(ref _maxActive, active, seen) != seen);
        Entered.TrySetResult();
    }
}

internal sealed class FakeDocxTextExtractor : IDocxTextExtractor
{
    public ParsedDocument Result { get; set; } = new() { Text = string.Empty };

    public Task<ParsedDocument> ExtractAsync(string filePath, CancellationToken cancellationToken) =>
        Task.FromResult(Result);
}

internal sealed class WordCountTokenCounter : ITokenCounter
{
    public IReadOnlyList<int> Encode(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select((_, i) => i).ToList();

    public string Decode(IReadOnlyList<int> tokens) => string.Join(' ', tokens);

    public int Count(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
}

internal sealed class InMemoryDocumentStore : IDocumentStore
{
    public Dictionary<string, CollectorDocument> ByPath { get; } = [];

    public List<(string Id, DocumentStatus Status)> StatusHistory { get; } = [];

    public DocumentStatus? ThrowOnStatus { get; set; }

    public CancellationToken? LastStatusToken { get; private set; }

    public Task<CollectorDocument> UpsertAsync(
        SourceType sourceType,
        SourceKind sourceKind,
        string sourcePath,
        string filename,
        string contentHash,
        long sizeBytes,
        CancellationToken cancellationToken)
    {
        lock (ByPath)
        {
            return Task.FromResult(UpsertLocked(sourceType, sourceKind, sourcePath, filename, contentHash, sizeBytes));
        }
    }

    private CollectorDocument UpsertLocked(
        SourceType sourceType,
        SourceKind sourceKind,
        string sourcePath,
        string filename,
        string contentHash,
        long sizeBytes)
    {
        if (!ByPath.TryGetValue(sourcePath, out var document))
        {
            document = new CollectorDocument
            {
                Id = Guid.NewGuid().ToString("n"),
                SourceType = sourceType,
                SourceKind = sourceKind,
                SourcePath = sourcePath,
                Filename = filename,
                ContentHash = contentHash,
                SizeBytes = sizeBytes,
                Status = DocumentStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            ByPath[sourcePath] = document;
        }

        return document;
    }

    public Task<CollectorDocument?> GetAsync(string documentId, CancellationToken cancellationToken) =>
        Task.FromResult(ByPath.Values.FirstOrDefault(d => d.Id == documentId));

    public Task UpdateStatusAsync(string documentId, DocumentStatus status, CancellationToken cancellationToken)
    {
        LastStatusToken = cancellationToken;
        if (status == ThrowOnStatus)
        {
            return Task.FromException(new InvalidOperationException("Status write failed."));
        }

        lock (ByPath)
        {
            StatusHistory.Add((documentId, status));
            var entry = ByPath.Values.FirstOrDefault(d => d.Id == documentId);
            if (entry is not null)
            {
                ByPath[entry.SourcePath] = entry with { Status = status };
            }
        }

        return Task.CompletedTask;
    }
}

internal sealed class InMemoryUnitStore : IUnitStore
{
    public Dictionary<string, List<ExtractionUnit>> ByDocumentId { get; } = [];

    public Task ReplaceAsync(string documentId, IReadOnlyList<ExtractionUnit> units, CancellationToken cancellationToken)
    {
        lock (ByDocumentId)
        {
            ByDocumentId[documentId] = units.ToList();
        }

        return Task.CompletedTask;
    }

    public Dictionary<string, TaskCompletionSource> Gates { get; } = [];

    public async Task<IReadOnlyList<ExtractionUnit>> GetByDocumentIdAsync(string documentId, CancellationToken cancellationToken)
    {
        if (Gates.TryGetValue(documentId, out var gate))
        {
            await gate.Task;
        }

        return ByDocumentId.TryGetValue(documentId, out var units) ? units : [];
    }
}
