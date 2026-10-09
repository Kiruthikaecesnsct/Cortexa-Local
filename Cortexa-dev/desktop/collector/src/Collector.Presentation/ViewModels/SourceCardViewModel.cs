using Collector.Domain.Enums;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Collector.Presentation.ViewModels;

public sealed class SourceCardViewModel(SourceCardContent content, Action<SourceType> select) : ObservableObject
{
    private bool _isSelected;

    public SourceType Source { get; } = content.Source;

    public string IconGlyph { get; } = content.IconGlyph;

    public string AccessLabel { get; } = content.AccessLabel;

    public string Title { get; } = content.Title;

    public string Description { get; } = content.Description;

    public string AutomationName { get; } = $"{content.Title}. {content.Description}";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value && !_isSelected)
            {
                select(Source);
            }
        }
    }

    public void Sync(SourceType selected) => SetProperty(ref _isSelected, selected == Source, nameof(IsSelected));
}

public sealed record SourceCardContent(SourceType Source, string IconGlyph, string AccessLabel, string Title, string Description);

public static class SourceCardCatalog
{
    public static IReadOnlyList<SourceCardContent> All { get; } =
    [
        new(SourceType.Local, Resources.Glyphs.Document, Resources.RemoteSourceStrings.LocalLabel, Resources.ExtractionStrings.SourceLocalTitle, Resources.ExtractionStrings.SourceLocalDescription),
        new(SourceType.Github, Resources.Glyphs.Code, Resources.RemoteSourceStrings.GitHubLabel, Resources.ExtractionStrings.SourceGitHubTitle, Resources.ExtractionStrings.SourceGitHubDescription),
        new(SourceType.AzureDevops, Resources.Glyphs.Cloud, Resources.RemoteSourceStrings.AzureDevOpsLabel, Resources.ExtractionStrings.SourceAzureDevOpsTitle, Resources.ExtractionStrings.SourceAzureDevOpsDescription),
        new(SourceType.Ssh, Resources.Glyphs.CommandPrompt, Resources.RemoteSourceStrings.SshLabel, Resources.ExtractionStrings.SourceSshTitle, Resources.ExtractionStrings.SourceSshDescription),
        new(SourceType.CortexaRepo, Resources.Glyphs.Repository, Resources.RemoteSourceStrings.CortexaLabel, Resources.ExtractionStrings.SourceCortexaTitle, Resources.ExtractionStrings.SourceCortexaDescription),
    ];
}
