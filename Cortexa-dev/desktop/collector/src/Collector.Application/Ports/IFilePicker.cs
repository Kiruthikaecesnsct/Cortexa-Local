namespace Collector.Application.Ports;

public interface IFilePicker
{
    Task<IReadOnlyList<string>> PickFilesAsync(CancellationToken cancellationToken);

    Task<string?> PickSingleFileAsync(string dialogTitle, string filter, CancellationToken cancellationToken);

    Task<string?> PickSaveFileAsync(string dialogTitle, string filter, string defaultFileName, CancellationToken cancellationToken);
}
