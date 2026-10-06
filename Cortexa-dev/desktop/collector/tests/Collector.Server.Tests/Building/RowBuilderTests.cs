using Collector.Domain.Enums;
using Collector.Server.Application.Building;
using Collector.Server.Application.Events;

namespace Collector.Server.Tests.Building;

public class RowBuilderTests
{
    [Fact]
    public void Provenance_ForPaper_UsesChunkOffsetsAsByteRange()
    {
        var document = TestData.PaperDocument();
        var chunks = ChunkRowBuilder.Build(TestData.BatchId, document, TestData.Collector());

        var row = Assert.Single(ProvenanceRowBuilder.Build(chunks, document));

        Assert.Equal(chunks[0].Id, row.Id);
        Assert.Equal(chunks[0].Id, row.ChunkId);
        Assert.Equal([chunks[0].StartChar, chunks[0].EndChar], row.ByteRange);
        Assert.Null(row.FilePath);
        Assert.Null(row.LineRange);
    }

    [Fact]
    public void Provenance_ForCode_UsesFilePathAndLineRange()
    {
        var document = TestData.CodeDocument();
        var chunks = ChunkRowBuilder.Build(TestData.BatchId, document, TestData.Collector());

        var row = Assert.Single(ProvenanceRowBuilder.Build(chunks, document));

        Assert.Equal("src/a.py", row.FilePath);
        Assert.Equal([10, 20], row.LineRange);
        Assert.Null(row.ByteRange);
    }

    [Fact]
    public void Provenance_ForCodeWithoutLocation_FallsBackToFilenameAndFirstLine()
    {
        var document = TestData.CodeDocument(TestData.PlainItem("Loose"));
        var chunks = ChunkRowBuilder.Build(TestData.BatchId, document, TestData.Collector());

        var row = Assert.Single(ProvenanceRowBuilder.Build(chunks, document));

        Assert.Equal(document.Filename, row.FilePath);
        Assert.Equal([1, 1], row.LineRange);
    }

    [Fact]
    public void Provenance_ProducesOneRowPerChunk()
    {
        var document = TestData.PaperDocument(TestData.PaperItem(), TestData.PlainItem("B"));
        var chunks = ChunkRowBuilder.Build(TestData.BatchId, document, TestData.Collector());

        var rows = ProvenanceRowBuilder.Build(chunks, document);

        Assert.Equal(chunks.Select(chunk => chunk.Id), rows.Select(row => row.ChunkId));
        Assert.All(rows, row => Assert.Equal(SourceKind.Paper, row.SourceKind));
    }

    [Fact]
    public void Document_IsCompletedWithEmptyBlobAndChunkCount()
    {
        var input = TestData.PaperDocument(TestData.PaperItem(), TestData.PlainItem("B"));
        var chunks = ChunkRowBuilder.Build(TestData.BatchId, input, TestData.Collector());

        var row = DocumentRowBuilder.Build(TestData.BatchId, input, chunks.Count, TestData.FixedTime);

        Assert.Equal("completed", row.Status);
        Assert.Equal(string.Empty, row.BlobUri);
        Assert.Equal(chunks.Count, row.ChunkCount);
        Assert.Equal("batch-1:doc-1", row.ProvenanceMapId);
    }

    [Fact]
    public void Saga_ListsEveryDocumentQueuedWithEmptyLists()
    {
        var command = TestData.Command(TestData.PaperDocument(), TestData.CodeDocument());

        var saga = SagaRowBuilder.Build(command);

        Assert.Equal("InProgress", saga.State);
        Assert.Equal(2, saga.TotalDocumentCount);
        Assert.Empty(saga.ActiveDocumentIds);
        Assert.Empty(saga.QueuedDocumentIds);
        Assert.All(saga.Documents, progress =>
        {
            Assert.Equal("Queued", progress.State);
            Assert.Empty(progress.CompletedCandidateIds);
            Assert.Empty(progress.ReceivedExtractionUnitIndices);
        });
    }

    [Theory]
    [InlineData("harvesting", true, false)]
    [InlineData("seeding", false, true)]
    [InlineData("dual", true, true)]
    [InlineData("DUAL", true, true)]
    [InlineData("other", false, false)]
    public void Saga_DerivesWantsFlagsFromEngine(string engine, bool harvesting, bool seeding)
    {
        var command = TestData.Command(TestData.PaperDocument()) with { Saga = TestData.SagaMetadata(engine) };

        var saga = SagaRowBuilder.Build(command);

        Assert.Equal(harvesting, saga.WantsHarvesting);
        Assert.Equal(seeding, saga.WantsSeeding);
    }

    [Fact]
    public void Event_CarriesChunkCountOfDocumentRows()
    {
        var input = TestData.PaperDocument(TestData.PaperItem(), TestData.PlainItem("B"));
        var chunks = ChunkRowBuilder.Build(TestData.BatchId, input, TestData.Collector());
        var document = DocumentRowBuilder.Build(TestData.BatchId, input, chunks.Count, TestData.FixedTime);

        var envelope = IngestionCompletedEventBuilder.Build(document, "corr", TestData.FixedTime);

        Assert.Equal(EventTypes.IngestionCompleted, envelope.EventType);
        Assert.Equal(chunks.Count, envelope.Payload.ChunkCount);
        Assert.Equal(document.ProvenanceMapId, envelope.Payload.ProvenanceMapId);
        Assert.Equal(TestData.BatchId, envelope.BatchId);
    }
}
