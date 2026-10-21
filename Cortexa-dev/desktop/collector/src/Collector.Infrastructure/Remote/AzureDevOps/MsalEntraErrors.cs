using Collector.Application.Remote.Auth;
using Microsoft.Identity.Client;

namespace Collector.Infrastructure.Remote.AzureDevOps;

internal static class MsalEntraErrors
{
    private const string AuthenticationCanceled = "authentication_canceled";
    private const string CodeExpired = "code_expired";
    private const string AuthorizationDeclined = "authorization_declined";
    private const string AccessDenied = "access_denied";

    public static bool IsHandled(Exception exception) => exception is MsalException or OperationCanceledException;

    public static string CodeOf(Exception exception) =>
        exception is MsalException msal ? msal.ErrorCode : exception.GetType().Name;

    public static EntraSignInOutcome Map(Exception exception) => exception switch
    {
        OperationCanceledException => new EntraSignInOutcome.Cancelled(),
        MsalException { ErrorCode: AuthenticationCanceled } => new EntraSignInOutcome.Cancelled(),
        MsalException { ErrorCode: CodeExpired } => new EntraSignInOutcome.Expired(),
        MsalException { ErrorCode: AuthorizationDeclined or AccessDenied } => new EntraSignInOutcome.Denied(),
        MsalClientException => new EntraSignInOutcome.Failed(EntraFailure.Unavailable),
        _ => new EntraSignInOutcome.Failed(EntraFailure.Other),
    };
}
