using Collector.Infrastructure.Cache;
using Collector.Infrastructure.Options;
using Collector.Tests.Support;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Collector.Tests.Cache;

public sealed class SqliteCacheInitializerTests : IDisposable
{
    private const int LatestVersion = 3;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"collector-cache-{Guid.NewGuid():N}");
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteCacheInitializer _initializer;

    public SqliteCacheInitializerTests()
    {
        var options = MsOptions.Create(new CacheOptions { DatabasePath = Path.Combine(_directory, "nested", "cache.db") });
        _factory = new SqliteConnectionFactory(options);
        _initializer = new SqliteCacheInitializer(_factory, new SqliteBatchStore(_factory, TimeProvider.System), NullLogger<SqliteCacheInitializer>.Instance);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private async Task<List<string>> NamesAsync(string type)
    {
        await using var connection = await _factory.OpenAsync(TestSupport.Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = $type AND name NOT LIKE 'sqlite_%' ORDER BY name";
        command.Parameters.AddWithValue("$type", type);
        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(TestSupport.Ct);
        while (await reader.ReadAsync(TestSupport.Ct))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private async Task<long> ScalarAsync(string sql)
    {
        await using var connection = await _factory.OpenAsync(TestSupport.Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(TestSupport.Ct));
    }

    [Fact]
    public async Task Creates_tables_indexes_and_sets_user_version()
    {
        await _initializer.InitializeAsync(TestSupport.Ct);

        Assert.Equal(["batch_documents", "batch_payloads", "batches", "documents", "remote_files", "units"], await NamesAsync("table"));
        var indexes = (await NamesAsync("index")).Where(n => n.StartsWith("ix_", StringComparison.Ordinal)).ToList();
        Assert.Equal(["ix_batches_retryable", "ix_batches_status", "ix_remote_files_local_path", "ix_units_document_id"], indexes);
        Assert.Equal(LatestVersion, await ScalarAsync("PRAGMA user_version"));
        Assert.Equal(1, await ScalarAsync("PRAGMA foreign_keys"));
    }

    [Fact]
    public async Task Second_run_changes_nothing()
    {
        await _initializer.InitializeAsync(TestSupport.Ct);
        var before = await NamesAsync("table");

        await _initializer.InitializeAsync(TestSupport.Ct);

        Assert.Equal(before, await NamesAsync("table"));
        Assert.Equal(LatestVersion, await ScalarAsync("PRAGMA user_version"));
    }

    [Fact]
    public async Task Enforces_foreign_keys_on_units()
    {
        await _initializer.InitializeAsync(TestSupport.Ct);
        await using var connection = await _factory.OpenAsync(TestSupport.Ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO units (id, document_id, ordinal, unit_kind, text, token_count, status) " +
            "VALUES ('u1', 'missing-document', 0, 'page', 'text', 1, 'pending')";

        await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync(TestSupport.Ct));
    }

    [Fact]
    public async Task Cascades_unit_deletes_with_the_document()
    {
        await _initializer.InitializeAsync(TestSupport.Ct);
        await using (var connection = await _factory.OpenAsync(TestSupport.Ct))
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO documents (id, source_type, source_kind, source_path, filename, content_hash, size_bytes, status, created_at, updated_at) " +
                "VALUES ('d1', 'pdf', 'file', 'a.pdf', 'a.pdf', 'h', 1, 'pending', '2026-10-06T00:00:00Z', '2026-10-06T00:00:00Z');" +
                "INSERT INTO units (id, document_id, ordinal, unit_kind, text, token_count, status) VALUES ('u1', 'd1', 0, 'page', 't', 1, 'pending');" +
                "DELETE FROM documents WHERE id = 'd1';";
            await command.ExecuteNonQueryAsync(TestSupport.Ct);
        }

        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM units"));
    }

    [Fact]
    public async Task Upgrades_a_version_one_cache_keeping_existing_batches_without_payloads()
    {
        await SeedVersionOneAsync();

        await _initializer.InitializeAsync(TestSupport.Ct);

        Assert.Equal(LatestVersion, await ScalarAsync("PRAGMA user_version"));
        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM batches WHERE id = 'old' AND replaced_at IS NULL"));
        Assert.Equal(0, await ScalarAsync("SELECT COUNT(*) FROM batch_payloads"));
    }

    [Fact]
    public async Task Marks_uploading_batches_failed_with_network_error_on_startup()
    {
        await _initializer.InitializeAsync(TestSupport.Ct);
        await ExecuteAsync(
            "INSERT INTO batches (id, idempotency_key, batch_name, provider, model, prompt_version, status, created_at, updated_at) " +
            "VALUES ('b1', 'k1', 'n', 'claude', 'm', 'p', 'uploading', '2026-10-06T00:00:00Z', '2026-10-06T00:00:00Z')");

        await _initializer.InitializeAsync(TestSupport.Ct);

        Assert.Equal(1, await ScalarAsync("SELECT COUNT(*) FROM batches WHERE id = 'b1' AND status = 'failed' AND last_error = 'network'"));
    }

    private async Task SeedVersionOneAsync()
    {
        var assembly = typeof(SqliteCacheInitializer).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("V001_Initial.sql", StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        await ExecuteAsync(await reader.ReadToEndAsync(TestSupport.Ct) + "PRAGMA user_version = 1;");
        await ExecuteAsync(
            "INSERT INTO batches (id, idempotency_key, batch_name, provider, model, prompt_version, status, created_at, updated_at) " +
            "VALUES ('old', 'k-old', 'n', 'claude', 'm', 'p', 'failed', '2026-10-06T00:00:00Z', '2026-10-06T00:00:00Z')");
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = await _factory.OpenAsync(TestSupport.Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestSupport.Ct);
    }
}
