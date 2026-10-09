using System.Collections.ObjectModel;
using System.IO;
using Collector.Application.Settings;
using Collector.Domain.Enums;
using Collector.Presentation.Navigation;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Collector.Presentation.ViewModels;

public static class AiModelFocusKeys
{
    public const string CheckedCard = "CheckedProvider";
}

public sealed partial class AiModelSectionViewModel : FocusableViewModel
{
    private readonly AiModelChoiceService _choices;
    private readonly IProviderReadiness _readiness;
    private readonly SettingsNavigator _navigator;
    private readonly ILogger<AiModelSectionViewModel> _logger;
    private bool _syncing;
    private int _refreshGeneration;

    public AiModelSectionViewModel(
        AiModelChoiceService choices,
        IProviderReadiness readiness,
        SettingsNavigator navigator,
        ILogger<AiModelSectionViewModel> logger)
    {
        _choices = choices;
        _readiness = readiness;
        _navigator = navigator;
        _logger = logger;
        Cards = [.. ProviderPresentation.Cards.Select(content => new ProviderCardViewModel(content, OnProviderPicked))];
        Models = [];
        ApplyChoice(choices.Current);
        readiness.Changed += (_, _) => _ = RefreshReadinessAsync();
    }

    public IReadOnlyList<ProviderCardViewModel> Cards { get; }

    public ObservableCollection<string> Models { get; }

    public bool HasModels => Models.Count > 0;

    public bool ShowModelsEmpty => !HasModels;

    public bool HasSaved => !string.IsNullOrEmpty(SavedMessage);

    public string ProviderName => ProviderPresentation.Title(SelectedProvider);

    public string ModelHelper => SettingsStrings.ModelHelper(ProviderName);

    public string ModelsEmptyMessage => SettingsStrings.ModelsEmpty(ProviderName);

    public string ModelFieldHelper => HasModels ? ModelHelper : string.Empty;

    public string? ModelsError => HasModels ? null : ModelsEmptyMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(ProviderName),
        nameof(ModelHelper),
        nameof(ModelsEmptyMessage),
        nameof(ModelFieldHelper),
        nameof(ModelsError))]
    public partial CollectorProvider SelectedProvider { get; set; }

    [ObservableProperty]
    public partial string? SelectedModel { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSaved))]
    public partial string? SavedMessage { get; set; }

    [ObservableProperty]
    public partial BannerViewModel? SaveBanner { get; set; }

    [ObservableProperty]
    public partial BannerViewModel? ReadinessBanner { get; set; }

    public void OnNavigatedTo()
    {
        SavedMessage = null;
        SaveBanner = null;
        ApplyChoice(_choices.Current);
        _ = RefreshReadinessAsync();
    }

    public void FocusCheckedCard() => RequestFocus(AiModelFocusKeys.CheckedCard);

    public async Task RefreshReadinessAsync()
    {
        var generation = Interlocked.Increment(ref _refreshGeneration);
        foreach (var card in Cards)
        {
            card.Readiness = ProviderReadinessState.Checking;
        }

        ReadinessBanner = null;
        await Task.WhenAll(Cards.Select(card => RefreshCardAsync(card, generation)));
        if (generation == _refreshGeneration)
        {
            ReadinessBanner = BuildReadinessBanner();
        }
    }

    partial void OnSelectedModelChanged(string? value)
    {
        if (_syncing || value is null)
        {
            return;
        }

        _ = SaveSelectionAsync();
    }

    private async Task RefreshCardAsync(ProviderCardViewModel card, int generation)
    {
        var state = await _readiness.CheckAsync(card.Provider, CancellationToken.None);
        if (generation == _refreshGeneration)
        {
            card.Readiness = state;
        }
    }

    private void OnProviderPicked(CollectorProvider provider)
    {
        ApplyChoice(new AiModelChoice(provider, DefaultModelFor(provider)));
        ReadinessBanner = BuildReadinessBanner();
        _ = SaveSelectionAsync();
    }

    private string DefaultModelFor(CollectorProvider provider) =>
        _choices.DefaultModelFor(provider) ?? _choices.ModelsFor(provider).FirstOrDefault() ?? string.Empty;

    private void ApplyChoice(AiModelChoice choice)
    {
        _syncing = true;
        try
        {
            SelectedProvider = choice.Provider;
            Models.Clear();
            foreach (var model in _choices.ModelsFor(choice.Provider))
            {
                Models.Add(model);
            }

            SelectedModel = Models.Contains(choice.Model) ? choice.Model : null;
            OnPropertyChanged(nameof(HasModels));
            OnPropertyChanged(nameof(ShowModelsEmpty));
            OnPropertyChanged(nameof(ModelFieldHelper));
            OnPropertyChanged(nameof(ModelsError));
            foreach (var card in Cards)
            {
                card.Sync(choice.Provider);
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private async Task SaveSelectionAsync()
    {
        if (!HasModels || SelectedModel is null)
        {
            return;
        }

        var choice = new AiModelChoice(SelectedProvider, SelectedModel);
        try
        {
            var saved = await _choices.SaveAsync(choice, CancellationToken.None);
            if (saved)
            {
                ShowSaved(choice);
                return;
            }

            Revert(SettingsStrings.ModelNotListedTitle, SettingsStrings.ModelNotListedMessage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save the AI model choice.");
            Revert(SettingsStrings.ChoiceSaveFailedTitle, SettingsStrings.SaveFailedMessage);
        }
    }

    private void ShowSaved(AiModelChoice choice)
    {
        SaveBanner = null;
        SavedMessage = SettingsStrings.ChoiceSaved(ProviderPresentation.Title(choice.Provider), choice.Model);
    }

    private void Revert(string title, string message)
    {
        SavedMessage = null;
        ApplyChoice(_choices.Current);
        ReadinessBanner = BuildReadinessBanner();
        SaveBanner = new BannerViewModel(new BannerContent
        {
            Severity = BannerSeverity.Error,
            Title = title,
            Message = message,
        });
    }

    private BannerViewModel? BuildReadinessBanner()
    {
        var state = Cards.First(card => card.Provider == SelectedProvider).Readiness;
        return state switch
        {
            ProviderReadinessState.Missing => MissingBanner(),
            ProviderReadinessState.Unknown => UnknownBanner(),
            _ => null,
        };
    }

    private BannerViewModel MissingBanner()
    {
        var bedrock = SelectedProvider == CollectorProvider.Bedrock;
        return GoToKeysBanner(
            bedrock ? SettingsStrings.BedrockWarningTitle : SettingsStrings.KeyWarningTitle(ProviderName),
            bedrock ? SettingsStrings.BedrockWarningMessage : SettingsStrings.KeyWarningMessage,
            withAction: true);
    }

    private BannerViewModel UnknownBanner() =>
        GoToKeysBanner(SettingsStrings.ReadinessUnknownTitle(ProviderName), SettingsStrings.ReadinessUnknownMessage, withAction: false);

    private BannerViewModel GoToKeysBanner(string title, string message, bool withAction) => new(new BannerContent
    {
        Severity = BannerSeverity.Warning,
        Title = title,
        Message = message,
        ActionText = withAction ? SettingsStrings.GoToKeys : null,
        ActionName = withAction ? SettingsStrings.GoToKeysName : null,
        ActionCommand = withAction ? new RelayCommand(() => _navigator.Open(SettingsSection.ProviderKeys)) : null,
    });
}
