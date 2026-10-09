using Collector.Domain.Enums;
using Collector.Infrastructure.Http;

namespace Collector.Infrastructure.Remote.Cortexa;

public sealed class CortexaGatewayHttp(IHttpClientFactory factory, CortexaErrorMapper mapper)
{
    public RemoteHttp Create() => new(factory.CreateClient(HttpClientNames.CortexaGateway), mapper, SourceType.CortexaRepo);
}
