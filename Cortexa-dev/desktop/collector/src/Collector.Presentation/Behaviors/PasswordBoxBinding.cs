using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Collector.Presentation.ViewModels;

namespace Collector.Presentation.Behaviors;

public static class PasswordBoxBinding
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(PasswordBoxBinding),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty AutoFocusProperty = DependencyProperty.RegisterAttached(
        "AutoFocus",
        typeof(bool),
        typeof(PasswordBoxBinding),
        new PropertyMetadata(false));

    private static readonly DependencyProperty AttachedHostProperty = DependencyProperty.RegisterAttached(
        "AttachedHost",
        typeof(IPasswordHost),
        typeof(PasswordBoxBinding),
        new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static bool GetAutoFocus(DependencyObject element) => (bool)element.GetValue(AutoFocusProperty);

    public static void SetAutoFocus(DependencyObject element, bool value) => element.SetValue(AutoFocusProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box)
        {
            return;
        }

        box.Loaded -= OnLoaded;
        box.Unloaded -= OnUnloaded;
        if (!(bool)e.NewValue)
        {
            return;
        }

        box.Loaded += OnLoaded;
        box.Unloaded += OnUnloaded;
        if (box.IsLoaded)
        {
            Attach(box);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Attach((PasswordBox)sender);

    private static void OnUnloaded(object sender, RoutedEventArgs e) => Detach((PasswordBox)sender);

    private static void Attach(PasswordBox box)
    {
        if (box.DataContext is not IPasswordHost host)
        {
            return;
        }

        host.PasswordSource = new PasswordBoxSource(box);
        box.SetValue(AttachedHostProperty, host);
        box.PasswordChanged -= OnPasswordChanged;
        box.PasswordChanged += OnPasswordChanged;
        if (GetAutoFocus(box))
        {
            box.Dispatcher.BeginInvoke(DispatcherPriority.Input, () => box.Focus());
        }
    }

    private static void Detach(PasswordBox box)
    {
        box.PasswordChanged -= OnPasswordChanged;
        box.Clear();
        if (box.GetValue(AttachedHostProperty) is IPasswordHost host)
        {
            host.PasswordSource = null;
        }

        box.ClearValue(AttachedHostProperty);
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (((PasswordBox)sender).GetValue(AttachedHostProperty) is IPasswordHost host)
        {
            host.OnPasswordEdited();
        }
    }

    private sealed class PasswordBoxSource(PasswordBox box) : IPasswordSource
    {
        public string GetPassword() => box.Password;

        public void Clear() => box.Clear();
    }
}
