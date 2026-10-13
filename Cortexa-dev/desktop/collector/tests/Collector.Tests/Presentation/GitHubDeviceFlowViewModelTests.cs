using Collector.Application.Remote.Auth;
using Collector.Presentation.Resources;
using Collector.Presentation.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace Collector.Tests.Presentation;

public sealed class GitHubDeviceFlowViewModelTests : IDisposable
{
    private static readonly Uri AuthBase = new("https://github.com");

    private readonly FakeGitHubSignIn _signIn = new();
    private readonly SwitchableClipboard _clipboard = new();
    private readonly FakeLinkLauncher _links = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-10-13T00:00:00Z"));
    private readonly List<string> _focus = [];
    private readonly GitHubDeviceFlowViewModel _flow;

    public GitHubDeviceFlowViewModelTests() =>
        _flow = new GitHubDeviceFlowViewModel(new DeviceFlowServices(_signIn, _clipboard, _links, AuthBase), _time, _focus.Add);

    public void Dispose() => _flow.Dispose();

    private Task<bool> StartWaiting()
    {
        _signIn.CompleteGate = new TaskCompletionSource<DeviceFlowOutcome>();
        return _flow.RunAsync(RemoteAuthMethod.GitHubDevice);
    }

    private async Task<bool> RunWith(DeviceFlowOutcome outcome)
    {
        _signIn.Outcome = outcome;
        return await _flow.RunAsync(RemoteAuthMethod.GitHubDevice);
    }

    [Fact]
    public void Waiting_ShowsTheCodeLinkAndSpelledName_AndFocusesCopy()
    {
        _ = StartWaiting();

        Assert.Equal(DeviceFlowState.Waiting, _flow.State);
        Assert.Equal("WDJB-MJHT", _flow.UserCode);
        Assert.Equal("github.com/login/device", _flow.LinkText);
        Assert.Equal(GitHubAuthStrings.UserCodeName("W D J B, dash, M J H T"), _flow.UserCodeName);
        Assert.Contains(RemoteFocusKeys.DeviceCopy, _focus);
        Assert.Equal(GitHubAuthStrings.ExpiresIn("15:00"), _flow.RemainingText);
    }

    [Fact]
    public async Task Approval_ClearsThePanel_AndReportsSignedIn()
    {
        var run = StartWaiting();
        _signIn.CompleteGate!.SetResult(new DeviceFlowOutcome.SignedIn());

        Assert.True(await run);
        Assert.Equal(DeviceFlowState.Idle, _flow.State);
        Assert.Equal(string.Empty, _flow.UserCode);
        Assert.True(_signIn.IsSignedIn);
    }

    [Fact]
    public void Copy_PutsTheCodeOnTheClipboard_AndRevertsAfterTheFeedbackDelay()
    {
        _ = StartWaiting();

        _flow.CopyCodeCommand.Execute(null);

        Assert.Equal("WDJB-MJHT", _clipboard.Text);
        Assert.True(_flow.IsCopied);
        Assert.Equal(GitHubAuthStrings.CopiedAnnouncement, _flow.Announcement);
        _time.Advance(TimeSpan.FromSeconds(3));
        Assert.False(_flow.IsCopied);
    }

    [Fact]
    public void Copy_WhenTheClipboardFails_ShowsAnInlineNotice()
    {
        _clipboard.Succeeds = false;
        _ = StartWaiting();

        _flow.CopyCodeCommand.Execute(null);

        Assert.Equal(GitHubAuthStrings.CopyFailed, _flow.Notice);
        Assert.False(_flow.IsCopied);
    }

    [Fact]
    public void OpenBrowser_OpensTheVerificationPage_WhenTheHostMatchesTheAuthHost()
    {
        _ = StartWaiting();

        _flow.OpenBrowserCommand.Execute(null);

        Assert.Equal(new Uri("https://github.com/login/device"), Assert.Single(_links.Opened));
        Assert.Null(_flow.Notice);
    }

    [Fact]
    public void OpenBrowser_DoesNotOpenAnythingWhenTheHostDiffers()
    {
        _signIn.Grant = _signIn.Grant with { VerificationUri = new Uri("https://evil.example/login/device") };
        _ = StartWaiting();

        _flow.OpenBrowserCommand.Execute(null);

        Assert.Empty(_links.Opened);
        Assert.Equal(GitHubAuthStrings.HostMismatch, _flow.Notice);
    }

    [Fact]
    public void OpenBrowser_WhenTheLauncherFails_ShowsTheManualAddress()
    {
        _links.Succeeds = false;
        _ = StartWaiting();

        _flow.OpenBrowserCommand.Execute(null);

        Assert.Equal(GitHubAuthStrings.BrowserFailed("github.com/login/device"), _flow.Notice);
        Assert.Equal(DeviceFlowState.Waiting, _flow.State);
    }

    [Fact]
    public async Task Cancel_StopsTheFlow_AndStoresNothing()
    {
        var canceled = 0;
        _flow.UserCanceled += (_, _) => canceled++;
        var run = StartWaiting();

        _flow.CancelCommand.Execute(null);

        Assert.False(await run);
        Assert.Equal(1, canceled);
        Assert.Equal(DeviceFlowState.Idle, _flow.State);
        Assert.False(_signIn.IsSignedIn);
    }

