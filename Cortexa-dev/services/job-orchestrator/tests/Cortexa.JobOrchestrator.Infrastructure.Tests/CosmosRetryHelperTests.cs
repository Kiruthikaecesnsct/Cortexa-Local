using System.Net;
using System.Net.Sockets;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Infrastructure.Helpers;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using NSubstitute;
using Xunit;

namespace Cortexa.JobOrchestrator.Infrastructure.Tests;

public sealed class CosmosRetryHelperTests
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan TotalBudget = TimeSpan.FromMilliseconds(100);

    private static IRetryPolicy BuildPolicy(int maxRetries = MaxRetries)
    {
        var policy = Substitute.For<IRetryPolicy>();
        policy.MaxRetries.Returns(maxRetries);
        policy.ShouldRetry(Arg.Any<int>()).Returns(x => (int)x[0] <= maxRetries);
        policy.DelayFor(Arg.Any<int>()).Returns(TimeSpan.FromMilliseconds(10));
        return policy;
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(503)]
    public async Task ExecuteWithRetryAsync_TransientCosmosException_Retries(int statusCode)
    {
        var policy = BuildPolicy();
        var attempt = 0;

        var result = await CosmosRetryHelper.ExecuteWithRetryAsync(
            async () =>
            {
                attempt++;
                if (attempt == 1)
                    throw new CosmosException("transient", (HttpStatusCode)statusCode, 0, "act-1", 0);
                return "success";
            },
            policy,
            TotalBudget);

        result.Should().Be("success");
        attempt.Should().Be(2);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(412)]
    [InlineData(403)]
    public async Task ExecuteWithRetryAsync_NonTransientCosmosException_DoesNotRetry(int statusCode)
    {
        var policy = BuildPolicy();
        var attempt = 0;

        var act = async () => await CosmosRetryHelper.ExecuteWithRetryAsync(
            async () =>
            {
                attempt++;
                throw new CosmosException("non-transient", (HttpStatusCode)statusCode, 0, "act-1", 0);
            },
            policy,
            TotalBudget);

        await act.Should().ThrowAsync<CosmosException>();
        attempt.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_HttpRequestException_Retries()
    {
        var policy = BuildPolicy();
        var attempt = 0;

        var result = await CosmosRetryHelper.ExecuteWithRetryAsync(
            async () =>
            {
                attempt++;
                if (attempt == 1)
                    throw new HttpRequestException("network error");
                return "success";
            },
            policy,
            TotalBudget);

        result.Should().Be("success");
        attempt.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_SocketException_Retries()
    {
        var policy = BuildPolicy();
        var attempt = 0;

        var result = await CosmosRetryHelper.ExecuteWithRetryAsync(
            async () =>
            {
                attempt++;
                if (attempt == 1)
                    throw new SocketException((int)SocketError.ConnectionReset);
                return "success";
            },
            policy,
            TotalBudget);

        result.Should().Be("success");
        attempt.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_TimeoutException_Retries()
    {
        var policy = BuildPolicy();
        var attempt = 0;

        var result = await CosmosRetryHelper.ExecuteWithRetryAsync(
            async () =>
            {
                attempt++;
                if (attempt == 1)
                    throw new TimeoutException("operation timed out");
                return "success";
            },
            policy,
            TotalBudget);

        result.Should().Be("success");
        attempt.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_TaskCanceledException_DoesNotRetry()
    {
        var policy = BuildPolicy();
        var attempt = 0;

        var act = async () => await CosmosRetryHelper.ExecuteWithRetryAsync(
            async () =>
            {
                attempt++;
                throw new TaskCanceledException("operation canceled");
            },
            policy,
            TotalBudget);

        await act.Should().ThrowAsync<TaskCanceledException>();
        attempt.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_OperationCanceledException_DoesNotRetry()
    {
        var policy = BuildPolicy();
        var attempt = 0;

        var act = async () => await CosmosRetryHelper.ExecuteWithRetryAsync(
            async () =>
            {
                attempt++;
                throw new OperationCanceledException("operation canceled");
            },
            policy,
            TotalBudget);

        await act.Should().ThrowAsync<OperationCanceledException>();
        attempt.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_BudgetExhausted_Throws()
    {
        var policy = BuildPolicy();
        var attempt = 0;

        var act = async () => await CosmosRetryHelper.ExecuteWithRetryAsync(
            async () =>
            {
                attempt++;
                await Task.Delay(60);
                throw new CosmosException("always fails", HttpStatusCode.ServiceUnavailable, 0, "act-1", 0);
            },
            policy,
            TimeSpan.FromMilliseconds(50));

        await act.Should().ThrowAsync<CosmosException>();
        attempt.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_PolicyRetriesExhausted_Throws()
    {
        var policy = BuildPolicy(maxRetries: 2);
        var attempt = 0;

        var act = async () => await CosmosRetryHelper.ExecuteWithRetryAsync(
            async () =>
            {
                attempt++;
                throw new CosmosException("always fails", HttpStatusCode.TooManyRequests, 0, "act-1", 0);
            },
            policy,
            TotalBudget);

        await act.Should().ThrowAsync<CosmosException>();
        attempt.Should().Be(3);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_CancellationHonored_Throws()
    {
        var policy = BuildPolicy();
        var cts = new CancellationTokenSource();
        var attempt = 0;

        var act = async () => await CosmosRetryHelper.ExecuteWithRetryAsync(
            async () =>
            {
                attempt++;
                if (attempt == 1)
                    throw new CosmosException("transient", HttpStatusCode.ServiceUnavailable, 0, "act-1", 0);
                cts.Cancel();
                await Task.Delay(50, cts.Token);
                return "success";
            },
            policy,
            TotalBudget,
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_VoidOverload_RetriesSuccessfully()
    {
        var policy = BuildPolicy();
        var attempt = 0;

        await CosmosRetryHelper.ExecuteWithRetryAsync(
            async () =>
            {
                attempt++;
                if (attempt == 1)
                    throw new CosmosException("transient", HttpStatusCode.TooManyRequests, 0, "act-1", 0);
                await Task.CompletedTask;
            },
            policy,
            TotalBudget);

        attempt.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_DelayExceedsBudget_ThrowsWithoutWaiting()
    {
        var policy = Substitute.For<IRetryPolicy>();
        policy.MaxRetries.Returns(10);
        policy.ShouldRetry(Arg.Any<int>()).Returns(true);
        policy.DelayFor(Arg.Any<int>()).Returns(TimeSpan.FromSeconds(5));

        var attempt = 0;
        var act = async () => await CosmosRetryHelper.ExecuteWithRetryAsync(
            async () =>
            {
                attempt++;
                throw new CosmosException("transient", HttpStatusCode.ServiceUnavailable, 0, "act-1", 0);
            },
            policy,
            TimeSpan.FromMilliseconds(50));

        await act.Should().ThrowAsync<CosmosException>();
        attempt.Should().Be(1);
    }
}
