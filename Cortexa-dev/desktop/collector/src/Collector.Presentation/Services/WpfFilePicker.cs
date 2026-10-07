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
}
