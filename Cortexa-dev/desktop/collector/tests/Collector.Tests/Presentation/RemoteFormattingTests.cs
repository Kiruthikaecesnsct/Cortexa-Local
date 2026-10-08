using System.Windows.Input;
using Collector.Application.Remote;
using Collector.Domain.Enums;
using Collector.Presentation.ViewModels;

namespace Collector.Tests.Presentation;

public sealed class RemoteFormattingTests
{
    private sealed class NoopCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
        }
    }

    private static RemoteBannerFactory Factory()
    {
        var command = new NoopCommand();
        return new RemoteBannerFactory(new RemoteBannerActions(command, command, command, command, command, command));
    }

    private static RemoteFailureContext Context(SourceType provider) =>
        new(provider, "octo/hello", "main", "octo", "612 MB", "500 MB");

    [Theory]
    [InlineData(0L, "0 KB")]
    [InlineData(917504L, "896 KB")]
    [InlineData(3250585L, "3.1 MB")]
    [InlineData(50540134L, "48.2 MB")]
    [InlineData(224395264L, "214 MB")]
    [InlineData(524288000L, "500 MB")]
    [InlineData(1073741824L, "1.0 GB")]
    [InlineData(778043392L, "742 MB")]
    public void Size_IsFormattedForTheRepositoryList(long bytes, string expected) =>
        Assert.Equal(expected, RemoteSizeFormatter.Format(bytes));

    [Theory]
    [InlineData("contoso", "contoso")]
    [InlineData("  contoso  ", "contoso")]
    [InlineData("https://dev.azure.com/contoso", "contoso")]
    [InlineData("https://dev.azure.com/contoso/Research/_git/repo", "contoso")]
    [InlineData("dev.azure.com/contoso", "contoso")]
    [InlineData("https://contoso.visualstudio.com", "contoso")]
    [InlineData("https://dev.azure.com/", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Organization_IsExtractedFromPastedText(string? raw, string expected) =>
        Assert.Equal(expected, AzureOrganizationParser.Parse(raw));

    [Theory]
    [InlineData(RemoteFailureKind.MissingToken, BannerSeverity.Warning, "Open Settings", false)]
    [InlineData(RemoteFailureKind.Auth, BannerSeverity.Error, "Open Settings", false)]
    [InlineData(RemoteFailureKind.AccessDenied, BannerSeverity.Error, "Open Settings", false)]
    [InlineData(RemoteFailureKind.SsoRequired, BannerSeverity.Error, "Open GitHub token settings", false)]
    [InlineData(RemoteFailureKind.NotFound, BannerSeverity.Error, "Reload repositories", false)]
    [InlineData(RemoteFailureKind.EmptyRepository, BannerSeverity.Warning, null, true)]
    [InlineData(RemoteFailureKind.RepositoryTooLarge, BannerSeverity.Error, "Local files", true)]
    [InlineData(RemoteFailureKind.RateLimited, BannerSeverity.Warning, "Try again", true)]
    [InlineData(RemoteFailureKind.Upstream, BannerSeverity.Error, "Try again", true)]
    public void GitHubFailure_MapsToTheDocumentedBanner(RemoteFailureKind kind, BannerSeverity severity, string? action, bool dismissible)
    {
        var banner = Factory().ForFailure(kind, Context(SourceType.Github));

        Assert.Equal(severity, banner.Severity);
        Assert.Equal(action, banner.ActionText);
        Assert.Equal(dismissible, banner.DismissCommand is not null);
    }

    [Fact]
    public void SsoFailure_ForAzureDevOps_FallsBackToTheAuthBanner()
    {
        var banner = Factory().ForFailure(RemoteFailureKind.SsoRequired, Context(SourceType.AzureDevops));

        Assert.Equal("Azure DevOps didn't accept your token.", banner.Title);
        Assert.Equal("Open Settings", banner.ActionText);
    }

    [Fact]
    public void TooLargeFailure_NamesTheRepositoryItsSizeAndTheLimit()
    {
        var banner = Factory().ForFailure(RemoteFailureKind.RepositoryTooLarge, Context(SourceType.Github));

        Assert.Contains("octo/hello is 612 MB", banner.Message, StringComparison.Ordinal);
        Assert.Contains("500 MB", banner.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RateLimitedFailure_WithResetTime_NamesTheWait()
    {
        var context = Context(SourceType.Github) with { RetryAfter = TimeSpan.FromMinutes(4) };

        var banner = Factory().ForFailure(RemoteFailureKind.RateLimited, context);

        Assert.Equal("Try again in about 4 minutes.", banner.Message);
    }

    [Fact]
    public void PausedBanner_AnnouncesTheApproximateWaitButShowsTheExactCountdown()
    {
        var banner = Factory().Paused(SourceType.AzureDevops, TimeSpan.FromSeconds(252));

        Assert.Equal("Azure DevOps is limiting requests.", banner.Title);
        Assert.Equal("Resuming in 4:12. The fetch continues on its own.", banner.Message);
        Assert.Equal("Azure DevOps is limiting requests. Resuming in about 4 minutes.", banner.AutomationName);
        Assert.Null(banner.DismissCommand);
    }
}
