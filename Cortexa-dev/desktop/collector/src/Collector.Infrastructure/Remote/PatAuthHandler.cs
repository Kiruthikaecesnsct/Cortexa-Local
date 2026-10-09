using Collector.Application.Ports;
using Collector.Application.Remote;
using Collector.Domain.Enums;

namespace Collector.Infrastructure.Remote;

public sealed class PatAuthHandler(
    ISessionCredentials credentials,
    PatScheme scheme,
    PatTarget target) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (IsConfiguredOrigin(request.RequestUri))
        {
            var pat = credentials.GetToken(target.Provider);
            if (string.IsNullOrWhiteSpace(pat))
            {
                throw new RemoteSourceException(RemoteFailureKind.MissingToken, target.Provider);
            }

            request.Headers.Authorization = PatSchemes.CreateHeader(scheme, pat);
        }

        return base.SendAsync(request, cancellationToken);
    }

    private bool IsConfiguredOrigin(Uri? uri) =>
        uri is { IsAbsoluteUri: true }
        && uri.Scheme == target.BaseAddress.Scheme
        && string.Equals(uri.Host, target.BaseAddress.Host, StringComparison.OrdinalIgnoreCase)
        && uri.Port == target.BaseAddress.Port;
}

public sealed record PatTarget(SourceType Provider, Uri BaseAddress);
