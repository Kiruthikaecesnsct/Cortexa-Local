using System.Text.Encodings.Web;
using System.Text.Json;
using Collector.Application.Ports;

namespace Collector.Application.Knowledge;

public sealed class LayerPromptBuilder(LayerPrompt prompt, UntrustedSourceGuard guard, ITokenCounter tokenCounter)
{
    public const string WhatText =
        "Name the architectural role of this one folder (module), as typed items of kind layer.";

    public const string WhyText =
        "After the file-level pass, the collector lifts per-file knowledge to module-level insight for folders with 3 or more files.";

    private const int MaxFiles = 50;
    private const int MaxInputTokens = 16000;
    private const int MinFilesAfterCeiling = 1;

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    public AiRequest Build(FolderGroup group) => new()
    {
        SystemText = prompt.SystemText,
        UserText = BuildUserText(group),
        OutputSchemaJson = prompt.SchemaJson,
    };

    public string BuildUserText(FolderGroup group)
    {
        var ordered = group.Files.OrderBy(file => file.FilePath, StringComparer.Ordinal).ToArray();
        var totalFileCount = ordered.Length;
        var included = ordered.Length > MaxFiles ? ordered[..MaxFiles] : ordered;
        var truncated = included.Length < totalFileCount;

        while (included.Length > MinFilesAfterCeiling
            && tokenCounter.Count(Render(group.FolderPath, included, totalFileCount, true)) > MaxInputTokens)
        {
            included = included[..^1];
            truncated = true;
        }

        return Render(group.FolderPath, included, totalFileCount, truncated);
    }

    private string Render(string folderPath, IReadOnlyList<FolderFile> included, int totalFileCount, bool truncated)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("what", WhatText);
            writer.WriteString("why", WhyText);
            WriteHow(writer, folderPath, included, totalFileCount, truncated);
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private void WriteHow(
        Utf8JsonWriter writer,
        string folderPath,
        IReadOnlyList<FolderFile> included,
        int totalFileCount,
        bool truncated)
    {
        writer.WriteStartObject("how");
        writer.WriteString("prompt_version", prompt.Version);
        WriteFolder(writer, folderPath, included.Count, totalFileCount, truncated);
        writer.WriteString("security", guard.SecurityText);
        writer.WriteString("source", guard.Wrap(BuildSourceBlock(included)));
        writer.WriteEndObject();
    }

    private static void WriteFolder(
        Utf8JsonWriter writer,
        string folderPath,
        int fileCount,
        int totalFileCount,
        bool truncated)
    {
        writer.WriteStartObject("folder");
        writer.WriteString("path", folderPath);
        writer.WriteNumber("file_count", fileCount);
        writer.WriteNumber("total_file_count", totalFileCount);
        writer.WriteBoolean("files_truncated", truncated);
        writer.WriteEndObject();
    }

    private string BuildSourceBlock(IReadOnlyList<FolderFile> files) =>
        string.Join("\n\n", files.Select(BuildFileBlock));

    private string BuildFileBlock(FolderFile file)
    {
        var path = guard.NeutralizeProse(UploadFieldClamp.Truncate(file.FilePath, UploadLimitsMirror.FilePathMax));
        var title = guard.NeutralizeProse(UploadFieldClamp.Truncate(CombinedTitle(file), UploadLimitsMirror.TitleMax));
        var summary = guard.NeutralizeProse(UploadFieldClamp.Truncate(CombinedSummary(file), UploadLimitsMirror.SummaryMax));
        return $"FILE: {path}\nTITLE: {title}\nSUMMARY: {summary}";
    }

    private static string CombinedTitle(FolderFile file) =>
        string.Join("; ", file.Items.Select(item => item.Title).Distinct(StringComparer.Ordinal));

    private static string CombinedSummary(FolderFile file) =>
        string.Join(" ", file.Items.Select(item => item.Summary).Distinct(StringComparer.Ordinal));
}
