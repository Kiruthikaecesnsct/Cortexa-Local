using CommunityToolkit.Mvvm.ComponentModel;

namespace Collector.Presentation.ViewModels;

public sealed class SegmentOptionViewModel<T>(T value, string label, Action<T> select) : ObservableObject
    where T : struct, Enum
{
    private bool _isSelected;

    public T Value { get; } = value;

    public string Label { get; } = label;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value && !_isSelected)
            {
                select(Value);
            }
        }
    }

    public void Sync(T selected) => SetProperty(ref _isSelected, EqualityComparer<T>.Default.Equals(selected, Value), nameof(IsSelected));
}
