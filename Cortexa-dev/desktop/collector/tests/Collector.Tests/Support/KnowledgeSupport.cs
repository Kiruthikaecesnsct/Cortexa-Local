using System.Text.Json;
using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Collector.Domain.Documents;
using Collector.Domain.Enums;
using Collector.Domain.Extraction;
using Collector.Domain.Knowledge;
using Microsoft.Extensions.Logging;

namespace Collector.Tests.Support;

internal static class KnowledgePipeline
{
    public static KnowledgePrompt Prompt { get; } = KnowledgePromptLoader.Load();

    public static UntrustedSourceGuard Guard() => new(Prompt);

    public static KnowledgePromptBuilder Builder() => new(Prompt, Guard());

    public static UnitItemAssembler Assembler() => new(new AnchorLocator(), new ExcerptCutter(), new IdentifierEchoDetector());

    public static ExtractionRunContext DefaultContext { get; } = new(CollectorProvider.Claude, null);

    public static UnitExtractionRunner Runner(IAiProvider provider, ILogger<UnitExtractionRunner> logger) =>
        new(new SingleProviderFactory(provider), Builder(), new KnowledgeParser(), Assembler(), new UnitSplitter(), logger);
}

internal sealed class SingleProviderFactory(IAiProvider provider, int concurrency = 1) : IAiProviderFactory
{
    public IAiProvider Resolve(CollectorProvider _) => provider;

    public int ConcurrencyFor(CollectorProvider _) => concurrency;
}

internal static class KnowledgeFixtures
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Knowledge", "Fixtures");

    public static JsonDocument Load(string fileName) => JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder, fileName)));

    public static ExtractionUnit UnitFrom(JsonElement unit)
    {
        var unitKind = Enum.Parse<UnitKind>(unit.GetProperty("unit_kind").GetString()!, ignoreCase: true);
        return new ExtractionUnit
        {
            Id = unit.GetProperty("id").GetString()!,
            DocumentId = unit.GetProperty("document_id").GetString()!,
            Ordinal = unit.GetProperty("ordinal").GetInt32(),
            UnitKind = unitKind,
            PageNumber = OptionalInt(unit, "page_number"),
            FilePath = unit.TryGetProperty("file_path", out var path) ? path.GetString() : null,
            StartLine = OptionalInt(unit, "start_line"),
            EndLine = OptionalInt(unit, "end_line"),
            Text = unit.GetProperty("text").GetString()!,
            TokenCount = TestData.DefaultTokenCount,
            Status = DocumentStatus.Pending,
        };
    }

    private static int? OptionalInt(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) ? value.GetInt32() : null;
}

internal static class TestData
{
    public const string DocumentId = "doc-1";
    public const string FilePath = "src/Sample.cs";
    public const int DefaultTokenCount = 100;
    public const int DefaultStartLine = 10;
    public const int DefaultPage = 3;

    public static ExtractionUnit FileUnit(string text, int startLine = DefaultStartLine, string id = "unit-1", string documentId = DocumentId) =>
        new()
        {
            Id = id,
            DocumentId = documentId,
            Ordinal = 0,
            UnitKind = UnitKind.File,
            FilePath = FilePath,
            StartLine = startLine,
            EndLine = startLine + text.Count(character => character == '\n'),
            Text = text,
            TokenCount = DefaultTokenCount,
            Status = DocumentStatus.Pending,
        };

    public static ExtractionUnit PageUnit(string text, int? page = DefaultPage, string id = "unit-1", string documentId = DocumentId) =>
        new()
        {
            Id = id,
            DocumentId = documentId,
            Ordinal = 0,
            UnitKind = UnitKind.Page,
            PageNumber = page,
            Text = text,
            TokenCount = DefaultTokenCount,
            Status = DocumentStatus.Pending,
        };

