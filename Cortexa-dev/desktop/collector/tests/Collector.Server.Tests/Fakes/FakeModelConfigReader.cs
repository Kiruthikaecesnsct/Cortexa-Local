using Collector.Server.Application.Ports;
using Collector.Server.Application.Upload;

namespace Collector.Server.Tests.Fakes;

internal sealed class FakeModelConfigReader : IModelConfigReader
{
    public static readonly ModelConfigSnapshot DefaultSnapshot = new(
        "row-extraction",
        "row-evidence",
        "row-scoring",
        "row-seeding",
        "row-mode");

    public ModelConfigSnapshot Snapshot { get; set; } = DefaultSnapshot;

    public int CallCount { get; private set; }

    public Task<ModelConfigSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(Snapshot);
    }
}
