using Collector.Application.Knowledge;
using Collector.Domain.Enums;
using Collector.Tests.Support;

namespace Collector.Tests.Knowledge;

public sealed class HandwrittenResponseTests
{
    private const int ExpectedPaperPage = 4;

    private static List<ExtractedKnowledgeItem> Assemble(string fixtureName, SourceKind kind)
    {
        using var document = KnowledgeFixtures.Load(fixtureName);
        var unit = KnowledgeFixtures.UnitFrom(document.RootElement.GetProperty("unit"));
        var source = TestData.Document(unit.DocumentId, kind);
        var parsed = new KnowledgeParser().Parse(document.RootElement.GetProperty("response").GetRawText());
        var assembler = KnowledgePipeline.Assembler();
        return [.. parsed.Items.Select(raw => assembler.Assemble(unit, source, raw))];
    }

    [Fact]
    public void Assemble_HandwrittenCodeResponse_NoItemIsEchoFlagged()
    {
        var items = Assemble("handwritten-code-response.json", SourceKind.Code);

        Assert.Equal(3, items.Count);
        Assert.All(items, item => Assert.False(item.EchoVerdict.IsEcho));
    }

    [Fact]
    public void Assemble_HandwrittenCodeResponse_AnchorsResolveToExpectedLines()
    {
        var items = Assemble("handwritten-code-response.json", SourceKind.Code);

        Assert.Equal([57, 63, 47], items.Select(item => item.Source.LineStart!.Value));
        Assert.Equal([57, 63, 47], items.Select(item => item.Source.LineEnd!.Value));
    }

    [Fact]
    public void Assemble_HandwrittenPaperResponse_NoItemIsEchoFlaggedAndPageIsFour()
    {
        var items = Assemble("handwritten-paper-response.json", SourceKind.Paper);

        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.False(item.EchoVerdict.IsEcho));
        Assert.All(items, item => Assert.Equal(ExpectedPaperPage, item.Source.PageNumber));
    }
}
