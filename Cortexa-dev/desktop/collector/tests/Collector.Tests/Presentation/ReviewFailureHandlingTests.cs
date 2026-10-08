using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Collector.Application.Upload;
using Collector.Domain.Enums;
using Collector.Presentation.Resources;
using Collector.Presentation.ViewModels;
using Collector.Tests.Support;

namespace Collector.Tests.Presentation;

public sealed class ReviewFailureHandlingTests
{
    private const string KeptTitle = "Kept idea";
    private const int TwoBatchDocuments = UploadLimitsMirror.MaxDocumentsPerBatch + 1;

    private readonly ReviewHarness _harness = new();

    private static KnowledgeUploadException Network() => new(null, null, "unreachable");

    private static KnowledgeUploadException KeyReused() => new(409, "idempotency_key_reused", "reused");

    private static ExtractionRunResult TwoBatchRun() =>
        ReviewData.Run([.. Enumerable.Range(0, TwoBatchDocuments).Select(index => ReviewData.Item($"Idea {index}", $"doc-{index}"))]);

    private IReadOnlyList<string> DocumentsOf(BatchStatus status) =>
        [.. _harness.Batches.Rows.Where(row => row.Status == status).SelectMany(row => row.Batch.DocumentIds)];

    private async Task<ReviewViewModel> UploadWithFirstBatchFailedAsync()
    {
        _harness.Client.FailNext(Network());
        var viewModel = _harness.Open(TwoBatchRun());
        await viewModel.UploadCommand.ExecuteAsync(null);
        return viewModel;
    }

    [Fact]
    public async Task UploadCommand_RetryableFailure_BannerOffersRetryAndChangeSelection()
    {
        _harness.Client.FailNext(Network());
        var viewModel = _harness.Open(ReviewData.Item(KeptTitle));

        await viewModel.UploadCommand.ExecuteAsync(null);

        Assert.True(viewModel.UploadBanner!.HasAction);
        Assert.True(viewModel.UploadBanner.HasSecondaryAction);
        Assert.Equal(ReviewStrings.Retry, viewModel.UploadBanner.ActionText);
        Assert.Equal(ReviewStrings.ChangeSelection, viewModel.UploadBanner.SecondaryActionText);
        Assert.True(viewModel.ShowChangeSelection);
        Assert.Equal(ReviewStrings.SelectionLockedFailed, viewModel.LockedNote);
    }

    [Fact]
    public async Task UploadCommand_NothingRetryable_HidesRetryAndKeepsChangeSelection()
    {
        _harness.Client.FailNext(KeyReused());
        var viewModel = _harness.Open(ReviewData.Item(KeptTitle));

        await viewModel.UploadCommand.ExecuteAsync(null);

        Assert.False(viewModel.ShowRetry);
        Assert.False(viewModel.RetryFailedCommand.CanExecute(null));
        Assert.False(viewModel.UploadBanner!.HasAction);
        Assert.True(viewModel.UploadBanner.HasSecondaryAction);
        Assert.Contains(ReviewStrings.ErrorKeyReused, viewModel.UploadBanner.Message);
        Assert.Contains(ReviewStrings.ChangeHelp, viewModel.UploadBanner.Message);
    }

    [Fact]
    public async Task UploadCommand_MixedRetryability_NotesBatchesThatCantRetry()
    {
        _harness.Client.FailNext(Network()).FailNext(KeyReused());
        var viewModel = _harness.Open(TwoBatchRun());

        await viewModel.UploadCommand.ExecuteAsync(null);

        Assert.Equal(BannerSeverity.Error, viewModel.UploadBanner!.Severity);
        Assert.True(viewModel.ShowRetry);
        Assert.Contains(ReviewStrings.CantRetryNote(1), viewModel.UploadBanner.Message);
    }

    [Fact]
    public async Task RetryFailedCommand_InFlight_ShowsInfoBannerWithoutActions()
    {
        _harness.Client.FailNext(Network());
        var viewModel = _harness.Open(ReviewData.Item(KeptTitle));
        await viewModel.UploadCommand.ExecuteAsync(null);
        _harness.UploadGate = new TaskCompletionSource();

        var retry = viewModel.RetryFailedCommand.ExecuteAsync(null);

        Assert.Equal(BannerSeverity.Info, viewModel.UploadBanner!.Severity);
        Assert.Equal(ReviewStrings.RetryingTitle, viewModel.UploadBanner.Title);
        Assert.False(viewModel.UploadBanner.HasAction);
        Assert.False(viewModel.UploadBanner.HasSecondaryAction);
        _harness.UploadGate.SetResult();
        await retry;
    }

    [Fact]
    public async Task ChangeSelectionCommand_UnlocksOnlyFailedGroupsWithoutReplacingBatches()
    {
        var viewModel = await UploadWithFirstBatchFailedAsync();
        var failedDocuments = DocumentsOf(BatchStatus.Failed);
        var uploadedDocuments = DocumentsOf(BatchStatus.Uploaded);

        await viewModel.ChangeSelectionCommand.ExecuteAsync(null);

        Assert.All(_harness.Batches.Rows, row => Assert.False(row.Replaced));
        var headers = viewModel.Headers();
        Assert.All(headers.Where(header => failedDocuments.Contains(header.DocumentId)), header => Assert.False(header.IsLocked));
        Assert.All(headers.Where(header => uploadedDocuments.Contains(header.DocumentId)), header => Assert.True(header.IsLocked));
        Assert.False(viewModel.IsLocked);
        Assert.Empty(viewModel.Results);
        Assert.Equal(ReviewStrings.UnlockedTitle, viewModel.UploadBanner!.Title);
        Assert.True(viewModel.UploadBanner.CanDismiss);
        Assert.False(viewModel.ShowChangeSelection);
        Assert.Equal(ReviewFocusKeys.ItemList, viewModel.TakePendingFocus());
    }

