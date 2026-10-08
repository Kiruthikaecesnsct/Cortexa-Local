using Collector.Domain.Enums;
using Collector.Domain.Knowledge;
using Collector.Domain.Upload;
using Collector.Server.Application.Commands;

namespace Collector.Server.Tests;

internal static class TestData
{
    public const string BatchId = "batch-1";
    public const string PaperDocumentId = "doc-1";
    public const string CodeDocumentId = "doc-2";
    public const string DualEngine = "dual";

    public static readonly DateTimeOffset FixedTime = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    public static CollectorInfo Collector() => new()
    {
        AppVersion = "1.0.0",
        Provider = CollectorProvider.Claude,
        Model = "claude-test-model",
        PromptVersion = "v1"
    };

    public static SagaMetadataInput SagaMetadata(string engine = DualEngine) => new()
    {
        BatchName = "Batch One",
        CreatedAt = FixedTime,
        OrgId = "org-1",
        OwnerUserId = "user-1",
        Engine = engine,
        ExtractionModel = "m-extract",
        PrimaryEvidenceModel = "m-evidence",
        ScoringModel = "m-scoring",
        SeedingModel = "m-seeding",
        SeedingMode = "legacy",
        CollectorProvider = CollectorProvider.Claude,
        CollectorModel = "claude-test-model"
    };

    public static KnowledgeItem PaperItem() => new()
    {
        Kind = KnowledgeKind.Logic,
        UnitKind = UnitKind.Section,
        Title = "Spectral Method",
        Summary = "Summary text",
        Details = "Detail text",
        Excerpt = "Key excerpt",
        Source = new KnowledgeSource { PageNumber = 3, Section = "Methods" }
    };

    public static KnowledgeItem PlainItem(string title) => new()
    {
        Kind = KnowledgeKind.Method,
        UnitKind = UnitKind.Module,
        Title = title,
        Summary = "Sum"
    };

    public static KnowledgeItem CodeItem() => new()
    {
        Kind = KnowledgeKind.Method,
        UnitKind = UnitKind.File,
        Title = "Parse",
        Summary = "Sum",
        Source = new KnowledgeSource { FilePath = "src/a.py", LineStart = 10, LineEnd = 20 }
    };

    public static KnowledgeItem LayerItem(string folderPath = "src/core/") => new()
    {
        Kind = KnowledgeKind.Layer,
        UnitKind = UnitKind.Module,
        Title = "Core Layer",
        Summary = "Sum",
        Source = new KnowledgeSource { FilePath = folderPath }
    };

    public static BatchDocumentInput PaperDocument(params KnowledgeItem[] items) => new()
    {
        DocumentId = PaperDocumentId,
        Filename = "paper.pdf",
        SourceKind = SourceKind.Paper,
        Items = items.Length == 0 ? [PaperItem()] : items
    };

    public static BatchDocumentInput CodeDocument(params KnowledgeItem[] items) => new()
    {
        DocumentId = CodeDocumentId,
        Filename = "a.py",
        SourceKind = SourceKind.Code,
        Items = items.Length == 0 ? [CodeItem()] : items
    };

    public static WriteKnowledgeBatchCommand Command(string batchId, params BatchDocumentInput[] documents) => new()
    {
        BatchId = batchId,
        Saga = SagaMetadata(),
        Collector = Collector(),
        Documents = documents
    };

    public static WriteKnowledgeBatchCommand Command(params BatchDocumentInput[] documents) =>
        Command(BatchId, documents);
}
