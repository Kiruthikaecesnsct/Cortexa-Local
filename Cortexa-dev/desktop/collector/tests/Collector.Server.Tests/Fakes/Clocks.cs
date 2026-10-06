using Collector.Server.Application.Ports;

namespace Collector.Server.Tests.Fakes;

internal sealed class FixedClock : IClock
{
    public DateTimeOffset UtcNow => TestData.FixedTime;
}

internal sealed class FixedTimeProvider : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => TestData.FixedTime;
}
