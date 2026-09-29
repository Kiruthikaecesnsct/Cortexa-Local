using Cortexa.ApiGateway.Api.Auth;
using Microsoft.Extensions.Configuration;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace Cortexa.ApiGateway.Api;

public sealed class DefaultClusterConfigFilter : IProxyConfigFilter
{
    private readonly string _namePrefix;
    private readonly string _internalDomain;

    public DefaultClusterConfigFilter(IConfiguration configuration)
    {
        _namePrefix = configuration["Gateway:NamePrefix"] ?? string.Empty;
        _internalDomain = configuration["Gateway:InternalDomain"] ?? string.Empty;
    }

    public ValueTask<ClusterConfig> ConfigureClusterAsync(ClusterConfig cluster, CancellationToken cancel)
    {
        var defaultHealthCheck = BuildDefaultHealthCheck();
        var defaultHttpRequest = BuildDefaultHttpRequest();
        var metadata = EnsureMetadataDefaults(cluster.Metadata);
        var destinations = FillDestinationAddresses(cluster);

        return new ValueTask<ClusterConfig>(cluster with
        {
            HealthCheck = cluster.HealthCheck ?? defaultHealthCheck,
            HttpRequest = cluster.HttpRequest ?? defaultHttpRequest,
            Metadata = metadata,
            Destinations = destinations
        });
    }

    private IReadOnlyDictionary<string, DestinationConfig> FillDestinationAddresses(ClusterConfig cluster)
    {
        var existing = cluster.Destinations ?? new Dictionary<string, DestinationConfig>();
        if (!ShouldRewriteAddresses() || existing.Count == 0)
        {
            return existing;
        }

        var address = ComputeInternalAddress(cluster.ClusterId);
        var updated = new Dictionary<string, DestinationConfig>();
        foreach (var kvp in existing)
        {
            updated[kvp.Key] = WithComputedAddress(kvp.Value, address);
        }
        return updated;
    }

    private bool ShouldRewriteAddresses()
        => !string.IsNullOrWhiteSpace(_namePrefix) && !string.IsNullOrWhiteSpace(_internalDomain);

    private string ComputeInternalAddress(string? clusterId)
        => $"https://{_namePrefix}-{clusterId}.internal.{_internalDomain}";

    private static DestinationConfig WithComputedAddress(DestinationConfig destination, string address)
        => string.IsNullOrWhiteSpace(destination.Address)
            ? destination with { Address = address }
            : destination;

    public ValueTask<RouteConfig> ConfigureRouteAsync(RouteConfig route, ClusterConfig? cluster, CancellationToken cancel)
    {
        var authMetadata = BuildAuthorizationMetadata(route);
        var updatedRoute = route with { Metadata = authMetadata };
        return new ValueTask<RouteConfig>(updatedRoute);
    }

    private static IReadOnlyDictionary<string, string> BuildAuthorizationMetadata(RouteConfig route)
    {
        var metadata = new Dictionary<string, string>();
        if (route.Metadata is not null)
        {
            foreach (var kv in route.Metadata)
            {
                metadata[kv.Key] = kv.Value;
            }
        }

        if (route.Metadata?.TryGetValue("AllowAnonymous", out var allowAnonymous) == true
            && string.Equals(allowAnonymous, "true", StringComparison.OrdinalIgnoreCase))
        {
            metadata["Authorization"] = "Anonymous";
        }

        if (route.Metadata?.TryGetValue("RequiredRole", out var requiredRole) == true
            && !string.IsNullOrWhiteSpace(requiredRole))
        {
            metadata["RequiredRole"] = requiredRole;
        }

        return metadata;
    }

    private static HealthCheckConfig BuildDefaultHealthCheck()
    {
        return new HealthCheckConfig
        {
            Active = new ActiveHealthCheckConfig
            {
                Enabled = true,
                Policy = "ConsecutiveFailures",
                Interval = TimeSpan.FromSeconds(10),
                Timeout = TimeSpan.FromSeconds(5),
                Path = "/health"
            },
            Passive = new PassiveHealthCheckConfig
            {
                Enabled = true,
                Policy = "TransportFailureRate",
                ReactivationPeriod = TimeSpan.FromMinutes(1)
            }
        };
    }

    private static ForwarderRequestConfig BuildDefaultHttpRequest()
    {
        return new ForwarderRequestConfig
        {
            ActivityTimeout = TimeSpan.FromMinutes(1)
        };
    }

    private static IReadOnlyDictionary<string, string> EnsureMetadataDefaults(IReadOnlyDictionary<string, string>? existing)
    {
        var metadata = existing ?? new Dictionary<string, string>();
        if (!metadata.ContainsKey("ConsecutiveFailuresHealthPolicy.Threshold"))
        {
            var updated = new Dictionary<string, string>();
            foreach (var kv in metadata)
            {
                updated[kv.Key] = kv.Value;
            }
            updated["ConsecutiveFailuresHealthPolicy.Threshold"] = "3";
            return updated;
        }
        return metadata;
    }
}
