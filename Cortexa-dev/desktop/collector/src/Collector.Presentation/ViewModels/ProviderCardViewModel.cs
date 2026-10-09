using Collector.Domain.Enums;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Collector.Presentation.ViewModels;

public sealed partial class ProviderCardViewModel(ProviderCardContent content, Action<CollectorProvider> select) : ObservableObject
{
    private bool _isSelected;

    public CollectorProvider Provider { get; } = content.Provider;

    public string Title { get; } = content.Title;

    public string Description { get; } = content.Description;

    public string Monogram { get; } = content.Monogram;

    public string ChipText => ProviderPresentation.Chip(Provider, Readiness);

    public string AutomationName => SettingsStrings.ProviderCardName(Title, ChipText);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value && !_isSelected)
            {
                select(Provider);
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChipText), nameof(AutomationName))]
    public partial ProviderReadinessState Readiness { get; set; } = ProviderReadinessState.Checking;

    public void Sync(CollectorProvider selected) => SetProperty(ref _isSelected, selected == Provider, nameof(IsSelected));
}
