using Collector.Application.Remote.Auth;
using Collector.Presentation.Resources;
using Collector.Presentation.ViewModels;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Collector.Tests.Presentation;

public sealed class EntraSignInViewModelTests : IDisposable
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-21T00:00:00Z");

    private readonly FakeEntraSignIn _signIn = new();
    private readonly SwitchableClipboard _clipboard = new();
    private readonly FakeLinkLauncher _links = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly List<string> _focus = [];
    private readonly List<string> _started = [];
    private readonly EntraSignInViewModel _flow;

    public EntraSignInViewModelTests()
    {
        var actions = new EntraSignInActions(
            new RelayCommand(() => _started.Add("interactive")),
            new RelayCommand(() => _started.Add("code")),
            new RelayCommand(() => _started.Add("connect")),
            new RelayCommand(() => _started.Add("signout")));
        var services = new EntraSignInServices(_signIn, _clipboard, _links, NullLogger.Instance);
        _flow = new EntraSignInViewModel(services, actions, _time, _focus.Add);
    }

    public void Dispose() => _flow.Dispose();

    private static EntraDeviceCode Code(string url = "https://microsoft.com/devicelogin", int minutes = 15) =>
        new("FQ7K-2RXP", new Uri(url), Now.AddMinutes(minutes));

    private Task<bool> StartCodeFlow(EntraDeviceCode? code = null)
    {
        _signIn.Code = code ?? Code();
        _signIn.Gate = new TaskCompletionSource<EntraSignInOutcome>();
        return _flow.RunAsync(useCode: true);
    }

    [Fact]
    public async Task BrokerSignIn_MovesThroughSigningInToSignedIn()
    {
        _signIn.Gate = new TaskCompletionSource<EntraSignInOutcome>();

        var run = _flow.RunAsync(useCode: false);

        Assert.Equal(EntraSignInState.SigningIn, _flow.State);
        Assert.True(_flow.IsSigningIn);
        _signIn.Gate.SetResult(new EntraSignInOutcome.SignedIn("access"));
        Assert.True(await run);
        Assert.Equal(EntraSignInState.SignedIn, _flow.State);
        Assert.True(_flow.IsSignedIn);
        Assert.Equal(EntraAuthStrings.SignedInGeneric, _flow.SignedInLabel);
        Assert.Equal(1, _signIn.InteractiveCalls);
    }

    [Fact]
    public void DeviceCode_ShowsTheCodeAndMicrosoftSuppliedLink_AndFocusesCopy()
    {
        _ = StartCodeFlow();

        Assert.Equal(EntraSignInState.DeviceCode, _flow.State);
        Assert.Equal("FQ7K-2RXP", _flow.UserCode);
        Assert.Equal("microsoft.com/devicelogin", _flow.LinkText);
        Assert.Equal("https://microsoft.com/devicelogin", _flow.VerificationUrl);
        Assert.Equal(GitHubAuthStrings.UserCodeName("F Q 7 K, dash, 2 R X P"), _flow.UserCodeName);
        Assert.Equal(GitHubAuthStrings.ExpiresIn("15:00"), _flow.RemainingText);
        Assert.Contains(RemoteFocusKeys.EntraCopy, _focus);
        Assert.True(_flow.CancelCommand.CanExecute(null));
    }

    [Fact]
    public async Task DeviceCode_Approval_SignsIn_AndClearsTheCode()
    {
        var run = StartCodeFlow();
        _signIn.Gate!.SetResult(new EntraSignInOutcome.SignedIn("access"));

        Assert.True(await run);
        Assert.Equal(EntraSignInState.SignedIn, _flow.State);
        Assert.Equal(string.Empty, _flow.UserCode);
        Assert.False(_flow.CancelCommand.CanExecute(null));
    }

    [Fact]
    public void Copy_PutsTheCodeOnTheClipboard_AndRevertsAfterTheFeedbackDelay()
    {
        _ = StartCodeFlow();

        _flow.CopyCodeCommand.Execute(null);

        Assert.Equal("FQ7K-2RXP", _clipboard.Text);
        Assert.True(_flow.IsCopied);
        Assert.Equal(GitHubAuthStrings.CopiedAnnouncement, _flow.Announcement);
        _time.Advance(TimeSpan.FromSeconds(3));
        Assert.False(_flow.IsCopied);
    }

    [Fact]
    public void Copy_WhenTheClipboardFails_ShowsAnInlineNotice()
    {
        _clipboard.Succeeds = false;
        _ = StartCodeFlow();

        _flow.CopyCodeCommand.Execute(null);

        Assert.Equal(GitHubAuthStrings.CopyFailed, _flow.Notice);
        Assert.False(_flow.IsCopied);
    }

    [Fact]
    public void OpenBrowser_OpensTheHttpsAddressMicrosoftSent()
    {
        _ = StartCodeFlow();

        _flow.OpenBrowserCommand.Execute(null);

        Assert.Equal([new Uri("https://microsoft.com/devicelogin")], _links.Opened);
        Assert.Null(_flow.Notice);
    }

    [Fact]
    public void OpenBrowser_RefusesAnInsecureAddress_AndShowsTheUnsafeNotice()
    {
        _ = StartCodeFlow(Code("http://microsoft.com/devicelogin"));

        _flow.OpenBrowserCommand.Execute(null);

        Assert.Empty(_links.Opened);
        Assert.Equal(EntraAuthStrings.UnsafeUrl, _flow.Notice);
    }

    [Fact]
    public void OpenBrowser_WhenTheLauncherFails_ShowsTheManualFallback()
    {
        _links.Succeeds = false;
        _ = StartCodeFlow();

        _flow.OpenBrowserCommand.Execute(null);

        Assert.Equal(GitHubAuthStrings.BrowserFailed("microsoft.com/devicelogin"), _flow.Notice);
    }

    [Fact]
    public async Task Cancel_ReturnsToIdle_StoresNothing_AndRaisesTheCanceledEvent()
    {
        var canceled = 0;
        _flow.UserCanceled += (_, _) => canceled++;
        var run = StartCodeFlow();

        _flow.CancelCommand.Execute(null);

        Assert.False(await run);
        Assert.Equal(EntraSignInState.Idle, _flow.State);
        Assert.False(_signIn.IsSignedIn);
        Assert.Equal(string.Empty, _flow.UserCode);
        Assert.Equal(1, canceled);
    }

    [Fact]
    public async Task ClockExpiry_ShowsTheExpiredOutcome_WithMinutesFromTheServiceExpiry()
    {
        var run = StartCodeFlow(Code(minutes: 10));

        _flow.UpdateRemaining(TimeSpan.Zero);

        Assert.False(await run);
        Assert.Equal(EntraSignInState.Expired, _flow.State);
        Assert.Equal(EntraOutcomeKind.Expired, _flow.OutcomeKind);
        Assert.True(_flow.IsExpiredOutcome);
        Assert.Equal(GitHubAuthStrings.ExpiredMessage(10), _flow.OutcomeMessage);
        Assert.Equal(GitHubAuthStrings.GetNewCode, _flow.RetryLabel);
        Assert.False(_signIn.IsSignedIn);
        Assert.Contains(RemoteFocusKeys.EntraOutcome, _focus);
    }

    [Fact]
    public async Task ExpiredOutcomeFromTheService_ShowsTheExpiredCard()
    {
        var run = StartCodeFlow();
        _signIn.Gate!.SetResult(new EntraSignInOutcome.Expired());

        Assert.False(await run);
        Assert.Equal(EntraSignInState.Expired, _flow.State);
        Assert.Equal(EntraAuthStrings.ExpiredTitle, _flow.OutcomeTitle);
        Assert.True(_flow.ShowOutcome);
    }

    [Fact]
    public async Task ClosedMicrosoftWindow_ShowsTheUserCanceledOutcome_WithSignInAsRetry()
    {
        _signIn.Outcome = new EntraSignInOutcome.Cancelled();

        Assert.False(await _flow.RunAsync(useCode: false));

        Assert.Equal(EntraSignInState.Failed, _flow.State);
        Assert.Equal(EntraOutcomeKind.UserCanceled, _flow.OutcomeKind);
        Assert.Equal(EntraAuthStrings.UserCanceledTitle, _flow.OutcomeTitle);
        Assert.Equal(EntraAuthStrings.SignInName, _flow.RetryLabel);
        Assert.False(_flow.ShowCodeFallback);
        Assert.False(_signIn.IsSignedIn);
    }

    [Theory]
    [InlineData(EntraFailure.NoWindow)]
    [InlineData(EntraFailure.Unavailable)]
    [InlineData(EntraFailure.Other)]
    public async Task FailedOutcome_ShowsTheErrorCard_AndOffersTheCodeFallback(EntraFailure reason)
    {
        _signIn.Outcome = new EntraSignInOutcome.Failed(reason);

        Assert.False(await _flow.RunAsync(useCode: false));

        Assert.Equal(EntraSignInState.Failed, _flow.State);
        Assert.Equal(EntraOutcomeKind.Error, _flow.OutcomeKind);
        Assert.Equal(EntraAuthStrings.FailedMessage, _flow.OutcomeMessage);
        Assert.Equal(GitHubAuthStrings.TryAgain, _flow.RetryLabel);
        Assert.True(_flow.ShowCodeFallback);
        Assert.Contains(RemoteFocusKeys.EntraOutcome, _focus);
    }

    [Fact]
    public async Task UnexpectedException_ShowsTheErrorCard_WithoutLeakingItsMessage()
    {
        _signIn.SignInError = new InvalidOperationException("alex@contoso.com secret");

        Assert.False(await _flow.RunAsync(useCode: false));

        Assert.Equal(EntraSignInState.Failed, _flow.State);
        Assert.DoesNotContain("alex", _flow.OutcomeName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Retry_RepeatsTheSameKindOfSignIn()
    {
        _signIn.Outcome = new EntraSignInOutcome.Failed(EntraFailure.Other);
        await _flow.RunAsync(useCode: true);
        _flow.RetryCommand.Execute(null);

        _signIn.Outcome = new EntraSignInOutcome.Cancelled();
        await _flow.RunAsync(useCode: false);
        _flow.RetryCommand.Execute(null);

        Assert.Equal(["code", "interactive"], _started);
    }

    [Fact]
    public async Task UsePat_ClearsTheOutcome_AndRaisesTheRequest()
    {
        var requested = 0;
        _flow.UsePatRequested += (_, _) => requested++;
        _signIn.Outcome = new EntraSignInOutcome.Failed(EntraFailure.Other);
        await _flow.RunAsync(useCode: false);

        _flow.UsePatCommand.Execute(null);

        Assert.Equal(EntraSignInState.Idle, _flow.State);
        Assert.False(_flow.ShowOutcome);
        Assert.Equal(1, requested);
    }

    [Fact]
    public void Countdown_AnnouncesOnlyAtTheFiveMinuteOneMinuteAndThirtySecondMarks()
    {
        _ = StartCodeFlow();

        _flow.UpdateRemaining(TimeSpan.FromMinutes(10));
        Assert.Equal(string.Empty, _flow.Announcement);
        _flow.UpdateRemaining(TimeSpan.FromSeconds(299));
        Assert.NotEqual(string.Empty, _flow.Announcement);
        var first = _flow.Announcement;
        _flow.UpdateRemaining(TimeSpan.FromSeconds(200));
        Assert.Equal(first, _flow.Announcement);
        _flow.UpdateRemaining(TimeSpan.FromSeconds(30));
        Assert.NotEqual(first, _flow.Announcement);
        Assert.True(_flow.IsExpiringSoon);
    }

    [Fact]
    public async Task SignOut_RemovesTheSignIn_AndReturnsToIdle()
    {
        _signIn.IsSignedIn = true;
        _signIn.Gate = null;
        _signIn.Outcome = new EntraSignInOutcome.SignedIn("access");
        await _flow.RunAsync(useCode: false);

        Assert.True(await _flow.SignOutAsync(CancellationToken.None));

        Assert.Equal(EntraSignInState.Idle, _flow.State);
        Assert.Equal(1, _signIn.SignOutCalls);
    }

    [Fact]
    public async Task SignOut_WhenRemovalFails_KeepsTheSignedInCard()
    {
        await _flow.RunAsync(useCode: false);
        _signIn.SignOutError = new IOException("store locked");

        Assert.False(await _flow.SignOutAsync(CancellationToken.None));

        Assert.True(_flow.IsSignedIn);
        Assert.False(_flow.IsSigningOut);
    }
}
