using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Collector.Presentation.Behaviors;

// A DataGridCell selects its row on every left mouse-down, even one a button inside it already handled.
// This runs the button's action on the preview event instead, so a row's checkbox or remove button
// works without moving the grid's selection.
public static class CellActionBehavior
{
    public static readonly DependencyProperty KeepsRowSelectionProperty = DependencyProperty.RegisterAttached(
        "KeepsRowSelection",
        typeof(bool),
        typeof(CellActionBehavior),
        new PropertyMetadata(false, OnKeepsRowSelectionChanged));

    public static bool GetKeepsRowSelection(DependencyObject element) =>
        (bool)element.GetValue(KeepsRowSelectionProperty);

    public static void SetKeepsRowSelection(DependencyObject element, bool value) =>
        element.SetValue(KeepsRowSelectionProperty, value);

    private static void OnKeepsRowSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        if (e.NewValue is true)
        {
            element.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        }
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindButton(e.OriginalSource as DependencyObject, (DependencyObject)sender) is not { IsEnabled: true } button)
        {
            return;
        }

        e.Handled = true;
        Activate(button);
    }

    private static ButtonBase? FindButton(DependencyObject? source, DependencyObject boundary)
    {
        for (var current = source; current is not null && current != boundary; current = Parent(current))
        {
            if (current is ButtonBase button)
            {
                return button;
            }
        }

        return null;
    }

    private static DependencyObject? Parent(DependencyObject element) =>
        element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);

    private static void Activate(ButtonBase button)
    {
        if (button is ToggleButton toggle)
        {
            toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, toggle.IsChecked != true);
        }

        if (button.Command is { } command && command.CanExecute(button.CommandParameter))
        {
            command.Execute(button.CommandParameter);
        }
    }
}
