using System.Collections.ObjectModel;
using System.Windows.Automation;
using Collector.Application.Ports;
using Collector.Application.Upload;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace Collector.Presentation.ViewModels;

public static class FailedUploadFocusKeys
{
    public const string Refresh = "Refresh";
}

public sealed partial class FailedUploadsViewModel : FocusableViewModel
{
    private readonly IBatchStore _store;
    private readonly RetryFailedUploadHandler _retry;
    private readonly TimeProvider _time;
    private readonly ILogger<FailedUploadsViewModel> _logger;
    private readonly KeyedMerge<FailedUploadRowViewModel, RetryableBatch> _merge;

    public FailedUploadsViewModel(
        IBatchStore store,
        RetryFailedUploadHandler retry,
        TimeProvider time,
        ILogger<FailedUploadsViewModel> logger)
    {
        _store = store;
        _retry = retry;
        _time = time;
        _logger = logger;
        var actions = new FailedUploadActions(RetryRowAsync, RemoveRowAsync);
        _merge = new KeyedMerge<FailedUploadRowViewModel, RetryableBatch>(
            row => row.Id,
            batch => batch.Id,
            batch => new FailedUploadRowViewModel(batch, actions),
            (row, batch) => row.Refresh(batch));
        Rows = [];
        Rows.CollectionChanged += (_, _) => NotifyRows();
    }

    public event Action? BatchUploaded;

    public ObservableCollection<FailedUploadRowViewModel> Rows { get; }

    public bool HasRows => Rows.Count > 0;

    public string CountText => HistoryStrings.FailedCount(Rows.Count);

    [ObservableProperty]
    public partial string Announcement { get; set; } = string.Empty;

    [ObservableProperty]
    public partial AutomationLiveSetting AnnouncementSetting { get; set; } = AutomationLiveSetting.Polite;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var batches = await ListAsync(cancellationToken);
        if (batches is not null)
        {
            await UiThread.InvokeAsync(() => _merge.Apply(Rows, WithRetrying(batches)));
        }
    }

    private IEnumerable<RetryableBatch> WithRetrying(IReadOnlyList<RetryableBatch> batches) =>
        batches.Concat(Rows.Where(row => row.IsRetrying && batches.All(batch => batch.Id != row.Id)).Select(row => row.Placeholder()));

    private async Task<IReadOnlyList<RetryableBatch>?> ListAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _store.ListRetryableAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Loading the failed uploads failed unexpectedly.");
            return null;
        }
    }

    private async Task RetryRowAsync(FailedUploadRowViewModel row)
    {
        Announce(HistoryStrings.AnnounceRetrying(row.Name), AutomationLiveSetting.Polite);
        var result = await SendAsync(row.Id);
        if (result.Status == UploadBatchStatus.Uploaded)
        {
            Complete(row, HistoryStrings.AnnounceUploaded(row.Name));
            BatchUploaded?.Invoke();
            return;
        }

        if (result.Attempted)
        {
            ShowFailure(row, result.Error ?? new UploadError(UploadErrorKind.Unknown, null));
            return;
        }

        await LoadAsync(CancellationToken.None);
    }

    private async Task<RetryResult> SendAsync(string batchId)
    {
        try
        {
            return await _retry.RetryAsync(batchId, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Retrying the failed upload failed unexpectedly.");
            return new RetryResult(true, UploadBatchStatus.Failed, null, new UploadError(UploadErrorKind.Unknown, null));
        }
    }

    private void ShowFailure(FailedUploadRowViewModel row, UploadError error)
    {
        row.Apply(error, _time.GetUtcNow());
        Announce(HistoryStrings.AnnounceRetryFailed(row.Name, row.ErrorText), AutomationLiveSetting.Assertive);
        RequestFocus(row.FocusKey);
    }

    private async Task RemoveRowAsync(FailedUploadRowViewModel row)
    {
        try
        {
            await _store.MarkReplacedAsync([row.Id], CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Removing the failed upload failed unexpectedly.");
            return;
        }

        Complete(row, HistoryStrings.AnnounceRemoved(row.Name));
    }

    private void Complete(FailedUploadRowViewModel row, string announcement)
    {
        var index = Rows.IndexOf(row);
        Rows.Remove(row);
        Announce(announcement, AutomationLiveSetting.Polite);
        RequestFocus(FocusAfterRemoval(index));
    }

    private string FocusAfterRemoval(int index)
    {
        if (index < Rows.Count)
        {
            return Rows[index].FocusKey;
        }

        return Rows.Count > 0 ? Rows[^1].FocusKey : FailedUploadFocusKeys.Refresh;
    }

    private void Announce(string text, AutomationLiveSetting setting)
    {
        AnnouncementSetting = setting;
        Announcement = string.Empty;
        Announcement = text;
    }

    private void NotifyRows()
    {
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(CountText));
    }
}
