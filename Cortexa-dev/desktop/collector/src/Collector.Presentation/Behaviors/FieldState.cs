using System.Windows;

namespace Collector.Presentation.Behaviors;

public static class FieldState
{
    public static readonly DependencyProperty HasErrorProperty = DependencyProperty.RegisterAttached(
        "HasError",
        typeof(bool),
        typeof(FieldState),
        new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty ErrorProperty = DependencyProperty.RegisterAttached(
        "Error",
        typeof(string),
        typeof(FieldState),
        new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty HelperProperty = DependencyProperty.RegisterAttached(
        "Helper",
        typeof(string),
        typeof(FieldState),
        new FrameworkPropertyMetadata(null));

    public static bool GetHasError(DependencyObject element) => (bool)element.GetValue(HasErrorProperty);

    public static void SetHasError(DependencyObject element, bool value) => element.SetValue(HasErrorProperty, value);

    public static string? GetError(DependencyObject element) => (string?)element.GetValue(ErrorProperty);

    public static void SetError(DependencyObject element, string? value) => element.SetValue(ErrorProperty, value);

    public static string? GetHelper(DependencyObject element) => (string?)element.GetValue(HelperProperty);

    public static void SetHelper(DependencyObject element, string? value) => element.SetValue(HelperProperty, value);
}
