using Collector.Application.Ports;
using Collector.Domain.Enums;
using Collector.Domain.Extraction;
using Collector.Domain.Serialization;
using Microsoft.Data.Sqlite;

namespace Collector.Infrastructure.Cache;

public sealed class SqliteUnitStore(SqliteConnectionFactory connections) : IUnitStore
{
    public async Task InsertAsync(string documentId, IReadOnlyList<ExtractionUnit> units, CancellationToken cancellationToken)
    {
        if (units.Count == 0)
        {
            return;
        }

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO units (id, document_id, ordinal, unit_kind, page_number, section_title, file_path, start_line, end_line, text, token_count, status)
            VALUES ($id, $document_id, $ordinal, $unit_kind, $page_number, $section_title, $file_path, $start_line, $end_line, $text, $token_count, $status);
            """;

        var id = command.CreateParameter();
        id.ParameterName = "$id";
        command.Parameters.Add(id);
        var docId = command.CreateParameter();
        docId.ParameterName = "$document_id";
        command.Parameters.Add(docId);
        var ordinal = command.CreateParameter();
        ordinal.ParameterName = "$ordinal";
        command.Parameters.Add(ordinal);
        var unitKind = command.CreateParameter();
        unitKind.ParameterName = "$unit_kind";
        command.Parameters.Add(unitKind);
        var pageNumber = command.CreateParameter();
        pageNumber.ParameterName = "$page_number";
        command.Parameters.Add(pageNumber);
        var sectionTitle = command.CreateParameter();
        sectionTitle.ParameterName = "$section_title";
        command.Parameters.Add(sectionTitle);
        var filePath = command.CreateParameter();
        filePath.ParameterName = "$file_path";
        command.Parameters.Add(filePath);
        var startLine = command.CreateParameter();
        startLine.ParameterName = "$start_line";
        command.Parameters.Add(startLine);
        var endLine = command.CreateParameter();
        endLine.ParameterName = "$end_line";
        command.Parameters.Add(endLine);
        var text = command.CreateParameter();
        text.ParameterName = "$text";
        command.Parameters.Add(text);
        var tokenCount = command.CreateParameter();
        tokenCount.ParameterName = "$token_count";
        command.Parameters.Add(tokenCount);
        var status = command.CreateParameter();
        status.ParameterName = "$status";
        command.Parameters.Add(status);

        foreach (var unit in units)
        {
            id.Value = unit.Id;
            docId.Value = documentId;
            ordinal.Value = unit.Ordinal;
            unitKind.Value = unit.UnitKind.ToWire();
            pageNumber.Value = unit.PageNumber ?? (object)DBNull.Value;
            sectionTitle.Value = unit.SectionTitle ?? (object)DBNull.Value;
            filePath.Value = unit.FilePath ?? (object)DBNull.Value;
            startLine.Value = unit.StartLine ?? (object)DBNull.Value;
            endLine.Value = unit.EndLine ?? (object)DBNull.Value;
            text.Value = unit.Text;
            tokenCount.Value = unit.TokenCount;
            status.Value = unit.Status.ToWire();
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

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
}
