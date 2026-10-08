using System.Reflection;
using System.Text.RegularExpressions;
using Collector.Application.Ports;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Collector.Infrastructure.Cache;

public sealed partial class SqliteCacheInitializer(
    SqliteConnectionFactory connections,
    IBatchStore batchStore,
    ILogger<SqliteCacheInitializer> logger) : ILocalCacheInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken);
        var current = await ReadVersionAsync(connection, cancellationToken);
        foreach (var script in LoadScripts().Where(s => s.Version > current))
        {
            await ApplyAsync(connection, script, cancellationToken);
            logger.LogInformation("Applied cache schema version {Version}.", script.Version);
        }

        await batchStore.MarkInterruptedAsync(cancellationToken);
    }

    private static async Task ApplyAsync(SqliteConnection connection, SchemaScript script, CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await ExecuteAsync(connection, script.Sql, cancellationToken, transaction);
        await ExecuteAsync(connection, $"PRAGMA user_version = {script.Version};", cancellationToken, transaction);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<int> ReadVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IEnumerable<SchemaScript> LoadScripts()
    {
        var assembly = typeof(SqliteCacheInitializer).Assembly;
        return assembly.GetManifestResourceNames()
            .Select(name => (Name: name, Match: ScriptName().Match(name)))
            .Where(item => item.Match.Success)
            .Select(item => new SchemaScript(int.Parse(item.Match.Groups[1].Value), ReadResource(assembly, item.Name)))
            .OrderBy(script => script.Version);
    }

    private static string ReadResource(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"\.Schema\.V(\d+)_[^.]+\.sql$")]
    private static partial Regex ScriptName();

    private sealed record SchemaScript(int Version, string Sql);
}
