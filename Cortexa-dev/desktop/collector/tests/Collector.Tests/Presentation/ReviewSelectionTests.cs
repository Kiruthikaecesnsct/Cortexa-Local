using Collector.Application.Knowledge;
using Collector.Domain.Enums;

namespace Collector.Tests.Presentation;

public sealed class ReviewSelectionTests
{
    private const string CleanTitle = "Clean idea";
    private const string EchoTitle = "Echo idea";
    private const string LogicTitle = "Logic idea";
    private const string SecondEchoTitle = "Second echo";
    private const string SmallDocumentId = "doc-small";
    private const string BigDocumentId = "doc-big";
    private const int SmallDocumentItems = 2;
    private const int OverLimitItems = UploadLimitsMirror.MaxItemsPerDocument + 1;

    private readonly ReviewHarness _harness = new();

    private ExtractedKnowledgeItem[] SmallAndBigDocuments() =>
        [.. ReviewData.Items(SmallDocumentId, SmallDocumentItems), .. ReviewData.Items(BigDocumentId, OverLimitItems)];

    [Fact]
    public void Load_EchoFlaggedItem_StartsExcluded()
    {
        var viewModel = _harness.Open(ReviewData.Item(CleanTitle), ReviewData.Item(EchoTitle, echo: true));

        Assert.False(viewModel.Row(EchoTitle).IsIncluded);
    }

    [Fact]
    public void Load_NonEchoItem_StartsIncluded()
    {
        var viewModel = _harness.Open(ReviewData.Item(CleanTitle), ReviewData.Item(EchoTitle, echo: true));

        Assert.True(viewModel.Row(CleanTitle).IsIncluded);
    }

    [Fact]
    public void IncludeAll_AfterExcludingEverything_SkipsEchoItems()
    {
        var viewModel = _harness.Open(ReviewData.Item(CleanTitle), ReviewData.Item(EchoTitle, echo: true));
        viewModel.ExcludeAllCommand.Execute(null);

        viewModel.IncludeAllCommand.Execute(null);

        Assert.True(viewModel.Row(CleanTitle).IsIncluded);
        Assert.False(viewModel.Row(EchoTitle).IsIncluded);
    }

    [Fact]
    public void IncludeAll_KindFilterActive_ChangesVisibleRowsOnly()
    {
        var viewModel = _harness.Open(ReviewData.Item(CleanTitle), ReviewData.Item(LogicTitle, kind: KnowledgeKind.Logic));
        var logicRow = viewModel.Row(LogicTitle);
        viewModel.ExcludeAllCommand.Execute(null);
        viewModel.SelectFilter(KnowledgeKind.Method);

        viewModel.IncludeAllCommand.Execute(null);

        Assert.True(viewModel.Row(CleanTitle).IsIncluded);
        Assert.False(logicRow.IsIncluded);
    }

    [Fact]
    public void ExcludeAll_KindFilterActive_ExcludesVisibleEchoesAndLeavesHiddenRows()
    {
        var viewModel = _harness.Open(
            ReviewData.Item(CleanTitle),
            ReviewData.Item(EchoTitle, echo: true),
            ReviewData.Item(LogicTitle, kind: KnowledgeKind.Logic));
        var logicRow = viewModel.Row(LogicTitle);
        viewModel.Row(EchoTitle).IsIncluded = true;
        viewModel.SelectFilter(KnowledgeKind.Method);

        viewModel.ExcludeAllCommand.Execute(null);

        Assert.False(viewModel.Row(CleanTitle).IsIncluded);
        Assert.False(viewModel.Row(EchoTitle).IsIncluded);
        Assert.True(logicRow.IsIncluded);
    }

    [Fact]
    public void CheckState_AllVisibleIncluded_IsTrue()
    {
        var viewModel = _harness.Open(ReviewData.Item(CleanTitle), ReviewData.Item(LogicTitle));

        Assert.True(viewModel.Headers().Single().CheckState);
    }

