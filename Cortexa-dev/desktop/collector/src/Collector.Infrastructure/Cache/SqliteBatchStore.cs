using Collector.Application.Ports;
using Collector.Domain.Enums;
using Collector.Domain.Serialization;
using Microsoft.Data.Sqlite;

namespace Collector.Infrastructure.Cache;

public sealed class SqliteBatchStore(SqliteConnectionFactory connections, TimeProvider timeProvider) : IBatchStore
{
    private const string BatchColumns = "b.id, b.idempotency_key, b.batch_name, b.status, b.server_batch_id, b.last_error";

    public async Task<StoredBatch> CreateAsync(NewBatch batch, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("n");
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await InsertBatchAsync(connection, transaction, id, batch, cancellationToken);
        await InsertDocumentsAsync(connection, transaction, id, batch.DocumentIds, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new StoredBatch
        {
            Id = id,
            IdempotencyKey = batch.IdempotencyKey,
            BatchName = batch.BatchName,
            Status = BatchStatus.Draft,
            DocumentIds = batch.DocumentIds,
        };
    }

    public async Task<StoredBatch?> FindByDocumentsAsync(
        IReadOnlyCollection<string> documentIds,
        CancellationToken cancellationToken)
    {
        if (documentIds.Count == 0)
        {
            return null;
        }

        await using var connection = await connections.OpenAsync(cancellationToken);
        var row = await ReadMatchingRowAsync(connection, documentIds, cancellationToken);
        if (row is null)
        {
            return null;
        }

        return row with { DocumentIds = await ReadDocumentIdsAsync(connection, row.Id, cancellationToken) };
    }

    public async Task<StoredBatch?> FindByServerBatchIdAsync(string serverBatchId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var row = await ReadByServerBatchIdAsync(connection, serverBatchId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        return row with { DocumentIds = await ReadDocumentIdsAsync(connection, row.Id, cancellationToken) };
    }

    public Task MarkUploadingAsync(string batchId, CancellationToken cancellationToken) =>
        UpdateAsync(new BatchUpdate(batchId, BatchStatus.Uploading, null, null), cancellationToken);

    public Task MarkUploadedAsync(string batchId, string serverBatchId, CancellationToken cancellationToken) =>
        UpdateAsync(new BatchUpdate(batchId, BatchStatus.Uploaded, serverBatchId, null), cancellationToken);

    public Task MarkFailedAsync(string batchId, string error, CancellationToken cancellationToken) =>
        UpdateAsync(new BatchUpdate(batchId, BatchStatus.Failed, null, error), cancellationToken);

    private async Task InsertBatchAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string id,
        NewBatch batch,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO batches (id, idempotency_key, batch_name, provider, model, prompt_version, status, created_at, updated_at)
            VALUES ($id, $idempotency_key, $batch_name, $provider, $model, $prompt_version, $status, $now, $now);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$idempotency_key", batch.IdempotencyKey);
        command.Parameters.AddWithValue("$batch_name", batch.BatchName);
        command.Parameters.AddWithValue("$provider", batch.Provider.ToWire());
        command.Parameters.AddWithValue("$model", batch.Model);
        command.Parameters.AddWithValue("$prompt_version", batch.PromptVersion);
        command.Parameters.AddWithValue("$status", BatchStatus.Draft.ToWire());
        command.Parameters.AddWithValue("$now", timeProvider.GetUtcNow().ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertDocumentsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string batchId,
        IReadOnlyList<string> documentIds,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO batch_documents (batch_id, document_id) VALUES ($batch_id, $document_id);";
        command.Parameters.AddWithValue("$batch_id", batchId);
        var documentParameter = command.Parameters.Add("$document_id", SqliteType.Text);
        foreach (var documentId in documentIds)
        {
            documentParameter.Value = documentId;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<StoredBatch?> ReadMatchingRowAsync(
        SqliteConnection connection,
        IReadOnlyCollection<string> documentIds,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var names = documentIds.Select((id, index) => AddParameter(command, $"$d{index}", id)).ToList();
        command.Parameters.AddWithValue("$count", documentIds.Count);
        command.CommandText =
            $"""
            SELECT {BatchColumns} FROM batches b
            WHERE (SELECT COUNT(*) FROM batch_documents d WHERE d.batch_id = b.id) = $count
              AND (SELECT COUNT(*) FROM batch_documents d WHERE d.batch_id = b.id AND d.document_id IN ({string.Join(", ", names)})) = $count
            ORDER BY b.created_at DESC LIMIT 1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRow(reader) : null;
    }

    private static async Task<StoredBatch?> ReadByServerBatchIdAsync(
        SqliteConnection connection,
        string serverBatchId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT {BatchColumns} FROM batches b
            WHERE b.server_batch_id = $server_batch_id
            ORDER BY b.created_at DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$server_batch_id", serverBatchId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRow(reader) : null;
    }

    private static string AddParameter(SqliteCommand command, string name, string value)
    {
        command.Parameters.AddWithValue(name, value);
        return name;
    }

    private static async Task<IReadOnlyList<string>> ReadDocumentIdsAsync(
        SqliteConnection connection,
        string batchId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT document_id FROM batch_documents WHERE batch_id = $batch_id ORDER BY document_id;";
        command.Parameters.AddWithValue("$batch_id", batchId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var ids = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    private async Task UpdateAsync(BatchUpdate update, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE batches SET status = $status,
                server_batch_id = COALESCE($server_batch_id, server_batch_id),
                last_error = $last_error,
                updated_at = $updated_at
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$status", update.Status.ToWire());
        command.Parameters.AddWithValue("$server_batch_id", (object?)update.ServerBatchId ?? DBNull.Value);
        command.Parameters.AddWithValue("$last_error", (object?)update.LastError ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated_at", timeProvider.GetUtcNow().ToString("O"));
        command.Parameters.AddWithValue("$id", update.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static StoredBatch ReadRow(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        IdempotencyKey = reader.GetString(1),
        BatchName = reader.GetString(2),
        Status = EnumWire.FromWire<BatchStatus>(reader.GetString(3)),
        ServerBatchId = reader.IsDBNull(4) ? null : reader.GetString(4),
        LastError = reader.IsDBNull(5) ? null : reader.GetString(5),
        DocumentIds = [],
    };

    private sealed record BatchUpdate(string Id, BatchStatus Status, string? ServerBatchId, string? LastError);
}
