using Collector.Application.Settings;
using Collector.Domain.Enums;
using Collector.Presentation.Navigation;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collector.Presentation.ViewModels;

public sealed partial class ActiveModelViewModel : ObservableObject
{
    private readonly AiModelChoiceService _choices;
    private readonly IProviderReadiness _readiness;
    private readonly SettingsNavigator _navigator;
    private int _generation;

    public ActiveModelViewModel(AiModelChoiceService choices, IProviderReadiness readiness, SettingsNavigator navigator)
    {
        _choices = choices;
        _readiness = readiness;
        _navigator = navigator;
        Provider = choices.Current.Provider;
        Model = choices.Current.Model;
        choices.Changed += (_, _) => OnChoiceChanged();
        readiness.Changed += (_, _) => _ = RefreshAsync(CancellationToken.None);
    }

    public event EventHandler? Updated;

    public string ProviderName => ProviderPresentation.Title(Provider);

    public string ChipText => ProviderPresentation.Chip(Provider, Readiness);

    public bool IsReady => Readiness == ProviderReadinessState.Ready;

    public bool ShowChip => !IsReady;

    public bool ShowAddKey => !IsReady;

    public bool IsBedrock => Provider == CollectorProvider.Bedrock;

    public string AddKeyText => IsBedrock ? ExtractionStrings.ConnectInSettings : ExtractionStrings.AddKeyInSettings;

    public string AddKeyName => IsBedrock
        ? ExtractionStrings.ConnectInSettingsName
        : ExtractionStrings.AddKeyInSettingsName(ProviderName);

    public string GroupName => ExtractionStrings.ModelLineName(ProviderName, Model);

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(ProviderName),
        nameof(ChipText),
        nameof(IsBedrock),
        nameof(AddKeyText),
        nameof(AddKeyName),
        nameof(GroupName))]
    public partial CollectorProvider Provider { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GroupName))]
    public partial string Model { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChipText), nameof(IsReady), nameof(ShowChip), nameof(ShowAddKey))]
    public partial ProviderReadinessState Readiness { get; private set; } = ProviderReadinessState.Checking;

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var generation = Interlocked.Increment(ref _generation);
        var provider = Provider;
        Readiness = ProviderReadinessState.Checking;
        Updated?.Invoke(this, EventArgs.Empty);
        var state = await _readiness.CheckAsync(provider, cancellationToken);
        if (generation != _generation)
        {
            return;
        }

        Readiness = state;
        Updated?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Change() => _navigator.Open(SettingsSection.AiModel);

    [RelayCommand]
    private void AddKey() => _navigator.Open(SettingsSection.ProviderKeys);

    private void OnChoiceChanged()
    {
        Provider = _choices.Current.Provider;
        Model = _choices.Current.Model;
        _ = RefreshAsync(CancellationToken.None);
    }
}