    [Fact]
    public void CheckState_NoVisibleIncluded_IsFalse()
    {
        var viewModel = _harness.Open(ReviewData.Item(CleanTitle), ReviewData.Item(LogicTitle));

        viewModel.ExcludeAllCommand.Execute(null);

        Assert.False(viewModel.Headers().Single().CheckState);
    }

    [Fact]
    public void CheckState_SomeVisibleIncluded_IsNull()
    {
        var viewModel = _harness.Open(ReviewData.Item(CleanTitle), ReviewData.Item(EchoTitle, echo: true));

        Assert.Null(viewModel.Headers().Single().CheckState);
    }

    [Fact]
    public void ToggleCommand_SomeIncluded_IncludesAllRowsIncludingEchoes()
    {
        var viewModel = _harness.Open(ReviewData.Item(CleanTitle), ReviewData.Item(EchoTitle, echo: true));

        viewModel.Headers().Single().ToggleCommand.Execute(null);

        Assert.True(viewModel.Row(EchoTitle).IsIncluded);
        Assert.True(viewModel.Row(CleanTitle).IsIncluded);
    }

    [Fact]
    public void ToggleCommand_NoneIncluded_IncludesAllRowsIncludingEchoes()
    {
        var viewModel = _harness.Open(ReviewData.Item(CleanTitle), ReviewData.Item(EchoTitle, echo: true));
        viewModel.ExcludeAllCommand.Execute(null);

        viewModel.Headers().Single().ToggleCommand.Execute(null);

        Assert.All(viewModel.ItemRows(), row => Assert.True(row.IsIncluded));
    }

    [Fact]
    public void ToggleCommand_AllIncluded_ExcludesAllRows()
    {
        var viewModel = _harness.Open(ReviewData.Item(CleanTitle), ReviewData.Item(LogicTitle));

        viewModel.Headers().Single().ToggleCommand.Execute(null);

        Assert.All(viewModel.ItemRows(), row => Assert.False(row.IsIncluded));
    }

    [Fact]
    public void ToggleCommand_KindFilterActive_ChangesVisibleRowsOnly()
    {
        var viewModel = _harness.Open(
            ReviewData.Item(CleanTitle),
            ReviewData.Item(EchoTitle, echo: true),
            ReviewData.Item(LogicTitle, kind: KnowledgeKind.Logic, echo: true),
            ReviewData.Item(SecondEchoTitle, kind: KnowledgeKind.Logic, echo: true));
        var hiddenRow = viewModel.Row(LogicTitle);
        viewModel.SelectFilter(KnowledgeKind.Method);

        viewModel.Headers().Single().ToggleCommand.Execute(null);

        Assert.True(viewModel.Row(EchoTitle).IsIncluded);
        Assert.False(hiddenRow.IsIncluded);
    }

    [Fact]
    public void Load_MoreThanLimitIncludedInOneDocument_BlocksTheDocument()
    {
        var viewModel = _harness.Open(SmallAndBigDocuments());

        var big = viewModel.Headers().Single(header => header.DocumentId == BigDocumentId);
        Assert.True(big.IsBlocked);
        Assert.All(big.Rows, row => Assert.True(row.IsDocumentBlocked));
        Assert.Equal(1, viewModel.BlockedDocumentCount);
    }

    [Fact]
    public void UploadableCount_BlockedDocument_ExcludesItsItems()
    {
        var viewModel = _harness.Open(SmallAndBigDocuments());

        Assert.Equal(SmallDocumentItems, viewModel.UploadableCount);
    }

    [Fact]
    public void ExcludingItems_BlockedDocumentDropsToLimit_UnblocksIt()
    {
        var viewModel = _harness.Open(SmallAndBigDocuments());
        var big = viewModel.Headers().Single(header => header.DocumentId == BigDocumentId);

        big.Rows[0].IsIncluded = false;

        Assert.False(big.IsBlocked);
        Assert.All(big.Rows, row => Assert.False(row.IsDocumentBlocked));
        Assert.Equal(0, viewModel.BlockedDocumentCount);
        Assert.Equal(SmallDocumentItems + UploadLimitsMirror.MaxItemsPerDocument, viewModel.UploadableCount);
    }
}
