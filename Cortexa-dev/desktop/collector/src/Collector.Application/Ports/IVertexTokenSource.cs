namespace Collector.Application.Ports;

public interface IVertexTokenSource
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}
