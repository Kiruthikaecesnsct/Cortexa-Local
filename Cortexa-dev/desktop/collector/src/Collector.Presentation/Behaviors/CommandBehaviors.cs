using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Collector.Presentation.Behaviors;

public static class CommandBehaviors
{
    public static readonly DependencyProperty LostFocusCommandProperty = DependencyProperty.RegisterAttached(
        "LostFocusCommand",
        typeof(ICommand),
        typeof(CommandBehaviors),
        new PropertyMetadata(null, OnLostFocusCommandChanged));

    public static readonly DependencyProperty ActivateCommandProperty = DependencyProperty.RegisterAttached(
        "ActivateCommand",
        typeof(ICommand),
        typeof(CommandBehaviors),
        new PropertyMetadata(null, OnActivateCommandChanged));

    public static readonly DependencyProperty SpaceCommandProperty = DependencyProperty.RegisterAttached(
        "SpaceCommand",
        typeof(ICommand),
        typeof(CommandBehaviors),
        new PropertyMetadata(null, OnSpaceCommandChanged));

    public static ICommand? GetSpaceCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(SpaceCommandProperty);

    public static void SetSpaceCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(SpaceCommandProperty, value);

    public static ICommand? GetLostFocusCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(LostFocusCommandProperty);

    public static void SetLostFocusCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(LostFocusCommandProperty, value);

    public static ICommand? GetActivateCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(ActivateCommandProperty);

    public static void SetActivateCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(ActivateCommandProperty, value);

    private static void OnLostFocusCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.LostFocus -= OnLostFocus;
        if (e.NewValue is not null)
        {
            element.LostFocus += OnLostFocus;
        }
    }

    private static void OnLostFocus(object sender, RoutedEventArgs e) =>
        Execute(GetLostFocusCommand((DependencyObject)sender));

    private static void OnActivateCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBoxItem item)
        {
            return;
        }

        item.PreviewMouseLeftButtonUp -= OnMouseActivate;
        item.KeyDown -= OnKeyActivate;
        if (e.NewValue is null)
        {
            return;
        }

        item.PreviewMouseLeftButtonUp += OnMouseActivate;
        item.KeyDown += OnKeyActivate;
    }

    private static void OnSpaceCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBoxItem item)
        {
            return;
        }

        item.KeyDown -= OnSpaceKeyDown;
        if (e.NewValue is not null)
        {
            item.KeyDown += OnSpaceKeyDown;
        }
    }

    private static void OnSpaceKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space || e.OriginalSource != sender)
        {
            return;
        }

        e.Handled = true;
        Execute(GetSpaceCommand((DependencyObject)sender));
    }

    private static void OnMouseActivate(object sender, MouseButtonEventArgs e) =>
        Execute(GetActivateCommand((DependencyObject)sender));

    private static void OnKeyActivate(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space))
        {
            return;
        }

        e.Handled = true;
        Execute(GetActivateCommand((DependencyObject)sender));
    }

    private static void Execute(ICommand? command)
    {
        if (command is not null && command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}
