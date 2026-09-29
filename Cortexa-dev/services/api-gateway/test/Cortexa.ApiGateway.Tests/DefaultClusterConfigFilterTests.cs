using Cortexa.ApiGateway.Api;
using Microsoft.Extensions.Configuration;
using Yarp.ReverseProxy.Configuration;
using Xunit;

namespace Cortexa.ApiGateway.Tests;

public sealed class DefaultClusterConfigFilterTests
{
    [Fact]
    public async Task ConfigureClusterAsync_FillsEmptyAddress_WhenPrefixAndDomainSet()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:NamePrefix"] = "cortexa-dev",
                ["Gateway:InternalDomain"] = "proudsmoke-86efe866.eastus.azurecontainerapps.io"
            })
            .Build();

        var filter = new DefaultClusterConfigFilter(config);
        var cluster = new ClusterConfig
        {
            ClusterId = "ingestion",
            Destinations = new Dictionary<string, DestinationConfig>
            {
                ["d1"] = new DestinationConfig { Address = "" }
            }
        };

        var result = await filter.ConfigureClusterAsync(cluster, CancellationToken.None);

        Assert.NotNull(result.Destinations);
        Assert.Single(result.Destinations);
        Assert.Equal("https://cortexa-dev-ingestion.internal.proudsmoke-86efe866.eastus.azurecontainerapps.io", result.Destinations["d1"].Address);
    }

    [Fact]
    public async Task ConfigureClusterAsync_PreservesNonEmptyAddress()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:NamePrefix"] = "cortexa-dev",
                ["Gateway:InternalDomain"] = "proudsmoke-86efe866.eastus.azurecontainerapps.io"
            })
            .Build();

        var filter = new DefaultClusterConfigFilter(config);
        var cluster = new ClusterConfig
        {
            ClusterId = "ingestion",
            Destinations = new Dictionary<string, DestinationConfig>
            {
                ["d1"] = new DestinationConfig { Address = "https://localhost:5001" }
            }
        };

        var result = await filter.ConfigureClusterAsync(cluster, CancellationToken.None);

        Assert.NotNull(result.Destinations);
        Assert.Single(result.Destinations);
        Assert.Equal("https://localhost:5001", result.Destinations["d1"].Address);
    }

    [Fact]
    public async Task ConfigureClusterAsync_LeavesEmptyAddressUnchanged_WhenConfigMissing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var filter = new DefaultClusterConfigFilter(config);
        var cluster = new ClusterConfig
        {
            ClusterId = "ingestion",
            Destinations = new Dictionary<string, DestinationConfig>
            {
                ["d1"] = new DestinationConfig { Address = "" }
            }
        };

        var result = await filter.ConfigureClusterAsync(cluster, CancellationToken.None);

        Assert.NotNull(result.Destinations);
        Assert.Single(result.Destinations);
        Assert.Equal("", result.Destinations["d1"].Address);
    }

    [Fact]
    public async Task ConfigureClusterAsync_LeavesEmptyAddressUnchanged_WhenOnlyPrefixSet()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:NamePrefix"] = "cortexa-dev"
            })
            .Build();

        var filter = new DefaultClusterConfigFilter(config);
        var cluster = new ClusterConfig
        {
            ClusterId = "ingestion",
            Destinations = new Dictionary<string, DestinationConfig>
            {
                ["d1"] = new DestinationConfig { Address = "" }
            }
        };

        var result = await filter.ConfigureClusterAsync(cluster, CancellationToken.None);

        Assert.NotNull(result.Destinations);
        Assert.Single(result.Destinations);
        Assert.Equal("", result.Destinations["d1"].Address);
    }

    [Fact]
    public async Task ConfigureClusterAsync_PreservesHealthAndMetadata()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:NamePrefix"] = "cortexa-dev",
                ["Gateway:InternalDomain"] = "test.azurecontainerapps.io"
            })
            .Build();

        var filter = new DefaultClusterConfigFilter(config);
        var cluster = new ClusterConfig
        {
            ClusterId = "ingestion",
            Destinations = new Dictionary<string, DestinationConfig>
            {
                ["d1"] = new DestinationConfig
                {
                    Address = "",
                    Health = "https://localhost:5001/health",
                    Metadata = new Dictionary<string, string> { ["key"] = "value" }
                }
            }
        };

        var result = await filter.ConfigureClusterAsync(cluster, CancellationToken.None);

        Assert.NotNull(result.Destinations);
        Assert.Single(result.Destinations);
        var destination = result.Destinations["d1"];
        Assert.Equal("https://cortexa-dev-ingestion.internal.test.azurecontainerapps.io", destination.Address);
        Assert.Equal("https://localhost:5001/health", destination.Health);
        Assert.NotNull(destination.Metadata);
        Assert.Single(destination.Metadata);
        Assert.Equal("value", destination.Metadata["key"]);
    }

    [Fact]
    public async Task ConfigureClusterAsync_HandlesMultipleDestinations()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:NamePrefix"] = "cortexa-dev",
                ["Gateway:InternalDomain"] = "test.azurecontainerapps.io"
            })
            .Build();

        var filter = new DefaultClusterConfigFilter(config);
        var cluster = new ClusterConfig
        {
            ClusterId = "ingestion",
            Destinations = new Dictionary<string, DestinationConfig>
            {
                ["d1"] = new DestinationConfig { Address = "" },
                ["d2"] = new DestinationConfig { Address = "https://localhost:5002" }
            }
        };

        var result = await filter.ConfigureClusterAsync(cluster, CancellationToken.None);

        Assert.NotNull(result.Destinations);
        Assert.Equal(2, result.Destinations.Count);
        Assert.Equal("https://cortexa-dev-ingestion.internal.test.azurecontainerapps.io", result.Destinations["d1"].Address);
        Assert.Equal("https://localhost:5002", result.Destinations["d2"].Address);
    }

    [Fact]
    public async Task ConfigureClusterAsync_SetsDefaultHealthCheck()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var filter = new DefaultClusterConfigFilter(config);
        var cluster = new ClusterConfig
        {
            ClusterId = "ingestion",
            Destinations = new Dictionary<string, DestinationConfig>
            {
                ["d1"] = new DestinationConfig { Address = "" }
            }
        };

        var result = await filter.ConfigureClusterAsync(cluster, CancellationToken.None);

        Assert.NotNull(result.HealthCheck);
        Assert.NotNull(result.HealthCheck.Active);
        Assert.True(result.HealthCheck.Active.Enabled);
        Assert.Equal("/health", result.HealthCheck.Active.Path);
    }
}
