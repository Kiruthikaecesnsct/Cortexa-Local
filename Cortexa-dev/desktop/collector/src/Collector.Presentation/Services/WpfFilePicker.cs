using Collector.Application.Ports;
using Collector.Presentation.Resources;
using Microsoft.Win32;

namespace Collector.Presentation.Services;

public sealed class WpfFilePicker : IFilePicker
{
    public Task<IReadOnlyList<string>> PickFilesAsync(CancellationToken cancellationToken)
    {
        var dialog = new OpenFileDialog
        {
            Title = ExtractionStrings.PickFilesDialogTitle,
            Multiselect = true,
            CheckFileExists = true,
        };
        var picked = dialog.ShowDialog() == true;
        IReadOnlyList<string> files = picked ? dialog.FileNames : [];
        return Task.FromResult(files);
    }

    public Task<string?> PickSingleFileAsync(string dialogTitle, string filter, CancellationToken cancellationToken)
    {
        var dialog = new OpenFileDialog
        {
            Title = dialogTitle,
            Filter = filter,
            Multiselect = false,
            CheckFileExists = true,
        };
        var picked = dialog.ShowDialog() == true;
        string? file = picked ? dialog.FileName : null;
        return Task.FromResult(file);
    }

    public Task<string?> PickSaveFileAsync(string dialogTitle, string filter, string defaultFileName, CancellationToken cancellationToken)
    {
        var dialog = new SaveFileDialog
        {
            Title = dialogTitle,
            Filter = filter,
            FileName = defaultFileName,
            OverwritePrompt = true,
        };
        var picked = dialog.ShowDialog() == true;
        string? file = picked ? dialog.FileName : null;
        return Task.FromResult(file);
    }
}
