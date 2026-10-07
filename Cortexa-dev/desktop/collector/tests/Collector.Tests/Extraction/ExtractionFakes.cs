using Collector.Application.Extraction;
using Collector.Application.Ports;
using Collector.Domain.Documents;
using Collector.Domain.Enums;
using Collector.Domain.Extraction;

namespace Collector.Tests.Extraction;

internal sealed class FakePdfTextExtractor : IPdfTextExtractor
{
    public ParsedDocument Result { get; set; } = new() { Text = string.Empty };

    public Task<ParsedDocument> ExtractAsync(string filePath, CancellationToken cancellationToken) =>
        Task.FromResult(Result);
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

    public Task<CollectorDocument> UpsertAsync(
        SourceType sourceType,
        SourceKind sourceKind,
        string sourcePath,
        string filename,
        string contentHash,
        long sizeBytes,
        CancellationToken cancellationToken)
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

        return Task.FromResult(document);
    }

    public Task UpdateStatusAsync(string documentId, DocumentStatus status, CancellationToken cancellationToken)
    {
        StatusHistory.Add((documentId, status));
        var entry = ByPath.Values.FirstOrDefault(d => d.Id == documentId);
        if (entry is not null)
        {
            ByPath[entry.SourcePath] = entry with { Status = status };
        }

        return Task.CompletedTask;
    }
}

internal sealed class InMemoryUnitStore : IUnitStore
{
    public Dictionary<string, List<ExtractionUnit>> ByDocumentId { get; } = [];

    public Task InsertAsync(string documentId, IReadOnlyList<ExtractionUnit> units, CancellationToken cancellationToken)
    {
        ByDocumentId[documentId] = units.ToList();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ExtractionUnit>> GetByDocumentIdAsync(string documentId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ExtractionUnit>>(ByDocumentId.TryGetValue(documentId, out var units) ? units : []);
}
