using System.Net;
using Collector.Application.Ports;
using Collector.Tests.Support;

namespace Collector.Tests.Ai;

public sealed class AnthropicErrorMapperTests
{
    private const string ErrorBody = "{\"type\":\"error\",\"error\":{\"type\":\"api_error\",\"message\":\"upstream said sk-leaky-secret\"}}";

    private static async Task<AiProviderException> MapStatusAsync(HttpStatusCode status)
    {
        var thrown = await AnthropicWire.ThrownForAsync((_, _) => Task.FromResult(StubHttpHandler.Json(status, ErrorBody)));
        return AnthropicWire.Map(thrown);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task Map_PermanentStatus_IsPermanent(HttpStatusCode status)
    {
        var mapped = await MapStatusAsync(status);

        Assert.Equal(AiFailureKind.Permanent, mapped.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData((HttpStatusCode)529)]
    public async Task Map_RetryableStatus_IsTransient(HttpStatusCode status)
    {
        var mapped = await MapStatusAsync(status);

        Assert.Equal(AiFailureKind.Transient, mapped.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Claude rejected the API key.")]
    [InlineData(HttpStatusCode.Forbidden, "Claude denied access for this API key.")]
    [InlineData(HttpStatusCode.TooManyRequests, "Claude is rate limiting requests.")]
    [InlineData(HttpStatusCode.BadRequest, "Claude returned status 400.")]
    public async Task Map_Status_UsesFixedMessageWithoutUpstreamText(HttpStatusCode status, string expected)
    {
        var mapped = await MapStatusAsync(status);

        Assert.Equal(expected, mapped.Message);
        Assert.DoesNotContain("sk-leaky-secret", mapped.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Map_NetworkFailure_IsTransient()
    {
        var thrown = await AnthropicWire.ThrownForAsync((_, _) => throw new HttpRequestException("connection refused"));

        var mapped = AnthropicWire.Map(thrown);

        Assert.Equal(AiFailureKind.Transient, mapped.Kind);
        Assert.Equal("The Claude service could not be reached.", mapped.Message);
    }
}
