using CommunityToolkit.Mvvm.ComponentModel;

namespace Collector.Presentation.ViewModels;

public sealed class SegmentOptionViewModel<T>(T value, string label, Action<T> select, string? unavailableReason = null, string? hint = null) : ObservableObject
    where T : struct, Enum
{
    private bool _isSelected;

    public T Value { get; } = value;

    public string Label { get; } = label;

    public string? UnavailableReason { get; } = unavailableReason;

    public bool IsAvailable => UnavailableReason is null;

    public string? HelpText => UnavailableReason ?? hint;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value && !_isSelected && IsAvailable)
            {
                select(Value);
            }
        }
    }

    public void Sync(T selected) => SetProperty(ref _isSelected, EqualityComparer<T>.Default.Equals(selected, Value), nameof(IsSelected));
}
