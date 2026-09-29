using System.Net;
using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Infrastructure.Helpers;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using NSubstitute;
using Xunit;

namespace Cortexa.JobOrchestrator.Infrastructure.Tests;

public sealed class CosmosRetryHelperExternalCancellationTests
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan TotalBudget = TimeSpan.FromMilliseconds(200);

    private static IRetryPolicy BuildPolicy(int maxRetries = MaxRetries)
    {
        var policy = Substitute.For<IRetryPolicy>();
        policy.MaxRetries.Returns(maxRetries);
        policy.ShouldRetry(Arg.Any<int>()).Returns(x => (int)x[0] <= maxRetries);
        policy.DelayFor(Arg.Any<int>()).Returns(TimeSpan.FromMilliseconds(10));
        return policy;
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_InternalDeadlineCancellation_RetriesAsTransient()
    {
        var policy = BuildPolicy();
        var externalCts = new CancellationTokenSource();
        var attempt = 0;

        var result = await CosmosRetryHelper.ExecuteWithRetryAsync(
            async () =>
            {
                attempt++;
                if (attempt == 1)
                {
                    using var internalCts = new CancellationTokenSource();
                    internalCts.Cancel();
                    throw new TaskCanceledException("internal deadline expired", null, internalCts.Token);
                }
                return "success";
            },
            policy,
            TotalBudget,
            externalCts.Token,
            externalCts.Token);

        result.Should().Be("success");
        attempt.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_ExternalTokenCancelled_DoesNotRetry()
    {
        var policy = BuildPolicy();
        var externalCts = new CancellationTokenSource();
        var attempt = 0;

        var act = async () => await CosmosRetryHelper.ExecuteWithRetryAsync(
            async () =>
            {
                attempt++;
                externalCts.Cancel();
                await Task.Delay(10, externalCts.Token);
                return "success";
            },
            policy,
            TotalBudget,
            externalCts.Token,
            externalCts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        attempt.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_OperationCanceledExceptionWithExternalTokenCancelled_DoesNotRetry()
    {
        var policy = BuildPolicy();
        var externalCts = new CancellationTokenSource();
        externalCts.Cancel();
        var attempt = 0;

        var act = async () => await CosmosRetryHelper.ExecuteWithRetryAsync(
            async () =>
            {
                attempt++;
                throw new OperationCanceledException("client abort", externalCts.Token);
            },
            policy,
            TotalBudget,
            ct: default,
            externalCt: externalCts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        attempt.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_OperationCanceledExceptionWithExternalTokenNotCancelled_Retries()
    {
        var policy = BuildPolicy();
        var externalCts = new CancellationTokenSource();
        var attempt = 0;

        var result = await CosmosRetryHelper.ExecuteWithRetryAsync(
            async () =>
            {
                attempt++;
                if (attempt == 1)
                {
                    using var internalCts = new CancellationTokenSource();
                    internalCts.Cancel();
                    throw new OperationCanceledException("internal deadline", internalCts.Token);
                }
                return "success";
            },
            policy,
            TotalBudget,
            ct: default,
            externalCt: externalCts.Token);

        result.Should().Be("success");
        attempt.Should().Be(2);
        externalCts.Token.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_TaskCanceledExceptionWithExternalTokenCancelled_DoesNotRetry()
    {
        var policy = BuildPolicy();
        var externalCts = new CancellationTokenSource();
        externalCts.Cancel();
        var attempt = 0;

        var act = async () => await CosmosRetryHelper.ExecuteWithRetryAsync(
            async () =>
            {
                attempt++;
                throw new TaskCanceledException("client abort", null, externalCts.Token);
            },
            policy,
            TotalBudget,
            ct: default,
            externalCt: externalCts.Token);

        await act.Should().ThrowAsync<TaskCanceledException>();
        attempt.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_TaskCanceledExceptionWithExternalTokenNotCancelled_Retries()
    {
        var policy = BuildPolicy();
        var externalCts = new CancellationTokenSource();
        var attempt = 0;

        var result = await CosmosRetryHelper.ExecuteWithRetryAsync(
            async () =>
            {
                attempt++;
                if (attempt == 1)
                {
                    using var internalCts = new CancellationTokenSource();
                    internalCts.Cancel();
                    throw new TaskCanceledException("internal deadline", null, internalCts.Token);
                }
                return "success";
            },
            policy,
            TotalBudget,
            ct: default,
            externalCt: externalCts.Token);

        result.Should().Be("success");
        attempt.Should().Be(2);
        externalCts.Token.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_NoExternalToken_CancellationNotRetried()
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
            TotalBudget,
            ct: default,
            externalCt: null);

        await act.Should().ThrowAsync<OperationCanceledException>();
        attempt.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_ExternalTokenButTransientCosmosException_Retries()
    {
        var policy = BuildPolicy();
        var externalCts = new CancellationTokenSource();
        var attempt = 0;

        var result = await CosmosRetryHelper.ExecuteWithRetryAsync(
            async () =>
            {
                attempt++;
                if (attempt == 1)
                    throw new CosmosException("transient", HttpStatusCode.TooManyRequests, 0, "act-1", 0);
                return "success";
            },
            policy,
            TotalBudget,
            ct: default,
            externalCt: externalCts.Token);

        result.Should().Be("success");
        attempt.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_JitterAndBudgetStillWork_WithExternalCt()
    {
        var policy = BuildPolicy(maxRetries: 10);
        var externalCts = new CancellationTokenSource();
        var attempt = 0;
        var delays = new List<TimeSpan>();
        var startTime = DateTime.UtcNow;

        var act = async () => await CosmosRetryHelper.ExecuteWithRetryAsync(
            async () =>
            {
                if (attempt > 0)
                    delays.Add(DateTime.UtcNow - startTime);

                attempt++;
                startTime = DateTime.UtcNow;
                throw new CosmosException("always fails", HttpStatusCode.ServiceUnavailable, 0, "act-1", 0);
            },
            policy,
            TimeSpan.FromMilliseconds(50),
            ct: default,
            externalCt: externalCts.Token);

        await act.Should().ThrowAsync<CosmosException>();
        attempt.Should().BeGreaterThan(1);
        delays.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_VoidOverload_InternalDeadlineCancellation_Retries()
    {
        var policy = BuildPolicy();
        var externalCts = new CancellationTokenSource();
        var attempt = 0;

        await CosmosRetryHelper.ExecuteWithRetryAsync(
            async () =>
            {
                attempt++;
                if (attempt == 1)
                {
                    using var internalCts = new CancellationTokenSource();
                    internalCts.Cancel();
                    throw new OperationCanceledException("internal deadline", internalCts.Token);
                }
                await Task.CompletedTask;
            },
            policy,
            TotalBudget,
            ct: default,
            externalCt: externalCts.Token);

        attempt.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_VoidOverload_ExternalTokenCancelled_DoesNotRetry()
    {
        var policy = BuildPolicy();
        var externalCts = new CancellationTokenSource();
        externalCts.Cancel();
        var attempt = 0;

        var act = async () => await CosmosRetryHelper.ExecuteWithRetryAsync(
            async () =>
            {
                attempt++;
                throw new TaskCanceledException("client abort", null, externalCts.Token);
            },
            policy,
            TotalBudget,
            ct: default,
            externalCt: externalCts.Token);

        await act.Should().ThrowAsync<TaskCanceledException>();
        attempt.Should().Be(1);
    }
}
