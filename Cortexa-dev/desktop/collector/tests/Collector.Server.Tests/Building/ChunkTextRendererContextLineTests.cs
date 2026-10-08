using Collector.Server.Application.Building;

namespace Collector.Server.Tests.Building;

public class ChunkTextRendererContextLineTests
{
    [Fact]
    public void Render_WithPageNumber_PrefixesPageContextLine()
    {
        var item = TestData.PaperItem();

        var text = ChunkTextRenderer.Render(item);

        Assert.StartsWith("Structured knowledge summary from page 3\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WithFilePathAndLines_PrefixesFileContextLine()
    {
        var item = TestData.CodeItem();

        var text = ChunkTextRenderer.Render(item);

        Assert.StartsWith(
            "Structured knowledge summary from file src/a.py (lines 10-20)\n",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WithPageAndFileBothPresent_PrefersPage()
    {
        var item = TestData.PaperItem() with
        {
            Source = TestData.PaperItem().Source with { FilePath = "src/a.py", LineStart = 1, LineEnd = 2 }
        };

        var text = ChunkTextRenderer.Render(item);

        Assert.StartsWith("Structured knowledge summary from page 3\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WithNeitherPageNorFile_OmitsContextLine()
    {
        var item = TestData.PlainItem("Parse");

        var text = ChunkTextRenderer.Render(item);

        Assert.DoesNotContain("Structured knowledge summary", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_WithFilePathButNoLineNumbers_DefaultsToLineOne()
    {
        var item = TestData.CodeItem() with
        {
            Source = TestData.CodeItem().Source with { LineStart = null, LineEnd = null }
        };

        var text = ChunkTextRenderer.Render(item);

        Assert.StartsWith(
            "Structured knowledge summary from file src/a.py (lines 1-1)\n",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ForLayerItem_PrefixesFolderContextLine()
    {
        var item = TestData.LayerItem("src/core/");

        var text = ChunkTextRenderer.Render(item);

        Assert.StartsWith("Structured knowledge summary for folder src/core/\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("lines", text.Split('\n')[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ForLayerItemWithPageNumber_PrefersPageContextLine()
    {
        var item = TestData.LayerItem() with { Source = new() { FilePath = "src/core/", PageNumber = 3 } };

        var text = ChunkTextRenderer.Render(item);

        Assert.StartsWith("Structured knowledge summary from page 3\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ForLayerItemWithoutFolder_OmitsContextLine()
    {
        var item = TestData.LayerItem() with { Source = new() };

        var text = ChunkTextRenderer.Render(item);

        Assert.DoesNotContain("Structured knowledge summary", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_KeepsOffsetsConsistentWithContextLinePrefixedText()
    {
        var document = TestData.PaperDocument();

        var chunks = ChunkRowBuilder.Build(TestData.BatchId, document, TestData.Collector());

        var chunk = Assert.Single(chunks);
        Assert.Equal(0, chunk.StartChar);
        Assert.Equal(chunk.Text.Length, chunk.EndChar);
        Assert.StartsWith("Structured knowledge summary from page", chunk.Text, StringComparison.Ordinal);
    }
}
