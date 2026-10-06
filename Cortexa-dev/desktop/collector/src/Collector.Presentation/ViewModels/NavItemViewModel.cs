using System.Windows.Input;
using Collector.Presentation.Navigation;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collector.Presentation.ViewModels;

public sealed partial class NavItemViewModel : ObservableObject
{
    public NavItemViewModel(ScreenRegistration registration, Action<string> navigate)
    {
        Registration = registration;
        ActivateCommand = new RelayCommand(() => navigate(registration.Key));
        IsEnabled = !registration.RequiresSignIn;
    }

    public ScreenRegistration Registration { get; }

    public ICommand ActivateCommand { get; }

    public string Title => Registration.Title;

    public string Glyph => Registration.Glyph;

    public bool ShowLabel => !IsCompact;

    public bool ShowLock => !IsEnabled;

    public string? HelpText => IsEnabled ? null : ShellStrings.RequiresSignInHelp;

    public string? ToolTip => IsEnabled ? (IsCompact ? Title : null) : ShellStrings.SignInRequired;

    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLock), nameof(HelpText), nameof(ToolTip))]
    public partial bool IsEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLabel), nameof(ToolTip))]
    public partial bool IsCompact { get; set; }
}
