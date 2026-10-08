using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.SSO;
using Amazon.SSOOIDC;
using Collector.Application.Ports;

namespace Collector.Infrastructure.Ai;

internal static class BedrockErrorMapper
{
    public static AiProviderException MapBedrock(AmazonBedrockRuntimeException exception) => exception switch
    {
        ThrottlingException => Transient(exception, "Bedrock is rate limiting requests."),
        ServiceUnavailableException => Transient(exception, "Bedrock is temporarily unavailable."),
        ModelTimeoutException => Transient(exception, "The Bedrock model timed out."),
        ModelNotReadyException => Transient(exception, "The Bedrock model is not ready."),
        InternalServerException => Transient(exception, "Bedrock returned an internal error."),
        AccessDeniedException => Permanent(exception, "Bedrock denied access to the configured model."),
        ValidationException => Permanent(exception, "Bedrock rejected the request."),
        ResourceNotFoundException => Permanent(exception, "Bedrock could not find the configured model."),
        ModelErrorException => Permanent(exception, "The Bedrock model could not process the request."),
        _ => Transient(exception, $"Bedrock returned an error: {exception.ErrorCode}."),
    };

    public static AiProviderException MapIdentityCenter(Exception exception) => exception switch
    {
        Amazon.SSOOIDC.Model.ExpiredTokenException => Reauth(exception),
        Amazon.SSOOIDC.Model.UnauthorizedClientException => Reauth(exception),
        Amazon.SSOOIDC.Model.InvalidGrantException => Reauth(exception),
        Amazon.SSOOIDC.Model.AccessDeniedException => Reauth(exception),
        Amazon.SSO.Model.UnauthorizedException => Reauth(exception),
        Amazon.SSOOIDC.Model.SlowDownException => Transient(exception, "AWS SSO is rate limiting the sign-in flow."),
        Amazon.SSO.Model.TooManyRequestsException => Transient(exception, "AWS SSO is rate limiting requests."),
        AmazonSSOOIDCException or AmazonSSOException => Permanent(exception, "AWS SSO rejected the sign-in request."),
        _ => Transient(exception, "AWS SSO could not be reached."),
    };

    public static AiProviderException MapUnreachable(Exception exception) =>
        new(AiFailureKind.Transient, "The Bedrock service could not be reached.", exception);

    public static AiProviderException MapTimeout(Exception exception) =>
        new(AiFailureKind.Transient, "The Bedrock request timed out.", exception);

    private static AiProviderException Reauth(Exception exception) =>
        new(AiFailureKind.MissingApiKey, "Sign in to AWS IAM Identity Center again to use Bedrock.", exception);

    private static AiProviderException Transient(Exception exception, string message) =>
        new(AiFailureKind.Transient, message, exception);

    private static AiProviderException Permanent(Exception exception, string message) =>
        new(AiFailureKind.Permanent, message, exception);
}
