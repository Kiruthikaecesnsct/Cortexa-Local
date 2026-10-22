using Collector.Application.Ports;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Ai;

public sealed class GeminiQuotaWait(IOptions<GeminiProviderOptions> options, TimeProvider timeProvider)
{
    public TimeSpan? DelayFor(AiProviderException? failure)
    {
        var max = TimeSpan.FromSeconds(options.Value.MaxQuotaRetryDelaySeconds);
        if (failure?.QuotaHint is not { } hint || hint.Scope == AiQuotaScope.PerDay || max <= TimeSpan.Zero)
        {
            return null;
        }

        var delay = hint.RetryAfter ?? (hint.Scope == AiQuotaScope.PerMinute ? max : null);
        return delay is { } wait && wait <= max ? wait : null;
    }

    public Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, timeProvider, cancellationToken);
}
