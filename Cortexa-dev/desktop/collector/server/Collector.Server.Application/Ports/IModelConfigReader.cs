using Collector.Server.Application.Upload;

namespace Collector.Server.Application.Ports;

public interface IModelConfigReader
{
    Task<ModelConfigSnapshot> ReadAsync(CancellationToken cancellationToken);
}
