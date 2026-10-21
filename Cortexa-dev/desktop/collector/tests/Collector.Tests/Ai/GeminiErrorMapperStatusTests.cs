using System.Net;
using Collector.Application.Ports;
using Collector.Infrastructure.Ai;

namespace Collector.Tests.Ai;

public sealed class GeminiErrorMapperStatusTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, AiFailureKind.Permanent)]
    [InlineData(HttpStatusCode.TooManyRequests, AiFailureKind.QuotaExceeded)]
    [InlineData(HttpStatusCode.ServiceUnavailable, AiFailureKind.Transient)]
    public void MapUnreachable_WithStatus_CarriesStatusCode(HttpStatusCode status, AiFailureKind kind)
    {
        var mapped = GeminiErrorMapper.MapUnreachable(new HttpRequestException("failed", null, status));

        Assert.Equal((int)status, mapped.StatusCode);
        Assert.Equal(kind, mapped.Kind);
    }

    [Fact]
    public void MapUnreachable_WithoutStatus_HasNoStatusCode()
    {
        var mapped = GeminiErrorMapper.MapUnreachable(new HttpRequestException("failed"));

        Assert.Null(mapped.StatusCode);
    }

    [Fact]
    public void Exception_DefaultsToNoStatusCode()
    {
        Assert.Null(new AiProviderException(AiFailureKind.Permanent, "x").StatusCode);
    }
}
