using Collector.Application.Ports;

namespace Collector.Tests.Presentation;

internal sealed class FakeFilePicker(IReadOnlyList<string> paths) : IFilePicker
{
    public Task<IReadOnlyList<string>> PickFilesAsync(CancellationToken cancellationToken) => Task.FromResult(paths);
}
