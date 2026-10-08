using Collector.Application.Knowledge;
using Collector.Domain.Enums;
using Collector.Domain.Knowledge;
using Collector.Tests.Support;

namespace Collector.Tests.Knowledge;

public sealed class LayerItemAssemblerTests
{
    private readonly LayerItemAssembler _assembler = new(LayerPipeline.Prompt);

    private static FolderGroup Group(string folder = "src/app") => new(
        folder,
        [new FolderFile($"{folder}/a.cs", [TestData.Item(documentId: "doc-rep") with
        {
            Source = new KnowledgeSource { FilePath = $"{folder}/a.cs", LineStart = 1, LineEnd = 2 },
        }])]);

    private static RawKnowledgeItem Raw(string title = "Domain model", string? details = null) => new()
    {
        Kind = KnowledgeKind.Layer,
        Title = title,
        Summary = "A module summary.",
        Details = details,
    };

    [Fact]
    public void Assemble_Always_SetsModuleAndLayerKind()
    {
        var item = _assembler.Assemble(Group(), Raw());

        Assert.Equal(UnitKind.Module, item.UnitKind);
        Assert.Equal(KnowledgeKind.Layer, item.Kind);
    }

    [Fact]
    public void Assemble_Always_SourceIsFolderPathWithTrailingSlashAndNullLines()
    {
        var item = _assembler.Assemble(Group("src/app"), Raw());

        Assert.Equal("src/app/", item.Source.FilePath);
        Assert.Null(item.Source.LineStart);
        Assert.Null(item.Source.LineEnd);
    }

    [Fact]
    public void Assemble_Always_BorrowsRepresentativeDocumentIdentity()
    {
        var item = _assembler.Assemble(Group(), Raw());

        Assert.Equal("doc-rep", item.DocumentId);
    }

    [Fact]
    public void Assemble_Always_StampsLayerPromptVersion()
    {
        var item = _assembler.Assemble(Group(), Raw());

        Assert.Equal(LayerPipeline.Prompt.Version, item.PromptVersion);
    }

    [Fact]
    public void Assemble_Always_HasBenignEchoVerdictAndNoExcerpt()
    {
        var item = _assembler.Assemble(Group(), Raw());

        Assert.False(item.EchoVerdict.IsEcho);
        Assert.Null(item.Excerpt);
    }
}