    [Fact]
    public async Task Expired_ShowsTheWarningOutcomeWithAGetNewCodeAction()
    {
        Assert.False(await RunWith(new DeviceFlowOutcome.Expired()));

        Assert.Equal(DeviceFlowState.Expired, _flow.State);
        Assert.True(_flow.IsExpiredOutcome);
        Assert.Equal(GitHubAuthStrings.GetNewCode, _flow.RetryLabel);
        Assert.Equal(GitHubAuthStrings.ExpiredMessage(15), _flow.OutcomeMessage);
        Assert.Contains(RemoteFocusKeys.DeviceOutcome, _focus);
        Assert.False(_signIn.IsSignedIn);
    }

    [Fact]
    public async Task Denied_ShowsTheErrorOutcome()
    {
        Assert.False(await RunWith(new DeviceFlowOutcome.Denied()));

        Assert.Equal(DeviceFlowState.Denied, _flow.State);
        Assert.Equal(GitHubAuthStrings.DeniedTitle, _flow.OutcomeTitle);
        Assert.Equal(GitHubAuthStrings.GetNewCode, _flow.RetryLabel);
        Assert.True(_flow.IsErrorOutcome);
    }

    [Theory]
    [InlineData(DeviceFlowFailure.Network)]
    [InlineData(DeviceFlowFailure.Other)]
    public async Task Failed_ShowsTryAgainAndUseAccessToken(DeviceFlowFailure reason)
    {
        Assert.False(await RunWith(new DeviceFlowOutcome.Failed(reason)));

        Assert.Equal(DeviceFlowState.Failed, _flow.State);
        Assert.Equal(GitHubAuthStrings.FailedTitle, _flow.OutcomeTitle);
        Assert.Equal(GitHubAuthStrings.TryAgain, _flow.RetryLabel);
    }

    [Fact]
    public async Task DeviceFlowDisabled_OffersOnlyTheAccessTokenAction()
    {
        Assert.False(await RunWith(new DeviceFlowOutcome.Failed(DeviceFlowFailure.DeviceFlowDisabled)));

        Assert.Equal(DeviceFlowState.Disabled, _flow.State);
        Assert.False(_flow.HasRetry);
        Assert.Contains(RemoteFocusKeys.DeviceUsePat, _focus);
    }

    [Fact]
    public async Task StartFailure_MapsTheReasonWithoutRequestingACode()
    {
        _signIn.StartError = new DeviceFlowException(DeviceFlowFailure.DeviceFlowDisabled);

        Assert.False(await _flow.RunAsync(RemoteAuthMethod.GitHubApp));

        Assert.Equal(DeviceFlowState.Disabled, _flow.State);
        Assert.Equal(string.Empty, _flow.UserCode);
    }

    [Fact]
    public async Task UnexpectedStartFailure_ShowsTheGenericFailure()
    {
        _signIn.StartError = new HttpRequestException("offline");

        Assert.False(await _flow.RunAsync(RemoteAuthMethod.GitHubDevice));

        Assert.Equal(DeviceFlowState.Failed, _flow.State);
    }

    [Fact]
    public async Task ClockReachingZero_ShowsExpiredEvenWithoutAnAnswerFromGitHub()
    {
        var run = StartWaiting();

        _flow.UpdateRemaining(TimeSpan.Zero);

        Assert.False(await run);
        Assert.Equal(DeviceFlowState.Expired, _flow.State);
        Assert.False(_signIn.IsSignedIn);
    }

    [Fact]
    public void Countdown_WarnsUnderOneMinute_AndAnnouncesOncePerThreshold()
    {
        _ = StartWaiting();

        _flow.UpdateRemaining(TimeSpan.FromSeconds(299));
        var first = _flow.Announcement;
        _flow.UpdateRemaining(TimeSpan.FromSeconds(298));
        _flow.UpdateRemaining(TimeSpan.FromSeconds(45));

        Assert.Equal(GitHubAuthStrings.ExpiresAnnouncement(CountdownFormatter.Approximate(TimeSpan.FromSeconds(299))), first);
        Assert.Equal(GitHubAuthStrings.ExpiresAnnouncement(CountdownFormatter.Approximate(TimeSpan.FromSeconds(45))), _flow.Announcement);
        Assert.True(_flow.IsExpiringSoon);
    }

    [Fact]
    public async Task OutcomeActions_RaiseTheirEvents()
    {
        var retried = 0;
        var usedPat = 0;
        _flow.RetryRequested += (_, _) => retried++;
        _flow.UsePatRequested += (_, _) => usedPat++;
        await RunWith(new DeviceFlowOutcome.Denied());

        _flow.RetryCommand.Execute(null);
        _flow.UsePatCommand.Execute(null);

        Assert.Equal(1, retried);
        Assert.Equal(1, usedPat);
        Assert.Equal(DeviceFlowState.Idle, _flow.State);
    }
}
