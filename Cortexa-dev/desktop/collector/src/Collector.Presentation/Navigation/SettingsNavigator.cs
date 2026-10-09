namespace Collector.Presentation.Navigation;

public sealed class SettingsNavigator(INavigationService navigation)
{
    private SettingsSection _selected = SettingsSection.AiModel;

    public event EventHandler? Changed;

    public event EventHandler<SettingsSection>? Opened;

    public SettingsSection Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }

            _selected = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Open(SettingsSection section)
    {
        Selected = section;
        navigation.NavigateTo(ScreenKeys.Settings);
        Opened?.Invoke(this, section);
    }
}
