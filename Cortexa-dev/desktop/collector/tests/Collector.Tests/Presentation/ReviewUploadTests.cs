using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Collector.Presentation.Resources;
using Collector.Presentation.ViewModels;

namespace Collector.Tests.Presentation;

public sealed class ReviewUploadTests
{
    private const string KeptTitle = "Kept idea";
    private const string DeselectedTitle = "Deselected idea";
    private const string EchoTitle = "Echo idea";
    private const string SmallDocumentId = "doc-small";
    private const string BigDocumentId = "doc-big";
    private const string OtherRunDocumentId = "doc-other";
    private const int SmallDocumentItems = 2;
    private const int OverLimitItems = UploadLimitsMirror.MaxItemsPerDocument + 1;
    private const int TwoBatchDocuments = UploadLimitsMirror.MaxDocumentsPerBatch + 1;
    private const string FirstServerBatchId = "server-1";

    private readonly ReviewHarness _harness = new();

    private static KnowledgeUploadException Network() => new(null, null, "unreachable");

    private static ExtractionRunResult TwoBatchRun() =>
        ReviewData.Run([.. Enumerable.Range(0, TwoBatchDocuments).Select(index => ReviewData.Item($"Idea {index}", $"doc-{index}"))]);

    [Fact]
    public async Task UploadCommand_ItemDeselected_RequestOmitsIt()
    {
        var viewModel = _harness.Open(ReviewData.Item(KeptTitle), ReviewData.Item(DeselectedTitle));
        viewModel.Row(DeselectedTitle).IsIncluded = false;

        await viewModel.UploadCommand.ExecuteAsync(null);

        Assert.Equal([KeptTitle], _harness.SentTitles());
    }

    [Fact]
    public async Task UploadCommand_EchoItemLeftExcluded_RequestOmitsIt()
    {
        var viewModel = _harness.Open(ReviewData.Item(KeptTitle), ReviewData.Item(EchoTitle, echo: true));

        await viewModel.UploadCommand.ExecuteAsync(null);

        Assert.DoesNotContain(EchoTitle, _harness.SentTitles());
        Assert.Contains(KeptTitle, _harness.SentTitles());
    }

    [Fact]
    public async Task UploadCommand_BlockedDocument_IsNotSentWhileOtherDocumentIs()
    {
        ExtractedKnowledgeItem[] items = [.. ReviewData.Items(SmallDocumentId, SmallDocumentItems), .. ReviewData.Items(BigDocumentId, OverLimitItems)];
        var viewModel = _harness.Open(items);

        await viewModel.UploadCommand.ExecuteAsync(null);

        Assert.Equal([SmallDocumentId], _harness.SentDocumentIds());
    }

    [Fact]
    public async Task UploadCommand_AllBatchesSucceed_ShowsSuccessBannerAndResults()
    {
        var viewModel = _harness.Open(ReviewData.Item(KeptTitle));

        await viewModel.UploadCommand.ExecuteAsync(null);

        Assert.Equal(BannerSeverity.Success, viewModel.UploadBanner!.Severity);
        Assert.Equal(FirstServerBatchId, Assert.Single(viewModel.Results).BatchId);
        Assert.True(viewModel.IsUploadComplete);
    }

    [Fact]
    public async Task UploadCommand_AllBatchesSucceed_DisablesUploadAndLabelsItUploaded()
    {
        var viewModel = _harness.Open(ReviewData.Item(KeptTitle));

        await viewModel.UploadCommand.ExecuteAsync(null);

        Assert.Equal(ReviewStrings.UploadedButton, viewModel.UploadLabel);
        Assert.False(viewModel.UploadCommand.CanExecute(null));
        Assert.False(viewModel.ShowRetry);
    }

    [Fact]
    public async Task UploadCommand_FirstUpload_LocksSelection()
    {
        var viewModel = _harness.Open(ReviewData.Item(KeptTitle), ReviewData.Item(DeselectedTitle));
        var row = viewModel.Row(KeptTitle);
        var header = viewModel.Headers().Single();

        await viewModel.UploadCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsLocked);
        Assert.True(row.IsLocked);
        Assert.False(row.CanToggle);
        Assert.False(row.ToggleCommand.CanExecute(null));
        Assert.False(header.ToggleCommand.CanExecute(null));
        Assert.False(viewModel.IncludeAllCommand.CanExecute(null));
        Assert.False(viewModel.ExcludeAllCommand.CanExecute(null));
    }

    [Fact]
    public async Task UploadCommand_OneOfTwoBatchesFails_ShowsWarningAndOffersRetry()
    {
        _harness.Client.FailNext(Network());
        var viewModel = _harness.Open(TwoBatchRun());

        await viewModel.UploadCommand.ExecuteAsync(null);

        Assert.Equal(BannerSeverity.Warning, viewModel.UploadBanner!.Severity);
        Assert.True(viewModel.ShowRetry);
        Assert.True(viewModel.RetryFailedCommand.CanExecute(null));
    }

    [Fact]
    public async Task RetryFailedCommand_OneBatchFailed_ResendsOnlyItWithSameKeyAndBody()
    {
        _harness.Client.FailNext(Network());
        var viewModel = _harness.Open(TwoBatchRun());
        await viewModel.UploadCommand.ExecuteAsync(null);

        await viewModel.RetryFailedCommand.ExecuteAsync(null);

        var calls = _harness.Client.Calls;
        Assert.Equal(3, calls.Count);
        Assert.Equal(calls[0].Key, calls[2].Key);
        Assert.Equal(calls[0].Body, calls[2].Body);
        Assert.True(viewModel.IsUploadComplete);
        Assert.Equal(BannerSeverity.Success, viewModel.UploadBanner!.Severity);
    }

    [Fact]
    public async Task UploadCommand_EveryBatchFails_ShowsErrorBanner()
    {
        _harness.Client.FailNext(Network());
        var viewModel = _harness.Open(ReviewData.Item(KeptTitle));

        await viewModel.UploadCommand.ExecuteAsync(null);

        Assert.Equal(BannerSeverity.Error, viewModel.UploadBanner!.Severity);
        Assert.True(viewModel.ShowRetry);
        Assert.False(viewModel.IsUploadComplete);
    }

    [Fact]
    public async Task OnNavigatedTo_NewRunWhileSessionExists_ResetsUploadState()
    {
        var viewModel = _harness.Open(ReviewData.Item(KeptTitle));
        await viewModel.UploadCommand.ExecuteAsync(null);

        _harness.Open(ReviewData.Item(KeptTitle, OtherRunDocumentId));

        Assert.False(viewModel.IsLocked);
        Assert.False(viewModel.IsUploadComplete);
        Assert.Null(viewModel.UploadBanner);
        Assert.Empty(viewModel.Results);
        Assert.True(viewModel.UploadCommand.CanExecute(null));
    }

    [Fact]
    public async Task UploadCommand_NewRunLoadedWhileUploadInFlight_StaleCompletionDoesNotTouchNewRun()
    {
        var viewModel = _harness.Open(ReviewData.Item(KeptTitle));
        _harness.UploadGate = new TaskCompletionSource();
        var upload = viewModel.UploadCommand.ExecuteAsync(null);
        _harness.Open(ReviewData.Item(KeptTitle, OtherRunDocumentId));

        _harness.UploadGate.SetResult();
        await upload;

        Assert.False(viewModel.IsUploading);
        Assert.False(viewModel.IsLocked);
        Assert.False(viewModel.IsUploadComplete);
        Assert.Null(viewModel.UploadBanner);
        Assert.Empty(viewModel.Results);
    }
}
