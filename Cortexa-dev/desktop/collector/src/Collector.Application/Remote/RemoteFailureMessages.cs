using Collector.Domain.Enums;

namespace Collector.Application.Remote;

public static class RemoteFailureMessages
{
    public static string For(RemoteFailureKind kind, SourceType provider)
    {
        var name = DisplayName(provider);
        return provider == SourceType.CortexaRepo && CortexaWording(kind) is { } cortexa
            ? cortexa
            : Generic(kind, name);
    }

    public static string DisplayName(SourceType provider) => provider switch
    {
        SourceType.Github => "GitHub",
        SourceType.AzureDevops => "Azure DevOps",
        SourceType.Ssh => "SSH",
        SourceType.CortexaRepo => "Cortexa",
        _ => provider.ToString(),
    };

    private static string? CortexaWording(RemoteFailureKind kind) => kind switch
    {
        RemoteFailureKind.Auth => "Your Cortexa session expired. Sign in again.",
        RemoteFailureKind.AccessDenied => "Your Cortexa account does not have permission to read saved repositories.",
        _ => null,
    };

    private static string Generic(RemoteFailureKind kind, string name)
    {
        return kind switch
        {
            RemoteFailureKind.MissingToken => $"Enter a {name} access token to connect.",
            RemoteFailureKind.Auth => $"{name} rejected the access token. Check that it is valid and has not expired.",
            RemoteFailureKind.AccessDenied => $"The {name} access token does not have permission for this request.",
            RemoteFailureKind.SsoRequired => $"This {name} organization requires single sign-on. Authorize the token for SSO and try again.",
            RemoteFailureKind.NotFound => $"{name} could not find the requested repository or branch.",
            RemoteFailureKind.EmptyRepository => "This repository has no files on the selected branch.",
            RemoteFailureKind.RateLimited => $"{name} rate limit reached. Try again after the limit resets.",
            RemoteFailureKind.RepositoryTooLarge => "This repository is too large to collect.",
            _ => $"{name} could not be reached or returned an unexpected response.",
        };
    }
}
