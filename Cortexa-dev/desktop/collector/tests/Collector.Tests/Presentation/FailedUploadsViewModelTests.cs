using System.Windows.Automation;
using Collector.Application.Ports;
using Collector.Presentation.Resources;
using Collector.Presentation.ViewModels;
using Collector.Tests.Support;

namespace Collector.Tests.Presentation;

public sealed class FailedUploadsViewModelTests : IDisposable
{
    private const string KeyReused = "rejected:idempotency_key_reused";

    private readonly HistoryHarness _harness = new();

    private FailedUploadsViewModel ViewModel => _harness.Failed;

    public void Dispose() => _harness.Poller.Dispose();

    private static KnowledgeUploadException Network() => new(null, null, "unreachable");

    [Fact]
    public async Task LoadAsync_FailedBatches_ListsRowsWithMappedErrors()
    {
        await _harness.SeedFailedAsync("key-1", "network", "doc-1");
        await _harness.SeedFailedAsync("key-2", KeyReused, "doc-2");

        await ViewModel.LoadAsync(TestSupport.Ct);

        Assert.Equal(2, ViewModel.Rows.Count);
        Assert.True(ViewModel.HasRows);
        Assert.Equal(ReviewStrings.ErrorNetwork, ViewModel.Rows[0].ErrorText);
        Assert.True(ViewModel.Rows[0].CanRetry);
        Assert.Equal(ReviewStrings.ErrorKeyReused, ViewModel.Rows[1].ErrorText);
        Assert.False(ViewModel.Rows[1].CanRetry);
        Assert.True(ViewModel.Rows[1].IsTerminal);
    }

    [Fact]
    public async Task LoadAsync_NothingFailed_HasNoRows()
    {
        await ViewModel.LoadAsync(TestSupport.Ct);

        Assert.False(ViewModel.HasRows);
    }

    [Fact]
    public async Task RetryCommand_Success_RemovesRowAndRaisesBatchUploaded()
    {
        await _harness.SeedFailedAsync("key-1");
        await ViewModel.LoadAsync(TestSupport.Ct);
        var raised = 0;
        ViewModel.BatchUploaded += () => raised++;

        await ViewModel.Rows[0].RetryCommand.ExecuteAsync(null);

        Assert.Empty(ViewModel.Rows);
        Assert.Equal(1, raised);
        Assert.Equal(AutomationLiveSetting.Polite, ViewModel.AnnouncementSetting);
        Assert.Equal(FailedUploadFocusKeys.Refresh, ViewModel.TakePendingFocus());
    }

    [Fact]
    public async Task RetryCommand_SuccessWithNeighbor_FocusesNextRetry()
    {
        await _harness.SeedFailedAsync("key-1", documentId: "doc-1");
        await _harness.SeedFailedAsync("key-2", documentId: "doc-2");
        await ViewModel.LoadAsync(TestSupport.Ct);
        var next = ViewModel.Rows[1];

        await ViewModel.Rows[0].RetryCommand.ExecuteAsync(null);

        Assert.Equal(next.RetryKey, ViewModel.TakePendingFocus());
    }

    [Fact]
    public async Task RetryCommand_Failure_KeepsRowWithUpdatedErrorAndAssertiveAnnouncement()
    {
        await _harness.SeedFailedAsync("key-1");
        await ViewModel.LoadAsync(TestSupport.Ct);
        var row = ViewModel.Rows[0];
        var raised = 0;
        ViewModel.BatchUploaded += () => raised++;
        _harness.UploadClient.FailNext(new KnowledgeUploadException(409, "upload_in_progress", "busy"));

        await row.RetryCommand.ExecuteAsync(null);

        Assert.Same(row, Assert.Single(ViewModel.Rows));
        Assert.Equal(ReviewStrings.ErrorInProgress, row.ErrorText);
        Assert.False(row.IsRetrying);
        Assert.True(row.CanRetry);
        Assert.Equal(0, raised);
        Assert.Equal(AutomationLiveSetting.Assertive, ViewModel.AnnouncementSetting);
        Assert.Contains(ReviewStrings.ErrorInProgress, ViewModel.Announcement);
        Assert.Equal(row.RetryKey, ViewModel.TakePendingFocus());
    }

    [Fact]
    public async Task RetryCommand_RejectedWithKeyReused_BecomesTerminalRow()
    {
        await _harness.SeedFailedAsync("key-1");
        await ViewModel.LoadAsync(TestSupport.Ct);
        var row = ViewModel.Rows[0];
        _harness.UploadClient.FailNext(new KnowledgeUploadException(409, "idempotency_key_reused", "reused"));

        await row.RetryCommand.ExecuteAsync(null);

        Assert.True(row.IsTerminal);
        Assert.Equal(ReviewStrings.ErrorKeyReused, row.ErrorText);
        Assert.Equal(row.RemoveKey, ViewModel.TakePendingFocus());
    }

    [Fact]
    public async Task RemoveCommand_TerminalRow_MarksBatchReplacedAndRemovesRow()
    {
        await _harness.SeedFailedAsync("key-1", KeyReused);
        await ViewModel.LoadAsync(TestSupport.Ct);
        var row = ViewModel.Rows[0];

        await row.RemoveCommand.ExecuteAsync(null);

        Assert.Empty(ViewModel.Rows);
        Assert.True(Assert.Single(_harness.Batches.Rows).Replaced);
        Assert.Empty(_harness.UploadClient.Calls);
    }

    [Fact]
    public async Task RetryCommand_PressedWhileRetrying_IsIgnored()
    {
        await _harness.SeedFailedAsync("key-1");
        await ViewModel.LoadAsync(TestSupport.Ct);
        var row = ViewModel.Rows[0];
        _harness.UploadGate.Gate = new TaskCompletionSource();

        var first = row.RetryCommand.ExecuteAsync(null);
        await HistoryHarness.Eventually(() => row.IsRetrying);
        Assert.True(row.RetryCommand.CanExecute(null));
        Assert.Equal(HistoryStrings.Retrying, row.RetryLabel);
        await row.RetryCommand.ExecuteAsync(null);
        _harness.UploadGate.Gate.SetResult();
        await first;

        Assert.Single(_harness.UploadClient.Calls);
        Assert.Empty(ViewModel.Rows);
    }

    [Fact]
    public async Task LoadAsync_WhileRetrying_KeepsRetryingRowState()
    {
        await _harness.SeedFailedAsync("key-1");
        await ViewModel.LoadAsync(TestSupport.Ct);
        var row = ViewModel.Rows[0];
        _harness.UploadGate.Gate = new TaskCompletionSource();
        var retry = row.RetryCommand.ExecuteAsync(null);
        await HistoryHarness.Eventually(() => row.IsRetrying);

        await ViewModel.LoadAsync(TestSupport.Ct);

        Assert.Same(row, Assert.Single(ViewModel.Rows));
        Assert.True(row.IsRetrying);
        _harness.UploadGate.Gate.SetResult();
        await retry;
    }
}
