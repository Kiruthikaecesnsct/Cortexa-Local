using Collector.Application.Ports;
using Collector.Domain.Documents;
using Collector.Domain.Enums;
using Collector.Domain.Serialization;
using Microsoft.Data.Sqlite;

namespace Collector.Infrastructure.Cache;

public sealed class SqliteDocumentStore(SqliteConnectionFactory connections, TimeProvider timeProvider) : IDocumentStore
{
    public async Task<CollectorDocument> UpsertAsync(
        SourceType sourceType,
        SourceKind sourceKind,
        string sourcePath,
        string filename,
        string contentHash,
        long sizeBytes,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO documents (id, source_type, source_kind, source_path, filename, content_hash, size_bytes, status, created_at, updated_at)
            VALUES ($id, $source_type, $source_kind, $source_path, $filename, $content_hash, $size_bytes, $status, $created_at, $updated_at)
            ON CONFLICT (source_type, source_path) DO UPDATE SET
                filename = excluded.filename,
                content_hash = excluded.content_hash,
                size_bytes = excluded.size_bytes,
                updated_at = excluded.updated_at
            RETURNING id, source_type, source_kind, source_path, filename, content_hash, size_bytes, status, created_at, updated_at;
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("n"));
        command.Parameters.AddWithValue("$source_type", sourceType.ToWire());
        command.Parameters.AddWithValue("$source_kind", sourceKind.ToWire());
        command.Parameters.AddWithValue("$source_path", sourcePath);
        command.Parameters.AddWithValue("$filename", filename);
        command.Parameters.AddWithValue("$content_hash", contentHash);
        command.Parameters.AddWithValue("$size_bytes", sizeBytes);
        command.Parameters.AddWithValue("$status", DocumentStatus.Pending.ToWire());
        command.Parameters.AddWithValue("$created_at", now.ToString("O"));
        command.Parameters.AddWithValue("$updated_at", now.ToString("O"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return ReadDocument(reader);
    }

    public async Task<CollectorDocument?> GetAsync(string documentId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, source_type, source_kind, source_path, filename, content_hash, size_bytes, status, created_at, updated_at
            FROM documents WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", documentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadDocument(reader) : null;
    }

    public async Task UpdateStatusAsync(string documentId, DocumentStatus status, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE documents SET status = $status, updated_at = $updated_at WHERE id = $id;";
        command.Parameters.AddWithValue("$status", status.ToWire());
        command.Parameters.AddWithValue("$updated_at", timeProvider.GetUtcNow().ToString("O"));
        command.Parameters.AddWithValue("$id", documentId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static CollectorDocument ReadDocument(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        SourceType = EnumWire.FromWire<SourceType>(reader.GetString(1)),
        SourceKind = EnumWire.FromWire<SourceKind>(reader.GetString(2)),
        SourcePath = reader.GetString(3),
        Filename = reader.GetString(4),
        ContentHash = reader.GetString(5),
        SizeBytes = reader.GetInt64(6),
        Status = EnumWire.FromWire<DocumentStatus>(reader.GetString(7)),
        CreatedAt = DateTimeOffset.Parse(reader.GetString(8)),
        UpdatedAt = DateTimeOffset.Parse(reader.GetString(9)),
    };
}
