using System.Net;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.History;

public sealed class HistoryRetryPolicy(IOptionsMonitor<CollectorServerOptions> server, TimeProvider timeProvider)
{
    private const int MaxRetries = 2;

    public async Task<HttpResponseMessage> SendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        var delays = server.CurrentValue.ReadRetryDelaysMs;
        var retries = Math.Min(MaxRetries, delays.Length);
        for (var attempt = 0; attempt < retries; attempt++)
        {
            var result = await AttemptAsync(send, cancellationToken);
            if (!result.IsTransient)
            {
                return result.Unwrap();
            }

            result.Dispose();
            await Task.Delay(TimeSpan.FromMilliseconds(delays[attempt]), timeProvider, cancellationToken);
        }

        return await send(cancellationToken);
    }

    private static async Task<Attempt> AttemptAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        try
        {
            return new Attempt(await send(cancellationToken), null);
        }
        catch (HttpRequestException exception)
        {
            return new Attempt(null, exception);
        }
    }

    private sealed record Attempt(HttpResponseMessage? Response, HttpRequestException? Error)
    {
        public bool IsTransient => Error is not null || IsTransientStatus(Response!.StatusCode);

        public HttpResponseMessage Unwrap() => Response!;

        public void Dispose() => Response?.Dispose();

        private static bool IsTransientStatus(HttpStatusCode status) =>
            (int)status >= (int)HttpStatusCode.InternalServerError || status == HttpStatusCode.RequestTimeout;
    }
}
