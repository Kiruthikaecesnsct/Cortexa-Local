using Collector.Server.Application.Building;

namespace Collector.Server.Tests.Building;

public class ChunkRowBuilderTests
{
    private const int ItemCount = 3;

    private static IReadOnlyList<Collector.Server.Application.Rows.ChunkRow> BuildThree() =>
        ChunkRowBuilder.Build(
            TestData.BatchId,
            TestData.PaperDocument(TestData.PaperItem(), TestData.PlainItem("B"), TestData.PlainItem("C")),
            TestData.Collector());

    [Fact]
    public void Build_ProducesOneChunkPerItemWithContiguousOrderIndex()
    {
        var chunks = BuildThree();

        Assert.Equal(ItemCount, chunks.Count);
        Assert.Equal(Enumerable.Range(0, ItemCount), chunks.Select(chunk => chunk.OrderIndex));
    }

    [Fact]
    public void Build_UsesDocumentIdAndOrderIndexAsChunkId()
    {
        var chunks = BuildThree();

        Assert.Equal(["doc-1|0", "doc-1|1", "doc-1|2"], chunks.Select(chunk => chunk.Id));
    }

    [Fact]
    public void Build_RunsOffsetsFromPreviousEnd()
    {
        var chunks = BuildThree();

        Assert.Equal(0, chunks[0].StartChar);
        Assert.Equal(chunks[0].Text.Length, chunks[0].EndChar);
        Assert.All(
            chunks.Skip(1).Select((chunk, index) => (chunk, previous: chunks[index])),
            pair => Assert.Equal(pair.previous.EndChar, pair.chunk.StartChar));
        Assert.All(chunks, chunk => Assert.Equal(chunk.StartChar + chunk.Text.Length, chunk.EndChar));
    }

    [Fact]
    public void Build_EstimatesTokensFromText()
    {
        var chunks = BuildThree();

        Assert.All(chunks, chunk => Assert.Equal(TokenEstimator.Estimate(chunk.Text), chunk.TokenCount));
    }

    [Fact]
    public void Build_UsesSectionThenTitleAsSectionHint()
    {
        var chunks = BuildThree();

        Assert.Equal("Methods", chunks[0].SectionHint);
        Assert.Equal("B", chunks[1].SectionHint);
        Assert.Equal(3, chunks[0].PageNumber);
        Assert.Null(chunks[1].PageNumber);
    }

    [Fact]
    public void Build_ForLayerItemWithTitle_LeavesSectionHintNull()
    {
        var document = TestData.CodeDocument(TestData.LayerItem());

        var chunk = Assert.Single(ChunkRowBuilder.Build(TestData.BatchId, document, TestData.Collector()));

        Assert.Null(chunk.SectionHint);
    }

    [Fact]
    public void Build_ForLayerItem_KeepsFolderAsKnowledgeSourceFilePath()
    {
        var document = TestData.CodeDocument(TestData.LayerItem("src/core/"));

        var chunk = Assert.Single(ChunkRowBuilder.Build(TestData.BatchId, document, TestData.Collector()));

        Assert.Equal("src/core/", chunk.Knowledge.Source.FilePath);
    }

    [Fact]
    public void Build_CopiesCollectorMetadataIntoKnowledge()
    {
        var chunk = BuildThree()[0];

        Assert.Equal(TestData.Collector().Provider, chunk.Knowledge.Provider);
        Assert.Equal(TestData.Collector().Model, chunk.Knowledge.Model);
        Assert.Equal(TestData.Collector().PromptVersion, chunk.Knowledge.PromptVersion);
    }

    [Fact]
    public void Build_WithNoItems_ReturnsNoChunks()
    {
        var chunks = ChunkRowBuilder.Build(
            TestData.BatchId,
            TestData.PaperDocument() with { Items = [] },
            TestData.Collector());

        Assert.Empty(chunks);
    }
}
