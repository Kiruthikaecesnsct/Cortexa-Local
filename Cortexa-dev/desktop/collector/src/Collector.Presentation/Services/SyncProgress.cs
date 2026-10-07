namespace Collector.Presentation.Services;

public sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => UiThread.Post(() => handler(value));
}
