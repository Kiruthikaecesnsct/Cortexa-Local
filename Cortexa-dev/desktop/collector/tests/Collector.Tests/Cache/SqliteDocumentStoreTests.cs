using Collector.Domain.Enums;
using Collector.Infrastructure.Cache;
using Collector.Infrastructure.Options;
using Collector.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Collector.Tests.Cache;

public sealed class SqliteDocumentStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"collector-docstore-{Guid.NewGuid():N}");
    private readonly SqliteConnectionFactory _factory;
    private readonly FakeTimeProvider _timeProvider = new(DateTimeOffset.Parse("2026-10-06T00:00:00Z"));
    private readonly SqliteDocumentStore _store;

    public SqliteDocumentStoreTests()
    {
        var options = MsOptions.Create(new CacheOptions { DatabasePath = Path.Combine(_directory, "cache.db") });
        _factory = new SqliteConnectionFactory(options);
        _store = new SqliteDocumentStore(_factory, _timeProvider);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private async Task InitializeSchemaAsync()
    {
        var initializer = new SqliteCacheInitializer(_factory, NullLogger<SqliteCacheInitializer>.Instance);
        await initializer.InitializeAsync(TestSupport.Ct);
    }

    [Fact]
    public async Task Inserts_a_new_document_as_pending()
    {
        await InitializeSchemaAsync();

        var document = await _store.UpsertAsync(
            SourceType.Local,
            SourceKind.Paper,
            "C:/docs/a.pdf",
            "a.pdf",
            "hash-1",
            1024,
            TestSupport.Ct);

        Assert.Equal(SourceType.Local, document.SourceType);
        Assert.Equal(SourceKind.Paper, document.SourceKind);
        Assert.Equal("C:/docs/a.pdf", document.SourcePath);
        Assert.Equal(DocumentStatus.Pending, document.Status);
        Assert.Equal(1024, document.SizeBytes);
    }

    [Fact]
    public async Task Upserting_the_same_source_path_resolves_the_existing_document()
    {
        await InitializeSchemaAsync();

        var first = await _store.UpsertAsync(
            SourceType.Local, SourceKind.Paper, "C:/docs/a.pdf", "a.pdf", "hash-1", 1024, TestSupport.Ct);
        var second = await _store.UpsertAsync(
            SourceType.Local, SourceKind.Paper, "C:/docs/a.pdf", "a.pdf", "hash-2", 2048, TestSupport.Ct);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("hash-2", second.ContentHash);
        Assert.Equal(2048, second.SizeBytes);
    }

    [Fact]
    public async Task Updates_status_for_an_existing_document()
    {
        await InitializeSchemaAsync();
        var document = await _store.UpsertAsync(
            SourceType.Local, SourceKind.Code, "C:/repo/a.cs", "a.cs", "hash-1", 512, TestSupport.Ct);

        await _store.UpdateStatusAsync(document.Id, DocumentStatus.Extracting, TestSupport.Ct);

        await using var connection = await _factory.OpenAsync(TestSupport.Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT status FROM documents WHERE id = $id";
        command.Parameters.AddWithValue("$id", document.Id);
        var status = (string)(await command.ExecuteScalarAsync(TestSupport.Ct))!;
        Assert.Equal("extracting", status);
    }
}
