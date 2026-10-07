using Collector.Domain.Enums;
using Collector.Presentation.Resources;
using Collector.Presentation.ViewModels;

namespace Collector.Tests.Presentation;

public sealed class ReviewFilterTests
{
    private const string FirstDocument = "doc-first";
    private const string SecondDocument = "doc-second";
    private const string MethodTitle = "Method idea";
    private const string SecondMethodTitle = "Another method idea";
    private const string LogicTitle = "Logic idea";
    private const int TotalItems = 3;
    private const int HiddenByLogicFilter = 2;

    private readonly ReviewHarness _harness = new();

    private ReviewViewModel OpenMixed() => _harness.Open(
        ReviewData.Item(MethodTitle, FirstDocument),
        ReviewData.Item(SecondMethodTitle, FirstDocument),
        ReviewData.Item(LogicTitle, SecondDocument, KnowledgeKind.Logic));

    [Fact]
    public void Build_MixedKinds_CreatesChipsOnlyForKindsWithItems()
    {
        var viewModel = OpenMixed();

        Assert.Equal<KnowledgeKind?>([null, KnowledgeKind.Logic, KnowledgeKind.Method], viewModel.Filters.Select(filter => filter.Kind));
    }

    [Fact]
    public void Build_Chips_AllChipIsFirstSelectedAndCountsEverything()
    {
        var viewModel = OpenMixed();

        var all = viewModel.Filters[0];
        Assert.True(all.IsSelected);
        Assert.Equal(ReviewStrings.ChipLabel(ReviewStrings.FilterAll, TotalItems), all.Label);
        Assert.All(viewModel.Filters.Skip(1), filter => Assert.False(filter.IsSelected));
    }

    [Fact]
    public void SelectFilter_Kind_RebuildsVisibleRowsWithHeadersOnlyForDocumentsWithVisibleItems()
    {
        var viewModel = OpenMixed();

        viewModel.SelectFilter(KnowledgeKind.Logic);

        Assert.Equal(2, viewModel.VisibleRows.Count);
        Assert.Equal(SecondDocument, Assert.Single(viewModel.Headers()).DocumentId);
        Assert.Equal(LogicTitle, Assert.Single(viewModel.ItemRows()).Title);
    }

    [Fact]
    public void SelectFilter_SelectedItemStillVisible_KeepsSelection()
    {
        var viewModel = OpenMixed();
        viewModel.SelectedRow = viewModel.Row(SecondMethodTitle);

        viewModel.SelectFilter(KnowledgeKind.Method);

        Assert.Equal(SecondMethodTitle, viewModel.SelectedItem!.Title);
    }

    [Fact]
    public void SelectFilter_SelectedItemHidden_SelectsFirstVisibleItem()
    {
        var viewModel = OpenMixed();
        viewModel.SelectedRow = viewModel.Row(MethodTitle);

        viewModel.SelectFilter(KnowledgeKind.Logic);

        Assert.Equal(LogicTitle, viewModel.SelectedItem!.Title);
    }

    [Fact]
    public void CountLine_FilterHidesItems_ShowsHiddenCount()
    {
        var viewModel = OpenMixed();

        viewModel.SelectFilter(KnowledgeKind.Logic);

        Assert.Equal(HiddenByLogicFilter, viewModel.HiddenCount);
        Assert.Equal(ReviewStrings.CountLine(TotalItems, TotalItems, HiddenByLogicFilter), viewModel.CountLine);
    }

    [Fact]
    public void CountLine_AllFilter_HasNoHiddenSuffix()
    {
        var viewModel = OpenMixed();

        Assert.Equal(ReviewStrings.CountLine(TotalItems, TotalItems, 0), viewModel.CountLine);
    }
}
