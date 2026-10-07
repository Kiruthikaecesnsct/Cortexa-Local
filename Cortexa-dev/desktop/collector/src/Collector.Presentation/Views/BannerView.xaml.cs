using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Collector.Presentation.Views;

public partial class BannerView : UserControl
{
    public BannerView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is null || !AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input, RaiseLiveRegionChanged);
    }

    private void RaiseLiveRegionChanged()
    {
        var peer = UIElementAutomationPeer.FromElement(Root) ?? UIElementAutomationPeer.CreatePeerForElement(Root);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
