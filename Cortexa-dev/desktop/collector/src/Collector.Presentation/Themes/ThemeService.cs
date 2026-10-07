using System.ComponentModel;
using System.Windows;

namespace Collector.Presentation.Themes;

public sealed class ThemeService
{
    private const string LightColors = "/Collector.Presentation;component/Themes/Colors.Light.xaml";
    private const string HighContrastColors = "/Collector.Presentation;component/Themes/Colors.HighContrast.xaml";
    private const string HighContrastProperty = nameof(SystemParameters.HighContrast);

    private readonly System.Windows.Application _application;
    private ResourceDictionary? _active;

    public ThemeService(System.Windows.Application application) => _application = application;

    public void Start()
    {
        Apply();
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
    }

    public void Stop() => SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;

    private void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == HighContrastProperty)
        {
            _application.Dispatcher.BeginInvoke(Apply);
        }
    }

    private void Apply()
    {
        var source = new Uri(SystemParameters.HighContrast ? HighContrastColors : LightColors, UriKind.Relative);
        var next = new ResourceDictionary { Source = source };
        var merged = _application.Resources.MergedDictionaries;
        var index = _active is null ? -1 : merged.IndexOf(_active);
        if (index < 0)
        {
            merged.Insert(0, next);
        }
        else
        {
            merged[index] = next;
        }

        _active = next;
    }
}
