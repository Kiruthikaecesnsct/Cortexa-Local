using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.SSO;
using Amazon.SSOOIDC;
using Collector.Application.Ports;
using Collector.Infrastructure.Ai;

namespace Collector.Tests.Ai;

public sealed class BedrockErrorMapperTests
{
    private const string Message = "upstream failure";

    [Theory]
    [InlineData(typeof(ThrottlingException), AiFailureKind.Transient)]
    [InlineData(typeof(ServiceUnavailableException), AiFailureKind.Transient)]
    [InlineData(typeof(ModelTimeoutException), AiFailureKind.Transient)]
    [InlineData(typeof(ModelNotReadyException), AiFailureKind.Transient)]
    [InlineData(typeof(InternalServerException), AiFailureKind.Transient)]
    [InlineData(typeof(AccessDeniedException), AiFailureKind.Permanent)]
    [InlineData(typeof(ValidationException), AiFailureKind.Permanent)]
    [InlineData(typeof(ResourceNotFoundException), AiFailureKind.Permanent)]
    [InlineData(typeof(ModelErrorException), AiFailureKind.Permanent)]
    public void MapBedrock_KnownExceptionType_MapsToExpectedKind(Type exceptionType, AiFailureKind expected)
    {
        var exception = CreateBedrockException(exceptionType);

        var mapped = BedrockWire.MapBedrock(exception);

        Assert.Equal(expected, mapped.Kind);
    }

    [Fact]
    public void MapBedrock_UnknownException_IsTransientAndReportsErrorCode()
    {
        var exception = new AmazonBedrockRuntimeException(Message) { ErrorCode = "Oddity" };

        var mapped = BedrockWire.MapBedrock(exception);

        Assert.Equal(AiFailureKind.Transient, mapped.Kind);
        Assert.Contains("Oddity", mapped.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(Amazon.SSOOIDC.Model.ExpiredTokenException))]
    [InlineData(typeof(Amazon.SSOOIDC.Model.UnauthorizedClientException))]
    [InlineData(typeof(Amazon.SSOOIDC.Model.InvalidGrantException))]
    [InlineData(typeof(Amazon.SSOOIDC.Model.AccessDeniedException))]
    [InlineData(typeof(Amazon.SSO.Model.UnauthorizedException))]
    public void MapIdentityCenter_ReauthCase_NeedsReconnect(Type exceptionType)
    {
        var exception = CreateIdentityCenterException(exceptionType);

        var mapped = BedrockWire.MapIdentityCenter(exception);

        Assert.Equal(AiFailureKind.MissingApiKey, mapped.Kind);
    }

    [Theory]
    [InlineData(typeof(Amazon.SSOOIDC.Model.SlowDownException))]
    [InlineData(typeof(Amazon.SSO.Model.TooManyRequestsException))]
    public void MapIdentityCenter_RateLimited_IsTransient(Type exceptionType)
    {
        var exception = CreateIdentityCenterException(exceptionType);

        var mapped = BedrockWire.MapIdentityCenter(exception);

        Assert.Equal(AiFailureKind.Transient, mapped.Kind);
    }

    [Fact]
    public void MapIdentityCenter_OtherOidcServiceException_IsPermanent()
    {
        var exception = new AmazonSSOOIDCException(Message);

        var mapped = BedrockWire.MapIdentityCenter(exception);

        Assert.Equal(AiFailureKind.Permanent, mapped.Kind);
    }

    [Fact]
    public void MapIdentityCenter_OtherSsoServiceException_IsPermanent()
    {
        var exception = new AmazonSSOException(Message);

        var mapped = BedrockWire.MapIdentityCenter(exception);

        Assert.Equal(AiFailureKind.Permanent, mapped.Kind);
    }

    [Fact]
    public void MapIdentityCenter_UnrecognizedException_IsTransient()
    {
        var mapped = BedrockWire.MapIdentityCenter(new InvalidOperationException(Message));

        Assert.Equal(AiFailureKind.Transient, mapped.Kind);
    }

    [Fact]
    public void MapUnreachable_IsTransient()
    {
        var mapped = BedrockWire.MapUnreachable(new InvalidOperationException(Message));

        Assert.Equal(AiFailureKind.Transient, mapped.Kind);
    }

    [Fact]
    public void MapTimeout_IsTransient()
    {
        var mapped = BedrockWire.MapTimeout(new OperationCanceledException(Message));

        Assert.Equal(AiFailureKind.Transient, mapped.Kind);
    }

    private static AmazonBedrockRuntimeException CreateBedrockException(Type exceptionType) =>
        (AmazonBedrockRuntimeException)Activator.CreateInstance(exceptionType, Message)!;

    private static Exception CreateIdentityCenterException(Type exceptionType) =>
        (Exception)Activator.CreateInstance(exceptionType, Message)!;
}
