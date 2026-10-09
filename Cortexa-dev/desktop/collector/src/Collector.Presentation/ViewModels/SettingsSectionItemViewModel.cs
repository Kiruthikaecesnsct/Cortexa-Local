using Collector.Presentation.Navigation;

namespace Collector.Presentation.ViewModels;

public sealed class SettingsSectionItemViewModel(SettingsSection section, string title, string glyph, object content)
{
    public SettingsSection Section { get; } = section;

    public string Title { get; } = title;

    public string Glyph { get; } = glyph;

    public object Content { get; } = content;
}

public sealed record ProviderKeysSection(SettingsViewModel Owner);

public sealed record ConnectionsSection(SettingsViewModel Owner);
