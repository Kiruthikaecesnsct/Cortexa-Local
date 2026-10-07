using Collector.Application.Ports;
using Collector.Infrastructure.Cache;
using Collector.Infrastructure.Options;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Collector.Tests.Support;

internal sealed class SqliteTestDatabase : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"collector-test-{Guid.NewGuid():N}");

    public SqliteTestDatabase()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new CacheOptions { DatabasePath = Path.Combine(_directory, "cache.db") });
        Connections = new SqliteConnectionFactory(options);
        Time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-07T00:00:00Z"));
        Documents = new SqliteDocumentStore(Connections, Time);
        Batches = new SqliteBatchStore(Connections, Time);
    }

    public SqliteConnectionFactory Connections { get; }

    public FakeTimeProvider Time { get; }

    public SqliteDocumentStore Documents { get; }

    public SqliteBatchStore Batches { get; }

    public static async Task<SqliteTestDatabase> CreateAsync()
    {
        var database = new SqliteTestDatabase();
        var initializer = new SqliteCacheInitializer(database.Connections, NullLogger<SqliteCacheInitializer>.Instance);
        await initializer.InitializeAsync(TestSupport.Ct);
        return database;
    }

    public async Task<string> AddDocumentAsync(string path) =>
        (await Documents.UpsertAsync(
            Collector.Domain.Enums.SourceType.Local,
            Collector.Domain.Enums.SourceKind.Code,
            path,
            Path.GetFileName(path),
            "hash",
            1,
            TestSupport.Ct)).Id;

    public static NewBatch NewBatch(string key, params string[] documentIds) => new()
    {
        IdempotencyKey = key,
        BatchName = $"Batch {key}",
        Provider = Collector.Domain.Enums.CollectorProvider.Claude,
        Model = "claude-test",
        PromptVersion = "knowledge.v1",
        DocumentIds = documentIds,
    };

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
