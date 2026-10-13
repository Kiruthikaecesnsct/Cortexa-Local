using System.Net;
using Collector.Application.Remote.Auth;
using Collector.Infrastructure.Remote.GitHub;
using Collector.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Collector.Tests.Remote;

public sealed class GitHubDeviceAuthorizationClientTests
{
    private const string AuthOrigin = "https://github.com/";
    private const string OAuthClientId = "oauth-client";
    private const string AppClientId = "app-client";
    private const int SlowDownSeconds = 10;
    private const int AccessLifetimeSeconds = 28800;
    private const int RefreshLifetimeSeconds = 15897600;

    private static readonly DateTimeOffset Start = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Start);

    private (GitHubDeviceAuthorizationClient Client, StubHttpHandler Handler) Build(HttpStatusCode status, string body)
    {
        var handler = StubHttpHandler.Returning(status, body);
        var factory = new BaseAddressClientFactory(handler, AuthOrigin);
        var options = RemoteData.Options(o =>
        {
            o.GitHub.OAuthClientId = OAuthClientId;
            o.GitHub.AppClientId = AppClientId;
            o.GitHub.OAuthScopes = "repo read:org";
        });
        return (new GitHubDeviceAuthorizationClient(factory, options, _time), handler);
    }

    private static string Success() =>
        $$"""{"access_token":"ghu_a","refresh_token":"ghr_r","expires_in":{{AccessLifetimeSeconds}},"refresh_token_expires_in":{{RefreshLifetimeSeconds}},"token_type":"bearer"}""";

    [Fact]
    public async Task RequestCodeAsync_Device_SendsClientIdAndScopeAndParsesTheGrant()
    {
        var (client, handler) = Build(HttpStatusCode.OK, """{"device_code":"dc","user_code":"AB-CD","verification_uri":"https://github.com/login/device","expires_in":900,"interval":5}""");

        var grant = await client.RequestCodeAsync(RemoteAuthMethod.GitHubDevice, TestSupport.Ct);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://github.com/login/device/code", request.Uri!.AbsoluteUri);
        Assert.Equal("application/json", request.Headers["Accept"]);
        Assert.Equal($"client_id={OAuthClientId}&scope=repo+read%3Aorg", request.Body);
        Assert.Equal("dc", grant.DeviceCode);
        Assert.Equal("AB-CD", grant.UserCode);
        Assert.Equal(TimeSpan.FromSeconds(900), grant.ExpiresIn);
        Assert.Equal(TimeSpan.FromSeconds(5), grant.Interval);
    }

    [Fact]
    public async Task RequestCodeAsync_App_UsesTheAppClientIdWithoutScope()
    {
        var (client, handler) = Build(HttpStatusCode.OK, """{"device_code":"dc","user_code":"AB-CD","verification_uri":"https://github.com/login/device","expires_in":900,"interval":5}""");

        await client.RequestCodeAsync(RemoteAuthMethod.GitHubApp, TestSupport.Ct);

        Assert.Equal($"client_id={AppClientId}", handler.Requests.Single().Body);
    }

    [Fact]
    public async Task RequestCodeAsync_DeviceFlowDisabledInA200Body_ThrowsTheTypedException()
    {
        var (client, _) = Build(HttpStatusCode.OK, """{"error":"device_flow_disabled"}""");

        var exception = await Assert.ThrowsAsync<DeviceFlowException>(
            () => client.RequestCodeAsync(RemoteAuthMethod.GitHubDevice, TestSupport.Ct));

        Assert.Equal(DeviceFlowFailure.DeviceFlowDisabled, exception.Reason);
    }

    [Fact]
    public async Task RequestCodeAsync_OtherErrorInA200Body_ThrowsOther()
    {
        var (client, _) = Build(HttpStatusCode.OK, """{"error":"incorrect_client_credentials"}""");

        var exception = await Assert.ThrowsAsync<DeviceFlowException>(
            () => client.RequestCodeAsync(RemoteAuthMethod.GitHubDevice, TestSupport.Ct));

        Assert.Equal(DeviceFlowFailure.Other, exception.Reason);
    }

    [Fact]
    public async Task RequestCodeAsync_ServerError_ThrowsNetwork()
    {
        var (client, _) = Build(HttpStatusCode.InternalServerError, "{}");

        var exception = await Assert.ThrowsAsync<DeviceFlowException>(
            () => client.RequestCodeAsync(RemoteAuthMethod.GitHubDevice, TestSupport.Ct));

        Assert.Equal(DeviceFlowFailure.Network, exception.Reason);
    }

    [Fact]
    public async Task RequestCodeAsync_NonHttpsVerificationUri_ThrowsOther()
    {
        var (client, _) = Build(HttpStatusCode.OK, """{"device_code":"dc","user_code":"AB-CD","verification_uri":"http://evil.example/device","expires_in":900,"interval":5}""");

        var exception = await Assert.ThrowsAsync<DeviceFlowException>(
            () => client.RequestCodeAsync(RemoteAuthMethod.GitHubDevice, TestSupport.Ct));

        Assert.Equal(DeviceFlowFailure.Other, exception.Reason);
    }

    [Fact]
    public async Task PollAsync_SendsTheDeviceGrantForm()
    {
        var (client, handler) = Build(HttpStatusCode.OK, """{"error":"authorization_pending"}""");

        await client.PollAsync(RemoteAuthMethod.GitHubDevice, "dc", TestSupport.Ct);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://github.com/login/oauth/access_token", request.Uri!.AbsoluteUri);
        Assert.Equal(
            $"client_id={OAuthClientId}&device_code=dc&grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Adevice_code",
            request.Body);
    }

    [Theory]
    [InlineData("authorization_pending", typeof(DevicePollResult.Pending))]
    [InlineData("expired_token", typeof(DevicePollResult.Expired))]
    [InlineData("access_denied", typeof(DevicePollResult.Denied))]
    [InlineData("incorrect_device_code", typeof(DevicePollResult.Failed))]
    [InlineData("unsupported_grant_type", typeof(DevicePollResult.Failed))]
    [InlineData("something_new", typeof(DevicePollResult.Failed))]
    public async Task PollAsync_ErrorInA200Body_MapsToTheMatchingResult(string error, Type expected)
    {
        var (client, _) = Build(HttpStatusCode.OK, $$"""{"error":"{{error}}"}""");

        var result = await client.PollAsync(RemoteAuthMethod.GitHubDevice, "dc", TestSupport.Ct);

        Assert.IsType(expected, result);
    }

    [Fact]
    public async Task PollAsync_DeviceFlowDisabled_IsATypedFailure()
    {
        var (client, _) = Build(HttpStatusCode.OK, """{"error":"device_flow_disabled"}""");

        var result = await client.PollAsync(RemoteAuthMethod.GitHubDevice, "dc", TestSupport.Ct);

        Assert.Equal(new DevicePollResult.Failed(DeviceFlowFailure.DeviceFlowDisabled), result);
    }

    [Fact]
    public async Task PollAsync_SlowDownWithInterval_CarriesTheNewInterval()
    {
        var (client, _) = Build(HttpStatusCode.OK, $$"""{"error":"slow_down","interval":{{SlowDownSeconds}}}""");

        var result = await client.PollAsync(RemoteAuthMethod.GitHubDevice, "dc", TestSupport.Ct);

        Assert.Equal(new DevicePollResult.SlowDown(TimeSpan.FromSeconds(SlowDownSeconds)), result);
    }

    [Fact]
    public async Task PollAsync_SlowDownWithoutInterval_HasNoNewInterval()
    {
        var (client, _) = Build(HttpStatusCode.OK, """{"error":"slow_down"}""");

        var result = await client.PollAsync(RemoteAuthMethod.GitHubDevice, "dc", TestSupport.Ct);

        Assert.Equal(new DevicePollResult.SlowDown(null), result);
    }

    [Fact]
    public async Task PollAsync_Success_ComputesBothExpiryTimes()
    {
        var (client, _) = Build(HttpStatusCode.OK, Success());

        var result = await client.PollAsync(RemoteAuthMethod.GitHubApp, "dc", TestSupport.Ct);

        var success = Assert.IsType<DevicePollResult.Success>(result);
        Assert.Equal("ghu_a", success.Tokens.AccessToken);
        Assert.Equal("ghr_r", success.Tokens.RefreshToken);
        Assert.Equal(Start.AddSeconds(AccessLifetimeSeconds), success.Tokens.AccessExpiresAt);
        Assert.Equal(Start.AddSeconds(RefreshLifetimeSeconds), success.Tokens.RefreshExpiresAt);
    }

    [Fact]
    public async Task PollAsync_SuccessWithoutExpiry_LeavesTheExpiriesEmpty()
    {
        var (client, _) = Build(HttpStatusCode.OK, """{"access_token":"gho_a","token_type":"bearer"}""");

        var result = await client.PollAsync(RemoteAuthMethod.GitHubDevice, "dc", TestSupport.Ct);

        var success = Assert.IsType<DevicePollResult.Success>(result);
        Assert.Null(success.Tokens.AccessExpiresAt);
        Assert.Null(success.Tokens.RefreshToken);
    }

    [Fact]
    public async Task PollAsync_ServerError_IsANetworkFailure()
    {
        var (client, _) = Build(HttpStatusCode.BadGateway, "{}");

        var result = await client.PollAsync(RemoteAuthMethod.GitHubDevice, "dc", TestSupport.Ct);

        Assert.Equal(new DevicePollResult.Failed(DeviceFlowFailure.Network), result);
    }

    [Fact]
    public async Task RefreshAsync_Success_ReturnsTheRotatedPair()
    {
        var (client, handler) = Build(HttpStatusCode.OK, Success());

        var result = await client.RefreshAsync(RemoteAuthMethod.GitHubApp, "old", TestSupport.Ct);

        Assert.IsType<RefreshResult.Refreshed>(result);
        Assert.Equal($"client_id={AppClientId}&grant_type=refresh_token&refresh_token=old", handler.Requests.Single().Body);
    }

    [Fact]
    public async Task RefreshAsync_BadRefreshToken_IsRejected()
    {
        var (client, _) = Build(HttpStatusCode.OK, """{"error":"bad_refresh_token"}""");

        var result = await client.RefreshAsync(RemoteAuthMethod.GitHubApp, "old", TestSupport.Ct);

        Assert.IsType<RefreshResult.Rejected>(result);
    }

    [Fact]
    public async Task RefreshAsync_ServerError_IsANetworkFailure()
    {
        var (client, _) = Build(HttpStatusCode.InternalServerError, "{}");

        var result = await client.RefreshAsync(RemoteAuthMethod.GitHubApp, "old", TestSupport.Ct);

        Assert.Equal(new RefreshResult.Failed(DeviceFlowFailure.Network), result);
    }
}
