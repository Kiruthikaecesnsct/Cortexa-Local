using System.Net;
using Collector.Application.Ports;
using Collector.Infrastructure.Ai;

namespace Collector.Tests.Ai;

public sealed class GroqErrorMapperTests
{
    private const string Message = "upstream failure";

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, AiFailureKind.Permanent)]
    [InlineData(HttpStatusCode.Unauthorized, AiFailureKind.Permanent)]
    [InlineData(HttpStatusCode.Forbidden, AiFailureKind.Permanent)]
    [InlineData(HttpStatusCode.NotFound, AiFailureKind.Permanent)]
    [InlineData(HttpStatusCode.UnprocessableEntity, AiFailureKind.Permanent)]
    [InlineData(HttpStatusCode.TooManyRequests, AiFailureKind.QuotaExceeded)]
    [InlineData(HttpStatusCode.InternalServerError, AiFailureKind.Transient)]
    [InlineData(HttpStatusCode.ServiceUnavailable, AiFailureKind.Transient)]
    [InlineData(HttpStatusCode.GatewayTimeout, AiFailureKind.Transient)]
    [InlineData(HttpStatusCode.BadGateway, AiFailureKind.Transient)]
    public void Map_KnownStatus_MapsToExpectedKind(HttpStatusCode status, AiFailureKind expected)
    {
        var mapped = GroqErrorMapper.Map(status, new InvalidOperationException(Message));

        Assert.Equal(expected, mapped.Kind);
    }

    [Fact]
    public void Map_Unauthorized_IsDistinctFromTooManyRequests()
    {
        var permanent = GroqErrorMapper.Map(HttpStatusCode.Unauthorized, new InvalidOperationException(Message));
        var quota = GroqErrorMapper.Map(HttpStatusCode.TooManyRequests, new InvalidOperationException(Message));

        Assert.Equal(AiFailureKind.Permanent, permanent.Kind);
        Assert.Equal(AiFailureKind.QuotaExceeded, quota.Kind);
        Assert.NotEqual(permanent.Kind, quota.Kind);
    }

    [Fact]
    public void MapUnreachable_IsTransient()
    {
        var mapped = GroqErrorMapper.MapUnreachable(new InvalidOperationException(Message));

        Assert.Equal(AiFailureKind.Transient, mapped.Kind);
    }

    [Fact]
    public void MapTimeout_IsTransient()
    {
        var mapped = GroqErrorMapper.MapTimeout(new OperationCanceledException(Message));

        Assert.Equal(AiFailureKind.Transient, mapped.Kind);
    }

    [Fact]
    public void MapMalformed_IsTransient()
    {
        var mapped = GroqErrorMapper.MapMalformed(new InvalidOperationException(Message));

        Assert.Equal(AiFailureKind.Transient, mapped.Kind);
    }
}
