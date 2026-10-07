using System.Text.Encodings.Web;
using System.Text.Json;
using Collector.Application.Ports;
using Collector.Domain.Enums;
using Collector.Domain.Extraction;

namespace Collector.Application.Knowledge;

public sealed class KnowledgePromptBuilder(KnowledgePrompt prompt, UntrustedSourceGuard guard)
{
    public const string WhatText = "Extract the knowledge in this one unit of a source document as typed items.";

    public const string WhyText =
        "A reviewer will upload the approved items to Cortexa. Each item must state the idea itself in plain language.";

    private const string CodeSourceKind = "code";
    private const string PaperSourceKind = "paper";

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    public AiRequest Build(ExtractionUnit unit) => new()
    {
        SystemText = prompt.SystemText,
        UserText = BuildUserText(unit),
        OutputSchemaJson = prompt.SchemaJson,
    };

    public string BuildUserText(ExtractionUnit unit)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("what", WhatText);
            writer.WriteString("why", WhyText);
            WriteHow(writer, unit);
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private void WriteHow(Utf8JsonWriter writer, ExtractionUnit unit)
    {
        writer.WriteStartObject("how");
        writer.WriteString("prompt_version", prompt.Version);
        WriteUnit(writer, unit);
        writer.WriteString("security", guard.SecurityText);
        writer.WriteString("source", guard.Wrap(NeutralizeSource(unit)));
        writer.WriteEndObject();
    }

    private string NeutralizeSource(ExtractionUnit unit) =>
        unit.UnitKind == UnitKind.File ? guard.NeutralizeCode(unit.Text) : guard.NeutralizeProse(unit.Text);

    private void WriteUnit(Utf8JsonWriter writer, ExtractionUnit unit)
    {
        writer.WriteStartObject("unit");
        writer.WriteString("document_id", unit.DocumentId);
        writer.WriteString("source_kind", unit.UnitKind == UnitKind.File ? CodeSourceKind : PaperSourceKind);
        writer.WriteString("unit_kind", unit.UnitKind.ToString().ToLowerInvariant());
        WriteLocation(writer, unit);
        writer.WriteEndObject();
    }

    private void WriteLocation(Utf8JsonWriter writer, ExtractionUnit unit)
    {
        if (unit.UnitKind == UnitKind.File)
        {
            WriteString(writer, "file_path", unit.FilePath);
            WriteNumber(writer, "line_start", unit.StartLine);
            WriteNumber(writer, "line_end", unit.EndLine);
            return;
        }

        WriteNumber(writer, "page_number", unit.PageNumber);
        if (unit.UnitKind == UnitKind.Section)
        {
            WriteString(writer, "section_title", NeutralizeSectionTitle(unit.SectionTitle));
        }
    }

    private string? NeutralizeSectionTitle(string? title) =>
        title is null ? null : UploadFieldClamp.Truncate(guard.NeutralizeProse(title), UploadLimitsMirror.SectionMax);

    private static void WriteString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }

    private static void WriteNumber(Utf8JsonWriter writer, string name, int? value)
    {
        if (value is not null)
        {
            writer.WriteNumber(name, value.Value);
        }
    }
}
