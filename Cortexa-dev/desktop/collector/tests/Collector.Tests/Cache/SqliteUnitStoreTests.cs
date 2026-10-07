using Collector.Domain.Enums;
using Collector.Domain.Extraction;
using Collector.Infrastructure.Cache;
using Collector.Infrastructure.Options;
using Collector.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Collector.Tests.Cache;

public sealed class SqliteUnitStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"collector-unitstore-{Guid.NewGuid():N}");
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteUnitStore _store;
    private readonly SqliteDocumentStore _documentStore;

    public SqliteUnitStoreTests()
    {
        var options = MsOptions.Create(new CacheOptions { DatabasePath = Path.Combine(_directory, "cache.db") });
        _factory = new SqliteConnectionFactory(options);
        _store = new SqliteUnitStore(_factory);
        _documentStore = new SqliteDocumentStore(_factory, new FakeTimeProvider(DateTimeOffset.Parse("2026-10-06T00:00:00Z")));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private async Task<string> SeedDocumentAsync()
    {
        var initializer = new SqliteCacheInitializer(_factory, NullLogger<SqliteCacheInitializer>.Instance);
        await initializer.InitializeAsync(TestSupport.Ct);
        var document = await _documentStore.UpsertAsync(
            SourceType.Local, SourceKind.Paper, "C:/docs/a.pdf", "a.pdf", "hash-1", 1024, TestSupport.Ct);
        return document.Id;
    }

    [Fact]
    public async Task Persists_units_with_document_id_kind_metadata_and_token_count()
    {
        var documentId = await SeedDocumentAsync();
        var units = new List<ExtractionUnit>
        {
            new()
            {
                Id = "u1",
                DocumentId = documentId,
                Ordinal = 0,
                UnitKind = UnitKind.Page,
                PageNumber = 1,
                Text = "page one text",
                TokenCount = 3,
                Status = DocumentStatus.Extracted,
            },
            new()
            {
                Id = "u2",
                DocumentId = documentId,
                Ordinal = 1,
                UnitKind = UnitKind.Page,
                PageNumber = 2,
                Text = "page two text",
                TokenCount = 4,
                Status = DocumentStatus.Extracted,
            },
        };

        await _store.InsertAsync(documentId, units, TestSupport.Ct);
        var persisted = await _store.GetByDocumentIdAsync(documentId, TestSupport.Ct);

        Assert.Equal(2, persisted.Count);
        Assert.All(persisted, u => Assert.Equal(documentId, u.DocumentId));
        Assert.Equal(UnitKind.Page, persisted[0].UnitKind);
        Assert.Equal(1, persisted[0].PageNumber);
        Assert.Equal(3, persisted[0].TokenCount);
        Assert.Equal(DocumentStatus.Extracted, persisted[0].Status);
        Assert.Equal(0, persisted[0].Ordinal);
        Assert.Equal(1, persisted[1].Ordinal);
    }

    [Fact]
    public async Task Persists_module_units_with_file_path_and_line_ranges()
    {
        var documentId = await SeedDocumentAsync();
        var units = new List<ExtractionUnit>
        {
            new()
            {
                Id = "u1",
                DocumentId = documentId,
                Ordinal = 0,
                UnitKind = UnitKind.Module,
                FilePath = "src/Foo.cs",
                StartLine = 1,
                EndLine = 10,
                Text = "public void Foo() {}",
                TokenCount = 5,
                Status = DocumentStatus.Extracted,
            },
        };

        await _store.InsertAsync(documentId, units, TestSupport.Ct);
        var persisted = await _store.GetByDocumentIdAsync(documentId, TestSupport.Ct);

        Assert.Single(persisted);
        Assert.Equal("src/Foo.cs", persisted[0].FilePath);
        Assert.Equal(1, persisted[0].StartLine);
        Assert.Equal(10, persisted[0].EndLine);
    }

    [Fact]
    public async Task Returns_empty_list_when_document_has_no_units()
    {
        var documentId = await SeedDocumentAsync();

        var persisted = await _store.GetByDocumentIdAsync(documentId, TestSupport.Ct);

        Assert.Empty(persisted);
    }
}