    [Fact]
    public async Task UploadCommand_AfterChangeSelection_UsesNewKeyAndSkipsUploadedDocuments()
    {
        var viewModel = await UploadWithFirstBatchFailedAsync();
        var failedKey = _harness.Client.Calls[0].Key;
        var failedDocuments = DocumentsOf(BatchStatus.Failed);
        await viewModel.ChangeSelectionCommand.ExecuteAsync(null);
        var callsBefore = _harness.Client.Calls.Count;

        await viewModel.UploadCommand.ExecuteAsync(null);

        var calls = _harness.Client.Calls.Skip(callsBefore).ToList();
        Assert.NotEmpty(calls);
        Assert.DoesNotContain(calls, call => call.Key == failedKey);
        var sent = calls.SelectMany(call => call.Request.Documents).Select(document => document.ClientDocumentId);
        Assert.Equal(failedDocuments.Order(), sent.Order());
        Assert.True(viewModel.IsUploadComplete);
    }

    [Fact]
    public async Task UploadCommand_AfterChangeSelection_ReplacesOldFailedBatchesOnceNewOnesArePersisted()
    {
        var viewModel = await UploadWithFirstBatchFailedAsync();
        var failedIds = _harness.Batches.Rows.Where(row => row.Status == BatchStatus.Failed).Select(row => row.Id).ToList();
        await viewModel.ChangeSelectionCommand.ExecuteAsync(null);

        await viewModel.UploadCommand.ExecuteAsync(null);

        Assert.All(_harness.Batches.Rows.Where(row => failedIds.Contains(row.Id)), row => Assert.True(row.Replaced));
        Assert.All(_harness.Batches.Rows.Where(row => !failedIds.Contains(row.Id)), row => Assert.False(row.Replaced));
    }

    [Fact]
    public async Task OnNavigatedFrom_AfterChangeSelection_LeavesFailedBatchesRetryable()
    {
        var viewModel = await UploadWithFirstBatchFailedAsync();
        await viewModel.ChangeSelectionCommand.ExecuteAsync(null);

        var failedDocuments = DocumentsOf(BatchStatus.Failed);
        viewModel.ItemRows().First(row => failedDocuments.Contains(row.Item.DocumentId)).IsIncluded = false;

        viewModel.OnNavigatedFrom();
        await viewModel.UploadCommand.ExecuteAsync(null);

        Assert.Contains(_harness.Batches.Rows, row => row.Status == BatchStatus.Failed && !row.Replaced);
    }

    [Fact]
    public async Task ChangeSelectionCommand_NoFailures_CannotExecute()
    {
        var viewModel = _harness.Open(ReviewData.Item(KeptTitle));
        await viewModel.UploadCommand.ExecuteAsync(null);

        Assert.False(viewModel.ShowChangeSelection);
        Assert.False(viewModel.ChangeSelectionCommand.CanExecute(null));
    }

    [Fact]
    public async Task ExcludeAllCommand_AfterChangeSelection_LeavesUploadedGroupsUntouched()
    {
        var viewModel = await UploadWithFirstBatchFailedAsync();
        var uploadedDocuments = DocumentsOf(BatchStatus.Uploaded);
        await viewModel.ChangeSelectionCommand.ExecuteAsync(null);

        viewModel.ExcludeAllCommand.Execute(null);

        var rows = viewModel.ItemRows();
        Assert.All(rows.Where(row => uploadedDocuments.Contains(row.Item.DocumentId)), row => Assert.True(row.IsIncluded));
        Assert.All(rows.Where(row => !uploadedDocuments.Contains(row.Item.DocumentId)), row => Assert.False(row.IsIncluded));
    }

    [Theory]
    [InlineData(UploadErrorKind.InProgress, null, ReviewStrings.ErrorInProgress)]
    [InlineData(UploadErrorKind.Rejected, ReviewStrings.KeyReusedCode, ReviewStrings.ErrorKeyReused)]
    [InlineData(UploadErrorKind.Network, null, ReviewStrings.ErrorNetwork)]
    [InlineData(UploadErrorKind.Session, null, ReviewStrings.ErrorSession)]
    [InlineData(UploadErrorKind.Unknown, null, ReviewStrings.ErrorUnknown)]
    public void UploadErrorText_MapsKindAndCode(UploadErrorKind kind, string? code, string expected)
    {
        Assert.Equal(expected, ReviewStrings.UploadErrorText(new UploadError(kind, code)));
    }

    [Fact]
    public void UploadErrorText_OtherRejectedCode_NamesTheCode()
    {
        var text = ReviewStrings.UploadErrorText(new UploadError(UploadErrorKind.Rejected, "too_big"));

        Assert.Equal(ReviewStrings.ErrorRejected("too_big"), text);
    }
}
