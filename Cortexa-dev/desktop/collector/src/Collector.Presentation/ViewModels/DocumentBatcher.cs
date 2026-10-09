using System.Collections.Concurrent;
using Collector.Application.Extraction;
using Collector.Presentation.Services;

namespace Collector.Presentation.ViewModels;

public sealed class DocumentBatcher : IDisposable
{
    private readonly ConcurrentQueue<SplitOutcome> _pending = new();
    private readonly Action<IReadOnlyList<SplitOutcome>> _onFlush;
    private readonly ITimer _timer;
    private volatile bool _disposed;

    public DocumentBatcher(TimeProvider time, Action<IReadOnlyList<SplitOutcome>> onFlush)
    {
        _onFlush = onFlush;
        _timer = time.CreateTimer(_ => OnTick(), null, IntakeTiming.FlushInterval, IntakeTiming.FlushInterval);
    }

    public int PendingCount => _pending.Count;

    public void Enqueue(SplitOutcome outcome) => _pending.Enqueue(outcome);

    public void FlushNow()
    {
        var batch = new List<SplitOutcome>(_pending.Count);
        while (_pending.TryDequeue(out var outcome))
        {
            batch.Add(outcome);
        }

        if (batch.Count > 0)
        {
            _onFlush(batch);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }

    private void OnTick()
    {
        if (_disposed || _pending.IsEmpty)
        {
            return;
        }

        UiThread.Post(FlushIfActive);
    }

    private void FlushIfActive()
    {
        if (!_disposed)
        {
            FlushNow();
        }
    }
}
