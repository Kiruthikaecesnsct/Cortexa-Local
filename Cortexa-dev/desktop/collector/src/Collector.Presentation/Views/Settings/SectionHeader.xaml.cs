using System.Windows;
using System.Windows.Controls;

namespace Collector.Presentation.Views.SettingsSections;

public partial class SectionHeader : UserControl
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph),
        typeof(string),
        typeof(SectionHeader),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(SectionHeader),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IntroProperty = DependencyProperty.Register(
        nameof(Intro),
        typeof(string),
        typeof(SectionHeader),
        new PropertyMetadata(string.Empty));

    public SectionHeader() => InitializeComponent();

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Intro
    {
        get => (string)GetValue(IntroProperty);
        set => SetValue(IntroProperty, value);
    }
}
