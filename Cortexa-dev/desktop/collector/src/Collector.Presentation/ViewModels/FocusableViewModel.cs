using CommunityToolkit.Mvvm.ComponentModel;

namespace Collector.Presentation.ViewModels;

public interface IFocusSource
{
    event EventHandler<string>? FocusRequested;

    string? PendingFocus { get; }

    string? TakePendingFocus();
}

public abstract class FocusableViewModel : ObservableObject, IFocusSource
{
    public event EventHandler<string>? FocusRequested;

    public string? PendingFocus { get; private set; }

    public string? TakePendingFocus()
    {
        var pending = PendingFocus;
        PendingFocus = null;
        return pending;
    }

    protected void RequestFocus(string key)
    {
        PendingFocus = key;
        FocusRequested?.Invoke(this, key);
    }
}
