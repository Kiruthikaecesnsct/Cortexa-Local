using Collector.Application.Ports;
using Collector.Domain.Enums;
using Collector.Domain.Remote;
using Collector.Domain.Serialization;
using Microsoft.Data.Sqlite;

namespace Collector.Infrastructure.Cache;

public sealed class SqliteRemoteFileStore(SqliteConnectionFactory connections) : IRemoteFileStore
{
    private const string Columns =
        "id, provider, repo_url, repo_key, branch, commit_sha, path, blob_sha, size_bytes, local_path, fetched_at";

    private const string KeyFilter =
        "provider = $provider AND repo_key = $repo_key AND COALESCE(branch, '') = COALESCE($branch, '')";

    public async Task UpsertAsync(RemoteFileRecord record, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            INSERT INTO remote_files ({Columns})
            VALUES ($id, $provider, $repo_url, $repo_key, $branch, $commit_sha, $path, $blob_sha, $size_bytes, $local_path, $fetched_at)
            ON CONFLICT (provider, repo_key, COALESCE(branch, ''), path) DO UPDATE SET
                repo_url = excluded.repo_url,
                commit_sha = excluded.commit_sha,
                blob_sha = excluded.blob_sha,
                size_bytes = excluded.size_bytes,
                local_path = excluded.local_path,
                fetched_at = excluded.fetched_at;
            """;
        command.Parameters.AddWithValue("$id", record.Id);
        command.Parameters.AddWithValue("$provider", record.Provider.ToWire());
        command.Parameters.AddWithValue("$repo_url", record.RepoUrl);
        command.Parameters.AddWithValue("$repo_key", record.RepoKey);
        command.Parameters.AddWithValue("$branch", (object?)record.Branch ?? DBNull.Value);
        command.Parameters.AddWithValue("$commit_sha", (object?)record.CommitSha ?? DBNull.Value);
        command.Parameters.AddWithValue("$path", record.Path);
        command.Parameters.AddWithValue("$blob_sha", (object?)record.BlobSha ?? DBNull.Value);
        command.Parameters.AddWithValue("$size_bytes", record.SizeBytes);
        command.Parameters.AddWithValue("$local_path", record.LocalPath);
        command.Parameters.AddWithValue("$fetched_at", record.FetchedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<RemoteFileRecord?> GetAsync(
        SourceType provider,
        string repoKey,
        string? branch,
        string path,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM remote_files WHERE {KeyFilter} AND path = $path;";
        AddKey(command, provider, repoKey, branch);
        command.Parameters.AddWithValue("$path", path);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRecord(reader) : null;
    }

    public async Task<IReadOnlyList<RemoteFileRecord>> ListAsync(
        SourceType provider,
        string repoKey,
        string? branch,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM remote_files WHERE {KeyFilter} ORDER BY path;";
        AddKey(command, provider, repoKey, branch);
        var records = new List<RemoteFileRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(ReadRecord(reader));
        }

        return records;
    }

    public async Task DeleteAsync(
        SourceType provider,
        string repoKey,
        string? branch,
        string path,
        CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM remote_files WHERE {KeyFilter} AND path = $path;";
        AddKey(command, provider, repoKey, branch);
        command.Parameters.AddWithValue("$path", path);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddKey(SqliteCommand command, SourceType provider, string repoKey, string? branch)
    {
        command.Parameters.AddWithValue("$provider", provider.ToWire());
        command.Parameters.AddWithValue("$repo_key", repoKey);
        command.Parameters.AddWithValue("$branch", (object?)branch ?? DBNull.Value);
    }

    private static RemoteFileRecord ReadRecord(SqliteDataReader reader) => new(
        reader.GetString(0),
        EnumWire.FromWire<SourceType>(reader.GetString(1)),
        reader.GetString(2),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        reader.IsDBNull(5) ? null : reader.GetString(5),
        reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetString(7),
        reader.GetInt64(8),
        reader.GetString(9),
        DateTimeOffset.Parse(reader.GetString(10)));
}
