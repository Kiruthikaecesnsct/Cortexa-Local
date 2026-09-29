using Cortexa.ModelRouter.Application.Exceptions;
using Cortexa.ModelRouter.Infrastructure.Configuration;
using Cortexa.ModelRouter.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cortexa.ModelRouter.Tests;

public sealed class FoundryConcurrencyLimiterTests
{
    private const string SampleDeployment = "grok-4-3-deployment";
    private const int WaitSeconds = 2;

    [Fact]
    public async Task AcquireAsync_BelowMaxInFlight_SucceedsForAllConcurrentCallers()
    {
        const int MaxInFlight = 2;
        var limiter = new FoundryConcurrencyLimiter();

        var firstLease = await limiter.AcquireAsync(SampleDeployment, MaxInFlight, WaitSeconds, CancellationToken.None);
        var secondLease = await limiter.AcquireAsync(SampleDeployment, MaxInFlight, WaitSeconds, CancellationToken.None);

        firstLease.Should().NotBeNull();
        secondLease.Should().NotBeNull();

        firstLease.Dispose();
        secondLease.Dispose();
    }

    [Fact]
    public async Task AcquireAsync_ExceedsMaxInFlight_ThrowsFoundryCapacityExceededExceptionWithCorrectParameters()
    {
        const int MaxInFlight = 1;
        var limiter = new FoundryConcurrencyLimiter();
        using var heldLease = await limiter.AcquireAsync(SampleDeployment, MaxInFlight, WaitSeconds, CancellationToken.None);

        var act = async () => await limiter.AcquireAsync(SampleDeployment, MaxInFlight, WaitSeconds, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<FoundryCapacityExceededException>();
        exception.Which.Deployment.Should().Be(SampleDeployment);
        exception.Which.MaxInFlight.Should().Be(MaxInFlight);
    }

    [Fact]
    public async Task AcquireAsync_AfterLeaseDisposed_FreesSlotForNextWaiter()
    {
        const int MaxInFlight = 1;
        var limiter = new FoundryConcurrencyLimiter();
        var firstLease = await limiter.AcquireAsync(SampleDeployment, MaxInFlight, WaitSeconds, CancellationToken.None);

        firstLease.Dispose();
        var secondLease = await limiter.AcquireAsync(SampleDeployment, MaxInFlight, WaitSeconds, CancellationToken.None);

        secondLease.Should().NotBeNull();
        secondLease.Dispose();
    }

    [Fact]
    public async Task Dispose_CalledTwice_DoesNotDoubleReleaseOrThrow()
    {
        const int MaxInFlight = 1;
        var limiter = new FoundryConcurrencyLimiter();
        var lease = await limiter.AcquireAsync(SampleDeployment, MaxInFlight, WaitSeconds, CancellationToken.None);

        lease.Dispose();
        var act = () => lease.Dispose();

        act.Should().NotThrow();
        var nextLease = await limiter.AcquireAsync(SampleDeployment, MaxInFlight, WaitSeconds, CancellationToken.None);
        nextLease.Should().NotBeNull();
        nextLease.Dispose();
    }

    [Fact]
    public async Task GetInFlightCount_AfterAcquiringSomeLeases_ReturnsAcquiredCountNotMax()
    {
        const int MaxInFlight = 5;
        const int LeasesToAcquire = 3;
        var limiter = new FoundryConcurrencyLimiter();
        var leases = new List<IDisposable>();
        for (var i = 0; i < LeasesToAcquire; i++)
            leases.Add(await limiter.AcquireAsync(SampleDeployment, MaxInFlight, WaitSeconds, CancellationToken.None));

        var inFlight = limiter.GetInFlightCount(SampleDeployment, MaxInFlight);

        inFlight.Should().Be(LeasesToAcquire);
        foreach (var lease in leases)
            lease.Dispose();
    }

    [Fact]
    public async Task GetInFlightCount_AfterAllLeasesReleased_ReturnsZero()
    {
        const int MaxInFlight = 3;
        var limiter = new FoundryConcurrencyLimiter();
        var lease = await limiter.AcquireAsync(SampleDeployment, MaxInFlight, WaitSeconds, CancellationToken.None);

        lease.Dispose();
        var inFlight = limiter.GetInFlightCount(SampleDeployment, MaxInFlight);

        inFlight.Should().Be(0);
    }

    [Fact]
    public void GetInFlightCount_NoAcquireCalled_ReturnsZero()
    {
        const int MaxInFlight = 4;
        var limiter = new FoundryConcurrencyLimiter();

        var inFlight = limiter.GetInFlightCount(SampleDeployment, MaxInFlight);

        inFlight.Should().Be(0);
    }

    [Fact]
    public async Task AcquireAsync_InFlightReachesHighWaterFraction_LogsWarning()
    {
        const int MaxInFlight = 5;
        const double HighWaterFraction = 0.8;
        const int LeasesToReachHighWater = 4;
        var logger = new CapturingLogger();
        var limiter = new FoundryConcurrencyLimiter(
            logger, Options.Create(new FoundrySettings { ConcurrencyHighWaterFraction = HighWaterFraction }));
        var leases = new List<IDisposable>();

        for (var i = 0; i < LeasesToReachHighWater; i++)
            leases.Add(await limiter.AcquireAsync(SampleDeployment, MaxInFlight, WaitSeconds, CancellationToken.None));

        logger.Logs.Should().Contain(LogLevel.Warning);
        foreach (var lease in leases)
            lease.Dispose();
    }

    [Fact]
    public async Task AcquireAsync_InFlightBelowHighWaterFraction_DoesNotLogWarning()
    {
        const int MaxInFlight = 5;
        const double HighWaterFraction = 0.8;
        const int LeasesBelowHighWater = 3;
        var logger = new CapturingLogger();
        var limiter = new FoundryConcurrencyLimiter(
            logger, Options.Create(new FoundrySettings { ConcurrencyHighWaterFraction = HighWaterFraction }));
        var leases = new List<IDisposable>();

        for (var i = 0; i < LeasesBelowHighWater; i++)
            leases.Add(await limiter.AcquireAsync(SampleDeployment, MaxInFlight, WaitSeconds, CancellationToken.None));

        logger.Logs.Should().NotContain(LogLevel.Warning);
        foreach (var lease in leases)
            lease.Dispose();
    }

    private sealed class CapturingLogger : ILogger<FoundryConcurrencyLimiter>
    {
        public List<LogLevel> Logs { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Logs.Add(logLevel);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
