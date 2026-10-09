using Collector.Presentation.Navigation;

namespace Collector.Tests.Presentation;

public sealed class SettingsNavigatorTests
{
    private readonly FakeNavigationService _navigation = new();

    private SettingsNavigator Create() => new(_navigation);

    [Fact]
    public void Selected_DefaultsToTheAiModelSection()
    {
        Assert.Equal(SettingsSection.AiModel, Create().Selected);
    }

    [Fact]
    public void Open_SelectsTheSectionAndNavigatesToSettings()
    {
        var navigator = Create();

        navigator.Open(SettingsSection.Connections);

        Assert.Equal(SettingsSection.Connections, navigator.Selected);
        Assert.Equal([ScreenKeys.Settings], _navigation.Visited);
    }

    [Fact]
    public void Open_RaisesChangedAndOpened()
    {
        var navigator = Create();
        var changed = 0;
        SettingsSection? opened = null;
        navigator.Changed += (_, _) => changed++;
        navigator.Opened += (_, section) => opened = section;

        navigator.Open(SettingsSection.ProviderKeys);

        Assert.Equal(1, changed);
        Assert.Equal(SettingsSection.ProviderKeys, opened);
    }

    [Fact]
    public void Selected_SettingTheSameSection_DoesNotRaiseChanged()
    {
        var navigator = Create();
        var changed = 0;
        navigator.Changed += (_, _) => changed++;

        navigator.Selected = SettingsSection.AiModel;

        Assert.Equal(0, changed);
    }

    [Fact]
    public void Selected_RemembersTheLastSectionAfterLeavingSettings()
    {
        var navigator = Create();
        navigator.Selected = SettingsSection.Connections;

        _navigation.NavigateTo(ScreenKeys.Extract);
        _navigation.NavigateTo(ScreenKeys.Settings);

        Assert.Equal(SettingsSection.Connections, navigator.Selected);
    }
}
