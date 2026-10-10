using Collector.Application.Remote;
using Collector.Domain.Enums;
using Collector.Infrastructure.Options;

namespace Collector.Infrastructure.Remote.RateLimit;

public sealed class RateLimitHandler(
    SourceType provider,
    RateLimitGate gate,
    IRateHeaderReader reader,
    RateLimitHandlerSettings settings) : DelegatingHandler
{
    private const string AttemptTimeoutMessage = "The remote request attempt timed out.";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Content is not null)
        {
            await request.Content.LoadIntoBufferAsync(cancellationToken);
        }

        for (var attempt = 0; ; attempt++)
        {
            var response = await SendOnceAsync(request, cancellationToken);
            var snapshot = reader.Read(response, settings.Time.GetUtcNow());
            gate.Update(snapshot);
            if (!reader.IsRateLimited(response, snapshot))
            {
                ThrottleIfAsked(snapshot);
                return response;
            }

            response.Dispose();
            var resumeAt = ResumeTime(snapshot);
            if (attempt >= settings.MaxRetries)
            {
                throw new RemoteSourceException(RemoteFailureKind.RateLimited, provider, snapshot.ResetAt ?? resumeAt);
            }

            gate.Pause(resumeAt);
        }
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var lease = await gate.EnterAsync(cancellationToken);
        using var timeout = new CancellationTokenSource(settings.AttemptTimeout, settings.Time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            return await ExchangeAsync(request, linked.Token);
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(AttemptTimeoutMessage, exception);
        }
    }

    private async Task<HttpResponseMessage> ExchangeAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (!request.Options.TryGetValue(RemoteRequestOptions.BufferBody, out var buffer) || !buffer)
        {
            return response;
        }

        try
        {
            await response.Content.LoadIntoBufferAsync(cancellationToken);
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private void ThrottleIfAsked(RateSnapshot snapshot)
    {
        if (snapshot.RetryAfter is { } delay && delay > TimeSpan.Zero)
        {
            gate.Pause(settings.Time.GetUtcNow() + delay);
        }
    }

    private DateTimeOffset ResumeTime(RateSnapshot snapshot)
    {
        var now = settings.Time.GetUtcNow();
        if (snapshot.RetryAfter is { } retryAfter)
        {
            return now + retryAfter;
        }

        if (snapshot.ResetAt is { } resetAt && resetAt > now)
        {
            return resetAt;
        }

        return now.AddSeconds(settings.Options.SecondaryWaitSeconds);
    }
}

public sealed record RateLimitHandlerSettings(
    RateLimitOptions Options,
    TimeProvider Time,
    TimeSpan AttemptTimeout,
    int MaxRetries);
