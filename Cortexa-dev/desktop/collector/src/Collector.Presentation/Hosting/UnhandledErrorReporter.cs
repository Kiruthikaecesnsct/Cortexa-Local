using System.Windows;
using Collector.Presentation.Resources;
using Microsoft.Extensions.Logging;

namespace Collector.Presentation.Hosting;

public sealed class UnhandledErrorReporter(Func<ILogger?> loggerAccessor)
{
    private int _dialogOpen;

    public void Report(Exception? exception, string source)
    {
        loggerAccessor()?.LogError(exception, "Unhandled error from {Source}.", source);
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            ShowDialog();
            return;
        }

        dispatcher.BeginInvoke(ShowDialog);
    }

    private void ShowDialog()
    {
        if (Interlocked.Exchange(ref _dialogOpen, 1) == 1)
        {
            return;
        }

        try
        {
            MessageBox.Show(
                ShellStrings.UnhandledError,
                ShellStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Interlocked.Exchange(ref _dialogOpen, 0);
        }
    }
}
