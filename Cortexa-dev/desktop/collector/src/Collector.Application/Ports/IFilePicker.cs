namespace Collector.Application.Ports;

public interface IFilePicker
{
    Task<IReadOnlyList<string>> PickFilesAsync(CancellationToken cancellationToken);
}
