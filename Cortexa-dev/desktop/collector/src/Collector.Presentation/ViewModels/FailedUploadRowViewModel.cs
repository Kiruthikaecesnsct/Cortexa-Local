using Collector.Application.Ports;
using Collector.Application.Upload;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collector.Presentation.ViewModels;

public sealed record FailedUploadActions(
    Func<FailedUploadRowViewModel, Task> Retry,
    Func<FailedUploadRowViewModel, Task> Remove);

public sealed partial class FailedUploadRowViewModel : ObservableObject
{
    private readonly FailedUploadActions _actions;

    public FailedUploadRowViewModel(RetryableBatch batch, FailedUploadActions actions)
    {
        _actions = actions;
        Id = batch.Id;
        Name = batch.BatchName;
        CountsText = HistoryStrings.Counts(batch.DocumentCount, batch.ItemCount);
        Apply(UploadErrorMapper.Parse(batch.LastError), batch.UpdatedAt);
    }

    public string Id { get; }

    public string Name { get; }

    public string CountsText { get; }

    public bool IsTerminal => !CanRetry;

    public string RetryKey => $"FailedRetry:{Id}";

    public string RemoveKey => $"FailedRemove:{Id}";

    public string FocusKey => CanRetry ? RetryKey : RemoveKey;

    public string RetryLabel => IsRetrying ? HistoryStrings.Retrying : HistoryStrings.Retry;

    public string RetryName => IsRetrying ? HistoryStrings.RetryingName(Name) : HistoryStrings.RetryName(Name);

    public string RemoveName => HistoryStrings.RemoveName(Name);

    public string DetailText => HistoryStrings.RowDetail(CountsText, FailedAtText);

    public string AutomationName => HistoryStrings.FailedRowName(Name, CountsText, FailedAtText, ErrorText);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    public partial string ErrorText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName), nameof(DetailText))]
    public partial string FailedAtText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTerminal), nameof(FocusKey))]
    public partial bool CanRetry { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RetryLabel), nameof(RetryName))]
    public partial bool IsRetrying { get; set; }

    public RetryableBatch Placeholder() => new()
    {
        Id = Id,
        IdempotencyKey = string.Empty,
        BatchName = Name,
        UpdatedAt = DateTimeOffset.MinValue,
        DocumentCount = 0,
        ItemCount = 0,
    };

    public void Refresh(RetryableBatch batch)
    {
        if (!IsRetrying)
        {
            Apply(UploadErrorMapper.Parse(batch.LastError), batch.UpdatedAt);
        }
    }

    public void Apply(UploadError error, DateTimeOffset failedAt)
    {
        ErrorText = ReviewStrings.UploadErrorText(error);
        FailedAtText = HistoryStrings.FailedAt(failedAt.ToLocalTime());
        CanRetry = error.CanRetry;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RetryAsync()
    {
        if (IsRetrying)
        {
            return;
        }

        IsRetrying = true;
        try
        {
            await _actions.Retry(this);
        }
        finally
        {
            IsRetrying = false;
        }
    }

    [RelayCommand]
    private Task RemoveAsync() => _actions.Remove(this);
}
