using Collector.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Collector.Presentation.Services;

public sealed class HistoryPoller(TimeProvider time, IOptions<HistoryOptions> options) : IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _cts is not null;
            }
        }
    }

    public void Start(Func<bool> shouldPoll, Func<CancellationToken, Task> tick)
    {
        CancellationTokenSource cts;
        lock (_gate)
        {
            if (_cts is not null)
            {
                return;
            }

            cts = new CancellationTokenSource();
            _cts = cts;
        }

        _ = RunAsync(cts, shouldPoll, tick);
    }

    public void Stop()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _cts = null;
        }
    }

    public void Dispose() => Stop();

    private async Task RunAsync(CancellationTokenSource cts, Func<bool> shouldPoll, Func<CancellationToken, Task> tick)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.PollSeconds), time);
            while (await timer.WaitForNextTickAsync(cts.Token) && shouldPoll())
            {
                await tick(cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Release(cts);
        }
    }

    private void Release(CancellationTokenSource cts)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_cts, cts))
            {
                _cts = null;
            }

            cts.Dispose();
        }
    }
}
