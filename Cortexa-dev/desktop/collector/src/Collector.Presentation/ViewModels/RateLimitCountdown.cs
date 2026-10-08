using System.Windows.Threading;

namespace Collector.Presentation.ViewModels;

public sealed class RateLimitCountdown : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private readonly TimeProvider _time;
    private readonly DispatcherTimer _timer;
    private DateTimeOffset _until;
    private Action<TimeSpan>? _onTick;

    public RateLimitCountdown(TimeProvider time)
    {
        _time = time;
        _timer = new DispatcherTimer { Interval = TickInterval };
        _timer.Tick += (_, _) => Tick();
    }

    public bool IsRunning => _timer.IsEnabled;

    public TimeSpan Remaining => Positive(_until - _time.GetUtcNow());

    public void Start(DateTimeOffset until, Action<TimeSpan> onTick)
    {
        _until = until;
        _onTick = onTick;
        _timer.Start();
        onTick(Remaining);
    }

    public void Stop()
    {
        _timer.Stop();
        _onTick = null;
    }

    public void Dispose() => Stop();

    private void Tick()
    {
        var remaining = Remaining;
        _onTick?.Invoke(remaining);
        if (remaining <= TimeSpan.Zero)
        {
            _timer.Stop();
        }
    }

    private static TimeSpan Positive(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;
}
