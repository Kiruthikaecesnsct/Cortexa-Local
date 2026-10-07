using Collector.Application.Ports;
using Collector.Domain.Enums;
using Collector.Domain.Extraction;
using Collector.Domain.Serialization;
using Microsoft.Data.Sqlite;

namespace Collector.Infrastructure.Cache;

public sealed class SqliteUnitStore(SqliteConnectionFactory connections) : IUnitStore
{
    private static readonly string[] InsertParameterNames =
    [
        "$id", "$document_id", "$ordinal", "$unit_kind", "$page_number", "$section_title",
        "$file_path", "$start_line", "$end_line", "$text", "$token_count", "$status",
    ];

    public async Task ReplaceAsync(string documentId, IReadOnlyList<ExtractionUnit> units, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await DeleteByDocumentIdAsync(connection, transaction, documentId, cancellationToken);
        await InsertUnitsAsync(connection, transaction, new UnitBatch(documentId, units), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task DeleteByDocumentIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string documentId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM units WHERE document_id = $document_id;";
        command.Parameters.AddWithValue("$document_id", documentId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertUnitsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        UnitBatch batch,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO units (id, document_id, ordinal, unit_kind, page_number, section_title, file_path, start_line, end_line, text, token_count, status)
            VALUES ($id, $document_id, $ordinal, $unit_kind, $page_number, $section_title, $file_path, $start_line, $end_line, $text, $token_count, $status);
            """;
        var parameters = InsertParameterNames.Select(name => command.Parameters.Add(new SqliteParameter { ParameterName = name })).ToArray();

        foreach (var unit in batch.Units)
        {
            var values = ToRowValues(batch.DocumentId, unit);
            for (var i = 0; i < parameters.Length; i++)
            {
                parameters[i].Value = values[i];
            }

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static object[] ToRowValues(string documentId, ExtractionUnit unit) =>
    [
        unit.Id,
        documentId,
        unit.Ordinal,
        unit.UnitKind.ToWire(),
        unit.PageNumber ?? (object)DBNull.Value,
        unit.SectionTitle ?? (object)DBNull.Value,
        unit.FilePath ?? (object)DBNull.Value,
        unit.StartLine ?? (object)DBNull.Value,
        unit.EndLine ?? (object)DBNull.Value,
        unit.Text,
        unit.TokenCount,
        unit.Status.ToWire(),
    ];

    public async Task<IReadOnlyList<ExtractionUnit>> GetByDocumentIdAsync(string documentId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, document_id, ordinal, unit_kind, page_number, section_title, file_path, start_line, end_line, text, token_count, status
            FROM units
            WHERE document_id = $document_id
            ORDER BY ordinal;
            """;
        command.Parameters.AddWithValue("$document_id", documentId);

        var results = new List<ExtractionUnit>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadUnit(reader));
        }

        return results;
    }

    private static ExtractionUnit ReadUnit(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        DocumentId = reader.GetString(1),
        Ordinal = reader.GetInt32(2),
        UnitKind = EnumWire.FromWire<UnitKind>(reader.GetString(3)),
        PageNumber = reader.IsDBNull(4) ? null : reader.GetInt32(4),
        SectionTitle = reader.IsDBNull(5) ? null : reader.GetString(5),
        FilePath = reader.IsDBNull(6) ? null : reader.GetString(6),
        StartLine = reader.IsDBNull(7) ? null : reader.GetInt32(7),
        EndLine = reader.IsDBNull(8) ? null : reader.GetInt32(8),
        Text = reader.GetString(9),
        TokenCount = reader.GetInt32(10),
        Status = EnumWire.FromWire<DocumentStatus>(reader.GetString(11)),
    };

    private sealed record UnitBatch(string DocumentId, IReadOnlyList<ExtractionUnit> Units);
}
