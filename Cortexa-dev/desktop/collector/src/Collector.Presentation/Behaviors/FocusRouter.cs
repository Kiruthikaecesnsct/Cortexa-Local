using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Collector.Presentation.ViewModels;

namespace Collector.Presentation.Behaviors;

public static class FocusRouter
{
    public static readonly DependencyProperty IsHostProperty = DependencyProperty.RegisterAttached(
        "IsHost",
        typeof(bool),
        typeof(FocusRouter),
        new PropertyMetadata(false, OnIsHostChanged));

    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key",
        typeof(string),
        typeof(FocusRouter),
        new PropertyMetadata(null));

    private static readonly DependencyProperty SubscriptionProperty = DependencyProperty.RegisterAttached(
        "Subscription",
        typeof(Subscription),
        typeof(FocusRouter),
        new PropertyMetadata(null));

    public static bool GetIsHost(DependencyObject element) => (bool)element.GetValue(IsHostProperty);

    public static void SetIsHost(DependencyObject element, bool value) => element.SetValue(IsHostProperty, value);

    public static string? GetKey(DependencyObject element) => (string?)element.GetValue(KeyProperty);

    public static void SetKey(DependencyObject element, string? value) => element.SetValue(KeyProperty, value);

    private static void OnIsHostChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.Loaded -= OnLoaded;
        element.Unloaded -= OnUnloaded;
        element.DataContextChanged -= OnDataContextChanged;
        if ((bool)e.NewValue)
        {
            element.Loaded += OnLoaded;
            element.Unloaded += OnUnloaded;
            element.DataContextChanged += OnDataContextChanged;
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        Subscribe(element);
        Drain(element);
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e) => Unsubscribe((FrameworkElement)sender);

    private static void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        if (!element.IsLoaded)
        {
            return;
        }

        Subscribe(element);
        Drain(element);
    }

    private static void Subscribe(FrameworkElement element)
    {
        Unsubscribe(element);
        if (element.DataContext is not IFocusSource source)
        {
            return;
        }

        var subscription = new Subscription(element, source);
        element.SetValue(SubscriptionProperty, subscription);
    }

    private static void Unsubscribe(FrameworkElement element)
    {
        if (element.GetValue(SubscriptionProperty) is not Subscription subscription)
        {
            return;
        }

        subscription.Dispose();
        element.ClearValue(SubscriptionProperty);
    }

    private static void Drain(FrameworkElement host)
    {
        if (host.DataContext is not IFocusSource source || source.PendingFocus is null)
        {
            return;
        }

        host.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => FocusPending(host, source));
    }

    private static void FocusPending(FrameworkElement host, IFocusSource source)
    {
        var key = source.TakePendingFocus();
        if (key is null)
        {
            return;
        }

        var target = Find(host, key);
        if (target is { IsVisible: true, IsEnabled: true, Focusable: true })
        {
            target.Focus();
        }
    }

    private static FrameworkElement? Find(DependencyObject root, string key)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is FrameworkElement element && GetKey(element) == key)
            {
                return element;
            }

            var nested = Find(child, key);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private sealed class Subscription : IDisposable
    {
        private readonly FrameworkElement _host;
        private readonly IFocusSource _source;

        public Subscription(FrameworkElement host, IFocusSource source)
        {
            _host = host;
            _source = source;
            _source.FocusRequested += OnFocusRequested;
        }

        public void Dispose() => _source.FocusRequested -= OnFocusRequested;

        private void OnFocusRequested(object? sender, string key) => Drain(_host);
    }
}
