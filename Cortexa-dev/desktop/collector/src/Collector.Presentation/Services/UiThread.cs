using System.Windows.Threading;

namespace Collector.Presentation.Services;

public static class UiThread
{
    public static void Post(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
    }
}
