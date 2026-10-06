using System.Windows.Input;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Collector.Presentation.ViewModels;

public enum BannerSeverity
{
    Info,
    Warning,
    Error,
    Success,
}

public sealed record BannerContent
{
    public required BannerSeverity Severity { get; init; }

    public required string Title { get; init; }

    public string? Message { get; init; }

    public string? Glyph { get; init; }

    public string? AutomationName { get; init; }

    public string? ActionText { get; init; }

    public ICommand? ActionCommand { get; init; }

    public ICommand? DismissCommand { get; init; }
}

public sealed partial class BannerViewModel : ObservableObject
{
    public BannerViewModel(BannerContent content)
    {
        Severity = content.Severity;
        Title = content.Title;
        Message = content.Message;
        Glyph = content.Glyph ?? DefaultGlyph(content.Severity);
        AutomationName = content.AutomationName ?? $"{content.Title} {content.Message}".Trim();
        ActionText = content.ActionText;
        ActionCommand = content.ActionCommand;
        DismissCommand = content.DismissCommand;
    }

    public BannerSeverity Severity { get; }

    public string Title { get; }

    public string Glyph { get; }

    public string AutomationName { get; }

    public string? ActionText { get; }

    public ICommand? ActionCommand { get; }

    public ICommand? DismissCommand { get; }

    public bool HasAction => ActionCommand is not null && ActionText is not null;

    public bool CanDismiss => DismissCommand is not null;

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    public partial string? Message { get; set; }

    private static string DefaultGlyph(BannerSeverity severity) => severity switch
    {
        BannerSeverity.Error => Glyphs.Error,
        BannerSeverity.Warning => Glyphs.Warning,
        BannerSeverity.Success => Glyphs.Success,
        _ => Glyphs.Info,
    };
}
