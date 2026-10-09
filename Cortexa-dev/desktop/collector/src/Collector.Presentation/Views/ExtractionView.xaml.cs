using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using Collector.Presentation.ViewModels;

namespace Collector.Presentation.Views;

public partial class ExtractionView : UserControl
{
    public ExtractionView() => InitializeComponent();

    private void OnDocumentsSorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not ExtractionViewModel viewModel || sender is not DataGrid grid)
        {
            return;
        }

        var direction = viewModel.SortBy(e.Column.SortMemberPath);
        foreach (var column in grid.Columns)
        {
            column.SortDirection = ReferenceEquals(column, e.Column) ? direction : null;
        }
    }
}

public sealed class EnumMatchConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        Equals(value, parameter);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? parameter : Binding.DoNothing;
}
