using Collector.Application.Ports;
using Collector.Application.Remote.Auth;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;

namespace Collector.Infrastructure.Remote.AzureDevOps;

public sealed class MsalEntraIdAuthClient : IEntraIdAuthClient
{
    private readonly AzureDevOpsSourceOptions _options;
    private readonly MsalCacheBinding _cache;
    private readonly IWindowHandleSource _windows;
    private readonly ILogger<MsalEntraIdAuthClient> _logger;
    private readonly Lazy<IPublicClientApplication> _broker;
    private readonly Lazy<IPublicClientApplication> _plain;

    public MsalEntraIdAuthClient(
        IOptions<RemoteSourceOptions> options,
        MsalCacheBinding cache,
        IWindowHandleSource windows,
        ILogger<MsalEntraIdAuthClient> logger)
    {
        _options = options.Value.AzureDevOps;
        _cache = cache;
        _windows = windows;
        _logger = logger;
        _broker = new Lazy<IPublicClientApplication>(() => Build(useBroker: true));
        _plain = new Lazy<IPublicClientApplication>(() => Build(useBroker: false));
    }

    private bool IsConfigured => !string.IsNullOrWhiteSpace(_options.EntraClientId);

    private string[] Scopes => [_options.EntraScope];

    public async Task<EntraSignInOutcome> AcquireSilentAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return new EntraSignInOutcome.Failed(EntraFailure.Unavailable);
        }

        var outcome = await SilentAsync(_broker.Value, includeOsAccount: true, cancellationToken);
        return outcome is EntraSignInOutcome.SignedIn
            ? outcome
            : await SilentAsync(_plain.Value, includeOsAccount: false, cancellationToken);
    }

    public async Task<EntraSignInOutcome> AcquireInteractiveAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return new EntraSignInOutcome.Failed(EntraFailure.Unavailable);
        }

        var handle = _windows.GetMainWindowHandle();
        if (handle == 0)
        {
            return new EntraSignInOutcome.Failed(EntraFailure.NoWindow);
        }

        var outcome = await InteractiveAsync(_broker.Value, handle, useOsAccount: true, cancellationToken);
        return outcome is EntraSignInOutcome.Failed { Reason: EntraFailure.Unavailable }
            ? await InteractiveAsync(_plain.Value, handle, useOsAccount: false, cancellationToken)
            : outcome;
    }

    public async Task<EntraSignInOutcome> AcquireWithDeviceCodeAsync(
        Func<EntraDeviceCode, Task> onCode,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return new EntraSignInOutcome.Failed(EntraFailure.Unavailable);
        }

        try
        {
            var result = await _plain.Value
                .AcquireTokenWithDeviceCode(Scopes, code => onCode(ToDeviceCode(code)))
                .ExecuteAsync(cancellationToken);
            return new EntraSignInOutcome.SignedIn(result.AccessToken);
        }
        catch (Exception exception) when (MsalEntraErrors.IsHandled(exception))
        {
            return Fail(exception);
        }
    }

    public async Task<bool> HasAccountAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return false;
        }

        return await EntraAccountProbe.AnyAsync(
        [
            () => CachedAccountAsync(_plain.Value, cancellationToken),
            () => CachedAccountAsync(_broker.Value, cancellationToken),
            async () => await SilentAsync(_broker.Value, includeOsAccount: true, cancellationToken) is EntraSignInOutcome.SignedIn,
        ]);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (IsConfigured)
            {
                await RemoveAccountsAsync(_plain.Value);
                await RemoveAccountsAsync(_broker.Value);
            }
        }
        finally
        {
            await _cache.ClearAsync(CancellationToken.None);
        }
    }

    private static EntraDeviceCode ToDeviceCode(DeviceCodeResult code) =>
        new(code.UserCode, new Uri(code.VerificationUrl), code.ExpiresOn);

    private IPublicClientApplication Build(bool useBroker)
    {
        var builder = PublicClientApplicationBuilder.Create(_options.EntraClientId)
            .WithAuthority(_options.EntraAuthority)
            .WithRedirectUri(_options.EntraRedirectUri);
        if (useBroker)
        {
            builder = builder.WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows));
        }

        var app = builder.Build();
        _cache.Attach(app.UserTokenCache);
        return app;
    }

    private async Task<bool> CachedAccountAsync(IPublicClientApplication app, CancellationToken cancellationToken)
    {
        try
        {
            return (await app.GetAccountsAsync().WaitAsync(cancellationToken)).Any();
        }
        catch (MsalException exception)
        {
            _logger.LogWarning("Could not read the signed-in accounts ({ErrorCode}).", exception.ErrorCode);
            return false;
        }
    }

    private async Task<EntraSignInOutcome> SilentAsync(
        IPublicClientApplication app,
        bool includeOsAccount,
        CancellationToken cancellationToken)
    {
        try
        {
            var accounts = await app.GetAccountsAsync().WaitAsync(cancellationToken);
            var account = accounts.FirstOrDefault() ?? (includeOsAccount ? PublicClientApplication.OperatingSystemAccount : null);
            if (account is null)
            {
                return new EntraSignInOutcome.Failed(EntraFailure.Unavailable);
            }

            var result = await app.AcquireTokenSilent(Scopes, account).ExecuteAsync(cancellationToken);
            return new EntraSignInOutcome.SignedIn(result.AccessToken);
        }
        catch (MsalUiRequiredException)
        {
            return new EntraSignInOutcome.Failed(EntraFailure.Unavailable);
        }
        catch (Exception exception) when (MsalEntraErrors.IsHandled(exception))
        {
            return Fail(exception);
        }
    }

    private async Task<EntraSignInOutcome> InteractiveAsync(
        IPublicClientApplication app,
        nint handle,
        bool useOsAccount,
        CancellationToken cancellationToken)
    {
        try
        {
            var builder = app.AcquireTokenInteractive(Scopes).WithParentActivityOrWindow(handle);
            if (useOsAccount)
            {
                builder = builder.WithAccount(PublicClientApplication.OperatingSystemAccount);
            }

            var result = await builder.ExecuteAsync(cancellationToken);
            return new EntraSignInOutcome.SignedIn(result.AccessToken);
        }
        catch (Exception exception) when (MsalEntraErrors.IsHandled(exception))
        {
            return Fail(exception);
        }
    }

    private async Task RemoveAccountsAsync(IPublicClientApplication app)
    {
        try
        {
            foreach (var account in await app.GetAccountsAsync())
            {
                await app.RemoveAsync(account);
            }
        }
        catch (MsalException exception)
        {
            _logger.LogWarning("Could not remove a signed-in account ({ErrorCode}).", exception.ErrorCode);
        }
    }

    private EntraSignInOutcome Fail(Exception exception)
    {
        _logger.LogWarning("Microsoft sign-in did not complete ({ErrorCode}).", MsalEntraErrors.CodeOf(exception));
        return MsalEntraErrors.Map(exception);
    }
}
