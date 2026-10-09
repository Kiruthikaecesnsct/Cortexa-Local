using System.Windows.Input;
using Collector.Application.Remote;
using Collector.Domain.Enums;
using Collector.Presentation.Resources;

namespace Collector.Presentation.ViewModels;

public sealed record RemoteBannerActions(
    ICommand OpenSettings,
    ICommand TryAgain,
    ICommand Reload,
    ICommand OpenGitHubTokens,
    ICommand UseLocal,
    ICommand Dismiss);

public sealed record RemoteFailureContext(
    SourceType Provider,
    string Repository,
    string Branch,
    string Owner,
    string Size,
    string Limit)
{
    public TimeSpan? RetryAfter { get; init; }
}

public sealed class RemoteBannerFactory(RemoteBannerActions actions)
{
    public BannerContent ForFailure(RemoteFailureKind kind, RemoteFailureContext context) =>
        context.Provider == SourceType.Ssh ? ForSshFailure(kind, context) : ForTokenFailure(kind, context);

    private BannerContent ForSshFailure(RemoteFailureKind kind, RemoteFailureContext context) => kind switch
    {
        RemoteFailureKind.Auth => SshAuth(),
        RemoteFailureKind.AccessDenied => SshDenied(),
        RemoteFailureKind.FingerprintMismatch => FingerprintMismatch(),
        RemoteFailureKind.NotFound or RemoteFailureKind.EmptyRepository or RemoteFailureKind.RepositoryTooLarge =>
            ForTokenFailure(kind, context),
        _ => SshConnectionFailed(),
    };

    private BannerContent ForTokenFailure(RemoteFailureKind kind, RemoteFailureContext context)
    {
        var provider = RemoteFailureMessages.DisplayName(context.Provider);
        var isGitHub = context.Provider == SourceType.Github;
        return kind switch
        {
            RemoteFailureKind.MissingToken => Missing(provider),
            RemoteFailureKind.Auth => Auth(provider),
            RemoteFailureKind.AccessDenied => Denied(provider, context.Repository, isGitHub),
            RemoteFailureKind.SsoRequired when isGitHub => Sso(context.Owner),
            RemoteFailureKind.SsoRequired => Auth(provider),
            RemoteFailureKind.NotFound => NotFound(context),
            RemoteFailureKind.EmptyRepository => EmptyRepository(context),
            RemoteFailureKind.RepositoryTooLarge => TooLarge(context),
            RemoteFailureKind.RateLimited => RateLimited(provider, isGitHub, context.RetryAfter),
            _ => Upstream(provider),
        };
    }

    public BannerContent Canceled() => Dismissible(
        BannerSeverity.Info,
        RemoteSourceStrings.CanceledTitle,
        RemoteSourceStrings.CanceledMessage);

    public BannerContent Resumed() => Dismissible(BannerSeverity.Info, RemoteSourceStrings.Resumed, null);

    public BannerContent Truncated(SourceType provider, int fetched) => Dismissible(
        BannerSeverity.Warning,
        RemoteSourceStrings.TruncatedTitle,
        RemoteSourceStrings.TruncatedMessage(RemoteFailureMessages.DisplayName(provider), fetched));

    public BannerContent NoFiles(string repository, string branch) => Dismissible(
        BannerSeverity.Warning,
        RemoteSourceStrings.NoFilesTitle,
        RemoteSourceStrings.NoFilesMessage(repository, branch));

    public BannerContent Paused(SourceType provider, TimeSpan remaining)
    {
        var isGitHub = provider == SourceType.Github;
        var title = RemoteSourceStrings.RateTitle(RemoteFailureMessages.DisplayName(provider), isGitHub);
        return new BannerContent
        {
            Severity = BannerSeverity.Warning,
            Title = title,
            Message = RemoteSourceStrings.RateMessage(CountdownFormatter.Format(remaining)),
            AutomationName = RemoteSourceStrings.RateAnnouncement(title, CountdownFormatter.Approximate(remaining)),
        };
    }

    private BannerContent Missing(string provider) => new()
    {
        Severity = BannerSeverity.Warning,
        Title = RemoteSourceStrings.MissingTitle(provider),
        Message = RemoteSourceStrings.MissingMessage(provider),
        ActionText = RemoteSourceStrings.OpenSettings,
        ActionName = RemoteSourceStrings.OpenSettingsAddName(provider),
        ActionCommand = actions.OpenSettings,
        SecondaryActionText = RemoteSourceStrings.TryAgain,
        SecondaryActionCommand = actions.TryAgain,
    };

