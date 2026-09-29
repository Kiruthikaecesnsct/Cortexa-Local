using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Cortexa.JobOrchestrator.Infrastructure.Messaging;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cortexa.JobOrchestrator.Application.Tests;

public sealed class RetryPolicyTests
{
    private static ExponentialBackoffRetryPolicy CreatePolicy(int maxRetries = 3, int[]? backoff = null)
    {
        var settings = new ServiceBusSettings
        {
            MaxRetries = maxRetries,
            RetryBackoffSeconds = backoff ?? [1, 2, 4]
        };
        return new ExponentialBackoffRetryPolicy(Options.Create(settings));
    }

    [Fact]
    public void ShouldRetry_Attempt0_ReturnsTrue()
    {
        var policy = CreatePolicy();
        policy.ShouldRetry(0).Should().BeTrue();
    }

    [Fact]
    public void DelayFor_Attempt0_Returns1Second()
    {
        var policy = CreatePolicy();
        policy.DelayFor(0).Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void DelayFor_Attempt1_Returns1Second()
    {
        var policy = CreatePolicy();
        policy.DelayFor(1).Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void DelayFor_Attempt2_Returns2Seconds()
    {
        var policy = CreatePolicy();
        policy.DelayFor(2).Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void DelayFor_Attempt3_Returns4Seconds()
    {
        var policy = CreatePolicy();
        policy.DelayFor(3).Should().Be(TimeSpan.FromSeconds(4));
    }

    [Fact]
    public void ShouldRetry_Attempt1_ReturnsTrue()
    {
        var policy = CreatePolicy();
        policy.ShouldRetry(1).Should().BeTrue();
    }

    [Fact]
    public void ShouldRetry_Attempt3_ReturnsTrue()
    {
        var policy = CreatePolicy();
        policy.ShouldRetry(3).Should().BeTrue();
    }

    [Fact]
    public void ShouldRetry_Attempt4_ReturnsFalse()
    {
        var policy = CreatePolicy();
        policy.ShouldRetry(4).Should().BeFalse();
    }

    [Fact]
    public void MaxRetries_ReturnsConfiguredValue()
    {
        var policy = CreatePolicy(maxRetries: 5);
        policy.MaxRetries.Should().Be(5);
    }
}
