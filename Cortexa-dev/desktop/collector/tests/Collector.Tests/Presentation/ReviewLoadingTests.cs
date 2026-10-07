using Collector.Presentation.Navigation;
using Collector.Presentation.Resources;
using Collector.Presentation.ViewModels;

namespace Collector.Tests.Presentation;

public sealed class ReviewLoadingTests
{
    private const string Title = "Idea";
    private const int FailedUnits = 1;
    private const int SkippedUnits = 2;

    private readonly ReviewHarness _harness = new();

    [Fact]
    public void OnNavigatedTo_NoRun_ShowsNoRunEmptyCard()
    {
        _harness.ViewModel.OnNavigatedTo();

        Assert.True(_harness.ViewModel.ShowEmptyCard);
        Assert.False(_harness.ViewModel.ShowReady);
        Assert.Equal(ReviewStrings.NoRunTitle, _harness.ViewModel.EmptyTitle);
    }

    [Fact]
    public void OnNavigatedTo_RunWithoutItems_ShowsEmptyRunCard()
    {
        _harness.Open(ReviewData.Run([]));

        Assert.True(_harness.ViewModel.ShowEmptyCard);
        Assert.Equal(ReviewStrings.EmptyTitle, _harness.ViewModel.EmptyTitle);
    }

    [Fact]
    public void OnNavigatedTo_RunSetInState_LoadsItsItems()
    {
        _harness.Open(ReviewData.Item(Title));

        Assert.True(_harness.ViewModel.ShowReady);
        Assert.False(_harness.ViewModel.ShowEmptyCard);
        Assert.Equal(1, _harness.ViewModel.TotalCount);
        Assert.Equal(Title, _harness.ViewModel.SelectedItem!.Title);
    }

    [Fact]
    public void OnNavigatedTo_PartialRun_ShowsWarningBanner()
    {
        _harness.Open(ReviewData.Run([ReviewData.Item(Title)], FailedUnits, SkippedUnits));

        Assert.Equal(BannerSeverity.Warning, _harness.ViewModel.RunBanner!.Severity);
    }

    [Fact]
    public void OnNavigatedTo_CompleteRun_HasNoRunBanner()
    {
        _harness.Open(ReviewData.Item(Title));

        Assert.Null(_harness.ViewModel.RunBanner);
    }

    [Fact]
    public void OnNavigatedTo_Ready_RequestsFiltersFocus()
    {
        _harness.Open(ReviewData.Item(Title));

        Assert.Equal(ReviewFocusKeys.Filters, _harness.ViewModel.PendingFocus);
    }

    [Fact]
    public void OnNavigatedTo_NotReady_RequestsGoToExtractFocus()
    {
        _harness.ViewModel.OnNavigatedTo();

        Assert.Equal(ReviewFocusKeys.GoToExtract, _harness.ViewModel.PendingFocus);
    }

    [Fact]
    public void GoToExtractCommand_Executed_NavigatesToExtract()
    {
        _harness.ViewModel.GoToExtractCommand.Execute(null);

        Assert.Equal([ScreenKeys.Extract], _harness.Navigation.Visited);
    }
}
