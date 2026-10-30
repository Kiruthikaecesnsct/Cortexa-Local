using System.Net;
using Collector.Application.Ports;
using Collector.Infrastructure.Ai;

namespace Collector.Tests.Ai;

public sealed class VertexErrorMapperTests
{
    private const string Model = "gemini-3.6-flash";

    [Theory]
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
        var mapped = VertexErrorMapper.Map(status, VertexWire.ErrorBody((int)status), Model);

        Assert.Equal(expected, mapped.Kind);
    }

    [Fact]
    public void Map_BadRequestWithoutSizeMarker_IsPermanent()
    {
        var mapped = VertexErrorMapper.Map(HttpStatusCode.BadRequest, VertexWire.ErrorBody(400), Model);

        Assert.Equal(AiFailureKind.Permanent, mapped.Kind);
        Assert.IsNotType<AiInputTooLargeException>(mapped);
    }

    [Fact]
    public void Map_BadRequestWithSizeMarker_ThrowsInputTooLarge()
    {
        var mapped = VertexErrorMapper.Map(HttpStatusCode.BadRequest, VertexWire.InputTooLargeErrorBody(500_000, 32_768), Model);

        var tooLarge = Assert.IsType<AiInputTooLargeException>(mapped);
        Assert.Equal(500_000, tooLarge.TokenCount);
        Assert.Equal(32_768, tooLarge.Limit);
        Assert.Equal(Model, tooLarge.Model);
    }

    [Fact]
    public void Map_Unauthorized_IsDistinctFromTooManyRequests()
    {
        var permanent = VertexErrorMapper.Map(HttpStatusCode.Unauthorized, null, Model);
        var quota = VertexErrorMapper.Map(HttpStatusCode.TooManyRequests, null, Model);

        Assert.Equal(AiFailureKind.Permanent, permanent.Kind);
        Assert.Equal(AiFailureKind.QuotaExceeded, quota.Kind);
        Assert.NotEqual(permanent.Kind, quota.Kind);
    }

    [Fact]
    public void MapUnreachable_IsTransient()
    {
        var mapped = VertexErrorMapper.MapUnreachable(new InvalidOperationException("upstream failure"));

        Assert.Equal(AiFailureKind.Transient, mapped.Kind);
    }

    [Fact]
    public void MapTimeout_IsTransient()
    {
        var mapped = VertexErrorMapper.MapTimeout(new OperationCanceledException("upstream failure"));

        Assert.Equal(AiFailureKind.Transient, mapped.Kind);
    }

    [Fact]
    public void MapMalformed_IsTransient()
    {
        var mapped = VertexErrorMapper.MapMalformed(new InvalidOperationException("upstream failure"));

        Assert.Equal(AiFailureKind.Transient, mapped.Kind);
    }
}
