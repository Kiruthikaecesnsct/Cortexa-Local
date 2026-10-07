using Collector.Application.Upload;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collector.Presentation.ViewModels;

public sealed partial class BatchResultRowViewModel : ObservableObject
{
    private static readonly TimeSpan CopiedDuration = TimeSpan.FromSeconds(2);

    private readonly IClipboard _clipboard;

    public BatchResultRowViewModel(BatchUploadResult result, int total, IClipboard clipboard)
    {
        _clipboard = clipboard;
        IsUploaded = result.Status == UploadBatchStatus.Uploaded;
        IsFailed = result.Status == UploadBatchStatus.Failed;
        Line = ReviewStrings.BatchLine(result.Index, total, result.DocumentCount, result.ItemCount);
        BatchId = result.ServerBatchId;
        ErrorText = IsFailed ? ReviewStrings.UploadErrorText(result.Error) : null;
        IdName = ReviewStrings.BatchIdName(result.Index);
        CopyIdleName = ReviewStrings.CopyBatchIdName(result.Index);
    }

    public bool IsUploaded { get; }

    public bool IsFailed { get; }

    public string Line { get; }

    public string? BatchId { get; }

    public string? ErrorText { get; }

    public string IdName { get; }

    public string CopyIdleName { get; }

    public bool HasBatchId => IsUploaded && !string.IsNullOrEmpty(BatchId);

    public string CopyName => IsCopied ? ReviewStrings.Copied : CopyIdleName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CopyName))]
    public partial bool IsCopied { get; set; }

    [RelayCommand]
    private async Task CopyAsync()
    {
        if (string.IsNullOrEmpty(BatchId) || !_clipboard.TrySetText(BatchId))
        {
            return;
        }

        IsCopied = true;
        await Task.Delay(CopiedDuration);
        IsCopied = false;
    }
}
