using System.Windows;
using Collector.Presentation.ViewModels;

namespace Collector.Presentation.Views;

public partial class MainWindow : Window
{
    private bool? _isNarrow;

    public MainWindow()
    {
        InitializeComponent();
        Closing += (_, _) => (DataContext as ShellViewModel)?.OnWindowClosing();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is not ShellViewModel shell)
        {
            return;
        }

        var breakpoint = (double)FindResource("Breakpoint.RailCompact");
        var isNarrow = e.NewSize.Width < breakpoint;
        if (_isNarrow == isNarrow)
        {
            return;
        }

        _isNarrow = isNarrow;
        shell.OnWindowNarrowChanged(isNarrow);
    }
}
