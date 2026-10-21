using System.Windows;
using System.Windows.Interop;
using Collector.Application.Ports;

namespace Collector.Presentation.Services;

public sealed class MainWindowHandleSource : IWindowHandleSource
{
    public nint GetMainWindowHandle()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return 0;
        }

        return dispatcher.CheckAccess() ? ReadHandle() : dispatcher.Invoke(ReadHandle);
    }

    private static nint ReadHandle() =>
        System.Windows.Application.Current?.MainWindow is Window window ? new WindowInteropHelper(window).Handle : 0;
}
