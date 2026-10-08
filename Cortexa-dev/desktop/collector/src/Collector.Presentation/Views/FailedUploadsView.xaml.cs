using System.ComponentModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;
using Collector.Presentation.ViewModels;

namespace Collector.Presentation.Views;

public partial class FailedUploadsView : UserControl
{
    public FailedUploadsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged previous)
        {
            previous.PropertyChanged -= OnViewModelChanged;
        }

        if (e.NewValue is INotifyPropertyChanged current)
        {
            current.PropertyChanged += OnViewModelChanged;
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        var announced = e.PropertyName == nameof(FailedUploadsViewModel.Announcement);
        if (announced && sender is FailedUploadsViewModel { Announcement.Length: > 0 } && AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, RaiseLiveRegionChanged);
        }
    }

    private void RaiseLiveRegionChanged()
    {
        var peer = UIElementAutomationPeer.FromElement(LiveText) ?? UIElementAutomationPeer.CreatePeerForElement(LiveText);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