    public static ExtractionUnit SectionUnit(string text, string? title, int? page = DefaultPage, string id = "unit-1") =>
        new()
        {
            Id = id,
            DocumentId = DocumentId,
            Ordinal = 0,
            UnitKind = UnitKind.Section,
            PageNumber = page,
            SectionTitle = title,
            Text = text,
            TokenCount = DefaultTokenCount,
            Status = DocumentStatus.Pending,
        };

    public static CollectorDocument Document(string id = DocumentId, SourceKind kind = SourceKind.Code, string filename = "Sample.cs") =>
        new()
        {
            Id = id,
            SourceType = SourceType.Local,
            SourceKind = kind,
            SourcePath = $"C:/repo/{id}/{filename}",
            Filename = filename,
            ContentHash = "hash",
            SizeBytes = DefaultTokenCount,
            Status = DocumentStatus.Extracted,
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch,
        };

    public static ExtractedKnowledgeItem Item(
        string title = "Retry with backoff",
        string summary = "A short summary.",
        string documentId = DocumentId,
        KnowledgeKind kind = KnowledgeKind.Method) =>
        new()
        {
            DocumentId = documentId,
            DocumentName = "Sample.cs",
            DocumentPath = $"C:/repo/{documentId}/Sample.cs",
            UnitKind = UnitKind.File,
            Kind = kind,
            Title = title,
            Summary = summary,
            Source = new KnowledgeSource { FilePath = FilePath, LineStart = DefaultStartLine, LineEnd = DefaultStartLine },
            EchoVerdict = EchoVerdict.Clean,
        };

    public static string ItemJson(string title, string summary = "A plain summary of the idea.", string kind = "method", string? anchorQuote = null) =>
        JsonSerializer.Serialize(new
        {
            kind,
            title,
            summary,
            details = string.Empty,
            anchor_quote = anchorQuote,
        });

    public static string Response(params string[] itemJson) => $"{{\"items\":[{string.Join(',', itemJson)}]}}";
}

internal static class Completions
{
    public const string Model = "claude-test";

    public static AiCompletion Completed(string text) => new() { Text = text, Outcome = AiOutcome.Completed, Model = Model };

    public static AiCompletion Truncated() => new() { Text = string.Empty, Outcome = AiOutcome.Truncated, Model = Model };

    public static AiCompletion Refused() => new() { Text = string.Empty, Outcome = AiOutcome.Refused, Model = Model };
}

internal sealed class ScriptedAiProvider : IAiProvider
{
    private readonly Queue<object> _script = new();
    private readonly object _gate = new();

    public List<AiRequest> Requests { get; } = [];

    public ScriptedAiProvider Then(AiCompletion completion)
    {
        _script.Enqueue(completion);
        return this;
    }

    public ScriptedAiProvider Throw(Exception exception)
    {
        _script.Enqueue(exception);
        return this;
    }

    public Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Requests.Add(request);
            return _script.Dequeue() switch
            {
                Exception exception => Task.FromException<AiCompletion>(exception),
                AiCompletion completion => Task.FromResult(completion),
                _ => throw new InvalidOperationException("Unsupported script entry."),
            };
        }
    }
}

internal sealed class FuncAiProvider(Func<AiRequest, CancellationToken, Task<AiCompletion>> handler) : IAiProvider
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return handler(request, cancellationToken);
    }
}

internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<string> _entries = [];

    public IReadOnlyList<string> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var pairs = state is IEnumerable<KeyValuePair<string, object?>> values
            ? string.Join('|', values.Select(pair => $"{pair.Key}={pair.Value}"))
            : string.Empty;
        lock (_entries)
        {
            _entries.Add($"{logLevel}|{formatter(state, exception)}|{pairs}|{exception}");
        }
    }
}

internal sealed class RecordingProgress<T> : IProgress<T>
{
    private readonly List<T> _reports = [];

    public IReadOnlyList<T> Reports
    {
        get
        {
            lock (_reports)
            {
                return [.. _reports];
            }
        }
    }

    public void Report(T value)
    {
        lock (_reports)
        {
            _reports.Add(value);
        }
    }
}
