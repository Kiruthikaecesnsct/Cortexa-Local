using System.Net.Http.Headers;
using Collector.Application.Ports;
using Collector.Application.Remote;
using Collector.Application.Remote.Auth;

namespace Collector.Infrastructure.Remote;

public sealed class RemoteTokenAuthHandler(
    IRemoteTokenResolver resolver,
    PatScheme patScheme,
    PatTarget target) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (RemoteOrigin.IsSame(request.RequestUri, target.BaseAddress))
        {
            var token = await resolver.ResolveAsync(target.Provider, cancellationToken);
            if (token is null || string.IsNullOrWhiteSpace(token.Value))
            {
                throw new RemoteSourceException(RemoteFailureKind.MissingToken, target.Provider);
            }

            request.Headers.Authorization = CreateHeader(token);
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private AuthenticationHeaderValue CreateHeader(RemoteAccessToken token) =>
        token.Kind == RemoteTokenKind.OAuth
            ? new AuthenticationHeaderValue("Bearer", token.Value)
            : PatSchemes.CreateHeader(patScheme, token.Value);
}
