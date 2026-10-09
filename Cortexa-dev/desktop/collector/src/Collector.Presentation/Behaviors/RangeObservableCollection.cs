using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Collector.Presentation.Behaviors;

public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    public void AddRange(IEnumerable<T> items)
    {
        var added = false;
        foreach (var item in items)
        {
            Items.Add(item);
            added = true;
        }

        if (added)
        {
            RaiseReset();
        }
    }

    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        RaiseReset();
    }

    private void RaiseReset()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
