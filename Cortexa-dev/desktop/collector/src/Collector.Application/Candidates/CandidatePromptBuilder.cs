using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Collector.Application.Knowledge;
using Collector.Application.Ports;

namespace Collector.Application.Candidates;

public sealed class CandidatePromptBuilder(CandidatePrompt prompt, UntrustedSourceGuard guard)
{
    public const string WhatText = "Find the patentable invention candidates in this one whole document.";

    public const string WhyText =
        "A reviewer will look at the candidates before anything leaves the machine. Each candidate must state the idea itself in plain language.";

    private const string CodeSourceKind = "code";
    private const string PaperSourceKind = "paper";

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    public AiRequest Build(WholeDocument document) => new()
    {
        SystemText = prompt.SystemText,
        UserText = BuildUserText(document),
        OutputSchemaJson = prompt.SchemaJson,
        Model = null,
    };

    private string BuildUserText(WholeDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("what", WhatText);
            writer.WriteString("why", WhyText);
            WriteHow(writer, document);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private void WriteHow(Utf8JsonWriter writer, WholeDocument document)
    {
        writer.WriteStartObject("how");
        writer.WriteString("prompt_version", prompt.Version);
        writer.WriteStartObject("document");
        writer.WriteString("document_id", document.DocumentId);
        writer.WriteString("source_kind", document.IsCode ? CodeSourceKind : PaperSourceKind);
        writer.WriteEndObject();
        writer.WriteString("security", guard.SecurityText);
        writer.WriteString("source", guard.Wrap(Neutralize(document)));
        writer.WriteEndObject();
    }

    private string Neutralize(WholeDocument document) =>
        document.IsCode ? guard.NeutralizeCode(document.Text) : guard.NeutralizeProse(document.Text);
}
