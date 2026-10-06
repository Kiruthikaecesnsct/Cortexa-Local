using Collector.Domain.Enums;
using Collector.Domain.Knowledge;
using Collector.Domain.Upload;

namespace Collector.Server.Tests.Upload;

internal static class UploadRequests
{
    public const string BatchName = "Quarterly Batch";

    public static Guid ClientId(int number) => new(number, 0, 0, new byte[8]);

    public static KnowledgeUploadRequest Valid(params UploadDocument[] documents) => new()
    {
        BatchName = BatchName,
        Collector = TestData.Collector(),
        Documents = documents.Length == 0 ? [PaperDocument()] : documents
    };

    public static KnowledgeUploadRequest WithItem(SourceKind sourceKind, KnowledgeItem item) =>
        Valid(Document(1, sourceKind, "doc.bin", item));

    public static UploadDocument PaperDocument(params KnowledgeItem[] items) =>
        Document(1, SourceKind.Paper, "paper.pdf", items.Length == 0 ? [PaperSection()] : items);

    public static UploadDocument CodeDocument(params KnowledgeItem[] items) =>
        Document(2, SourceKind.Code, "a.py", items.Length == 0 ? [CodeFile()] : items);

    public static UploadDocument Document(int number, SourceKind sourceKind, string filename, params KnowledgeItem[] items) => new()
    {
        ClientDocumentId = ClientId(number).ToString(),
        Filename = filename,
        SourceKind = sourceKind,
        SourceType = SourceType.Local,
        KnowledgeItems = items
    };

    public static KnowledgeItem PaperSection() => Item(KnowledgeKind.Logic, UnitKind.Section);

    public static KnowledgeItem CodeFile() => Item(KnowledgeKind.Method, UnitKind.File);

    public static KnowledgeItem Item(KnowledgeKind kind, UnitKind unitKind) => new()
    {
        Kind = kind,
        UnitKind = unitKind,
        Title = "Spectral Method",
        Summary = "Summary text",
        Details = "Detail text",
        Excerpt = "Key excerpt",
        Source = new KnowledgeSource
        {
            PageNumber = 1,
            Section = "Methods",
            FilePath = "src/a.py",
            LineStart = 1,
            LineEnd = 2
        }
    };
}
