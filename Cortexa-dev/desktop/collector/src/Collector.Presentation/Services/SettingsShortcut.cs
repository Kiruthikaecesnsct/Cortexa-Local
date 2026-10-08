using Collector.Presentation.Navigation;

namespace Collector.Presentation.Services;

public sealed class SettingsShortcut(INavigationService navigation)
{
    private string? _pendingFocusKey;

    public void Open(string focusKey)
    {
        _pendingFocusKey = focusKey;
        navigation.NavigateTo(ScreenKeys.Settings);
    }

    public string? TakeFocusKey()
    {
        var key = _pendingFocusKey;
        _pendingFocusKey = null;
        return key;
    }
}