    private BannerContent Auth(string provider) => new()
    {
        Severity = BannerSeverity.Error,
        Title = RemoteSourceStrings.AuthTitle(provider),
        Message = RemoteSourceStrings.AuthMessage,
        ActionText = RemoteSourceStrings.OpenSettings,
        ActionName = RemoteSourceStrings.OpenSettingsReplaceName(provider),
        ActionCommand = actions.OpenSettings,
        SecondaryActionText = RemoteSourceStrings.TryAgain,
        SecondaryActionCommand = actions.TryAgain,
    };

    private BannerContent Denied(string provider, string repository, bool isGitHub) => new()
    {
        Severity = BannerSeverity.Error,
        Title = RemoteSourceStrings.DeniedTitle,
        Message = RemoteSourceStrings.DeniedMessage(repository, isGitHub),
        ActionText = RemoteSourceStrings.OpenSettings,
        ActionName = RemoteSourceStrings.OpenSettingsReplaceName(provider),
        ActionCommand = actions.OpenSettings,
    };

    private BannerContent Sso(string owner) => new()
    {
        Severity = BannerSeverity.Error,
        Title = RemoteSourceStrings.SsoTitle(owner),
        Message = RemoteSourceStrings.SsoMessage(owner),
        ActionText = RemoteSourceStrings.OpenGitHubTokens,
        ActionName = RemoteSourceStrings.OpenGitHubTokensName,
        ActionCommand = actions.OpenGitHubTokens,
        SecondaryActionText = RemoteSourceStrings.TryAgain,
        SecondaryActionCommand = actions.TryAgain,
    };

    private BannerContent NotFound(RemoteFailureContext context) => new()
    {
        Severity = BannerSeverity.Error,
        Title = RemoteSourceStrings.NotFoundTitle,
        Message = RemoteSourceStrings.NotFoundMessage(context.Repository, context.Branch),
        ActionText = RemoteSourceStrings.Reload,
        ActionCommand = actions.Reload,
    };

    private BannerContent EmptyRepository(RemoteFailureContext context) => Dismissible(
        BannerSeverity.Warning,
        RemoteSourceStrings.EmptyRepositoryTitle,
        RemoteSourceStrings.EmptyRepositoryMessage(context.Repository, context.Branch));

    private BannerContent TooLarge(RemoteFailureContext context) => Dismissible(
        BannerSeverity.Error,
        RemoteSourceStrings.TooLargeTitle,
        RemoteSourceStrings.TooLargeMessage(context.Repository, context.Size, context.Limit)) with
    {
        ActionText = RemoteSourceStrings.LocalName,
        ActionName = RemoteSourceStrings.SwitchToLocalName,
        ActionCommand = actions.UseLocal,
    };

    private BannerContent RateLimited(string provider, bool isGitHub, TimeSpan? retryAfter) => Dismissible(
        BannerSeverity.Warning,
        RemoteSourceStrings.RateTitle(provider, isGitHub),
        retryAfter is { } wait ? RemoteSourceStrings.RateLimitedMessage(CountdownFormatter.Approximate(wait)) : null) with
    {
        ActionText = RemoteSourceStrings.TryAgain,
        ActionCommand = actions.TryAgain,
    };

    private BannerContent Upstream(string provider) => Dismissible(
        BannerSeverity.Error,
        RemoteSourceStrings.UpstreamTitle(provider),
        RemoteSourceStrings.UpstreamMessage) with
    {
        ActionText = RemoteSourceStrings.TryAgain,
        ActionCommand = actions.TryAgain,
    };

    private BannerContent SshAuth() => Dismissible(
        BannerSeverity.Error,
        RemoteSourceStrings.SshAuthTitle,
        RemoteSourceStrings.SshAuthMessage) with
    {
        ActionText = RemoteSourceStrings.TryAgain,
        ActionCommand = actions.TryAgain,
    };

    private BannerContent SshDenied() => Dismissible(
        BannerSeverity.Error,
        RemoteSourceStrings.SshDeniedTitle,
        RemoteSourceStrings.SshDeniedMessage);

    private BannerContent FingerprintMismatch() => Dismissible(
        BannerSeverity.Error,
        RemoteSourceStrings.SshFingerprintMismatchTitle,
        RemoteSourceStrings.SshFingerprintMismatchMessage);

    private BannerContent SshConnectionFailed() => Dismissible(
        BannerSeverity.Error,
        RemoteSourceStrings.SshConnectionFailedTitle,
        RemoteSourceStrings.SshConnectionFailedMessage) with
    {
        ActionText = RemoteSourceStrings.TryAgain,
        ActionCommand = actions.TryAgain,
    };

    private BannerContent Dismissible(BannerSeverity severity, string title, string? message) => new()
    {
        Severity = severity,
        Title = title,
        Message = message,
        DismissCommand = actions.Dismiss,
    };
}
