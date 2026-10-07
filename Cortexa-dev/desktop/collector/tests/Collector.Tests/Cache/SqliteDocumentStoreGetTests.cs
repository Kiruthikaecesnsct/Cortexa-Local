using Collector.Domain.Enums;
using Collector.Tests.Support;

namespace Collector.Tests.Cache;

public sealed class SqliteDocumentStoreGetTests : IAsyncLifetime
{
    private SqliteTestDatabase _database = null!;

    public async ValueTask InitializeAsync() => _database = await SqliteTestDatabase.CreateAsync();

    public ValueTask DisposeAsync()
    {
        _database.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task GetAsync_ExistingId_ReturnsTheDocument()
    {
        var id = await _database.AddDocumentAsync("C:/repo/a.cs");

        var document = await _database.Documents.GetAsync(id, TestSupport.Ct);

        Assert.Equal(id, document!.Id);
        Assert.Equal("C:/repo/a.cs", document.SourcePath);
        Assert.Equal(SourceKind.Code, document.SourceKind);
    }

    [Fact]
    public async Task GetAsync_UnknownId_ReturnsNull()
    {
        var document = await _database.Documents.GetAsync("does-not-exist", TestSupport.Ct);

        Assert.Null(document);
    }
}
