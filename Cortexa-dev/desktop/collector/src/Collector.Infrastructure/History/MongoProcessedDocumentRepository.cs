using Collector.Application.Candidates;
using Collector.Application.History;
using Collector.Application.Ports;
using Collector.Application.Secrets;
using Collector.Domain.Enums;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Collector.Infrastructure.History;

public sealed class MongoProcessedDocumentRepository(
    ISecretStore secrets,
    IOptions<MongoHistoryOptions> options) : IProcessedDocumentRepository
{
    private const string NotConfigured = "The MongoDB connection string is not set. Add it in Settings.";
    private const string UriVariable = "MONGODB_URI";
    private const string Unreachable = "History storage could not be reached.";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IMongoCollection<ProcessedDocumentDocument>? _collection;
    private string? _uri;

    public async Task<IReadOnlyList<ProcessedDocumentRecord>> ListAsync(string userId, CancellationToken cancellationToken)
    {
        var collection = await CollectionAsync(cancellationToken);
        var found = await Guarded(async () =>
            await collection.Find(document => document.UserId == userId)
                .SortByDescending(document => document.UpdatedAt)
                .ToListAsync(cancellationToken));
        return [.. found.Select(document => document.TryToRecord()).OfType<ProcessedDocumentRecord>()];
    }

    public async Task UpsertAsync(ProcessedDocumentRecord record, CancellationToken cancellationToken)
    {
        var collection = await CollectionAsync(cancellationToken);
        var document = ProcessedDocumentDocument.From(record);
        await Guarded(async () =>
            await collection.ReplaceOneAsync(
                existing => existing.Id == document.Id,
                document,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken));
    }

    private static async Task<T> Guarded<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception exception) when (exception is MongoException or TimeoutException or BsonException or FormatException)
        {
            throw new ProcessedDocumentStoreException(Unreachable, exception);
        }
    }

    private async Task<IMongoCollection<ProcessedDocumentDocument>> CollectionAsync(CancellationToken cancellationToken)
    {
        var uri = await secrets.ReadAsync(SecretSlot.MongoConnectionString, cancellationToken)
            ?? Environment.GetEnvironmentVariable(UriVariable);
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new ProcessedDocumentStoreException(NotConfigured);
        }

        if (_collection is not null && uri == _uri)
        {
            return _collection;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_collection is null || uri != _uri)
            {
                _collection = await CreateCollectionAsync(uri, cancellationToken);
                _uri = uri;
            }

            return _collection;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IMongoCollection<ProcessedDocumentDocument>> CreateCollectionAsync(string uri, CancellationToken cancellationToken)
    {
        var collection = new MongoClient(ClientSettings(uri))
            .GetDatabase(options.Value.Database)
            .GetCollection<ProcessedDocumentDocument>(options.Value.Collection);
        await EnsureIndexAsync(collection, cancellationToken);
        return collection;
    }

    private MongoClientSettings ClientSettings(string uri)
    {
        try
        {
            var settings = MongoClientSettings.FromConnectionString(uri);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(options.Value.ServerSelectionTimeoutSeconds);
            settings.SocketTimeout = TimeSpan.FromSeconds(options.Value.OperationTimeoutSeconds);
            return settings;
        }
        catch (Exception exception) when (exception is MongoConfigurationException or FormatException or ArgumentException)
        {
            throw new ProcessedDocumentStoreException(NotConfigured, exception);
        }
    }

    private static Task EnsureIndexAsync(IMongoCollection<ProcessedDocumentDocument> collection, CancellationToken cancellationToken)
    {
        var keys = Builders<ProcessedDocumentDocument>.IndexKeys.Ascending(document => document.UserId).Descending(document => document.UpdatedAt);
        return Guarded(async () =>
        {
            await collection.Indexes.CreateOneAsync(new CreateIndexModel<ProcessedDocumentDocument>(keys), cancellationToken: cancellationToken);
            return true;
        });
    }
}

internal sealed class ProcessedDocumentDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string DocumentId { get; set; } = string.Empty;

    public string Filename { get; set; } = string.Empty;

    public string SourceType { get; set; } = string.Empty;

    public string SourceKind { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string ContentHash { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Outcome { get; set; } = string.Empty;

    public string? FailureReason { get; set; }

    public List<CandidateDocument> Candidates { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public static ProcessedDocumentDocument From(ProcessedDocumentRecord record) => new()
    {
        Id = $"{record.UserId}|{record.DocumentId}",
        UserId = record.UserId,
        DocumentId = record.DocumentId,
        Filename = record.Filename,
        SourceType = record.SourceType.ToString(),
        SourceKind = record.SourceKind.ToString(),
        SourcePath = record.SourcePath,
        SizeBytes = record.SizeBytes,
        ContentHash = record.ContentHash,
        Provider = record.Provider,
        Model = record.Model,
        Status = record.Status.ToString(),
        Outcome = record.Outcome.ToString(),
        FailureReason = record.FailureReason,
        Candidates = [.. record.Candidates.Select(CandidateDocument.From)],
        CreatedAt = record.CreatedAt.UtcDateTime,
        UpdatedAt = record.UpdatedAt.UtcDateTime,
    };

    public ProcessedDocumentRecord? TryToRecord() =>
        Enum.TryParse<SourceType>(SourceType, out var sourceType)
        && Enum.TryParse<ProcessedStatus>(Status, out var status)
        && Enum.TryParse<CandidateOutcome>(Outcome, out var outcome)
            ? ToRecord(sourceType, status, outcome)
            : null;

    private ProcessedDocumentRecord ToRecord(SourceType sourceType, ProcessedStatus status, CandidateOutcome outcome) => new()
    {
        UserId = UserId,
        DocumentId = DocumentId,
        Filename = Filename,
        SourceType = sourceType,
        SourceKind = Enum.Parse<SourceKind>(SourceKind),
        SourcePath = SourcePath,
        SizeBytes = SizeBytes,
        ContentHash = ContentHash,
        Provider = Provider,
        Model = Model,
        Status = status,
        Outcome = outcome,
        FailureReason = FailureReason,
        Candidates = [.. Candidates.Select(candidate => candidate.ToCandidate())],
        CreatedAt = new DateTimeOffset(DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc)),
        UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(UpdatedAt, DateTimeKind.Utc)),
    };
}

internal sealed class CandidateDocument
{
    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string PatentabilityRationale { get; set; } = string.Empty;

    public string SourceFile { get; set; } = string.Empty;

    public string? ExcerptQuote { get; set; }

    public bool ExcerptVerified { get; set; }

    public static CandidateDocument From(InventionCandidate candidate) => new()
    {
        Title = candidate.Title,
        Summary = candidate.Summary,
        PatentabilityRationale = candidate.PatentabilityRationale,
        SourceFile = candidate.SourceFile,
        ExcerptQuote = candidate.Excerpt?.Quote,
        ExcerptVerified = candidate.Excerpt?.Verified ?? false,
    };

    public InventionCandidate ToCandidate() => new(
        Title,
        Summary,
        PatentabilityRationale,
        SourceFile,
        ExcerptQuote is null ? null : new CandidateExcerpt(ExcerptQuote, ExcerptVerified, null, null));
}
