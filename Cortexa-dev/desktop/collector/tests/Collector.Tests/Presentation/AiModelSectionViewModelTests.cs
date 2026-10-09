using System.IO;
using Collector.Application.Secrets;
using Collector.Application.Settings;
using Collector.Domain.Enums;
using Collector.Presentation.Navigation;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using Collector.Presentation.ViewModels;
using Collector.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Collector.Tests.Presentation;

public sealed class AiModelSectionViewModelTests
{
    private readonly ModelChoiceHarness _harness = new();

    private static ProviderCardViewModel Card(AiModelSectionViewModel section, CollectorProvider provider) =>
        section.Cards.Single(card => card.Provider == provider);

    [Fact]
    public void Load_ReadsTheCurrentChoice()
    {
        var section = _harness.CreateSection();

        Assert.Equal(CollectorProvider.Claude, section.SelectedProvider);
        Assert.Equal("claude-a", section.SelectedModel);
        Assert.Equal(["claude-a", "claude-b"], section.Models);
        Assert.True(Card(section, CollectorProvider.Claude).IsSelected);
        Assert.False(Card(section, CollectorProvider.Gemini).IsSelected);
    }

    [Fact]
    public void PickingAProvider_FiltersTheModelsSelectsTheDefaultAndSaves()
    {
        var section = _harness.CreateSection();

        Card(section, CollectorProvider.Gemini).IsSelected = true;

        Assert.Equal(["gemini-a", "gemini-b"], section.Models);
        Assert.Equal("gemini-a", section.SelectedModel);
        Assert.Equal(new AiModelChoice(CollectorProvider.Gemini, "gemini-a"), _harness.Store.Choice);
        Assert.Equal(SettingsStrings.ChoiceSaved("Gemini", "gemini-a"), section.SavedMessage);
        Assert.Null(section.SaveBanner);
    }

    [Fact]
    public void PickingAProvider_SavesOnce()
    {
        var section = _harness.CreateSection();
        var saves = 0;
        _harness.Choices.Changed += (_, _) => saves++;

        Card(section, CollectorProvider.Gemini).IsSelected = true;

        Assert.Equal(1, saves);
    }

    [Fact]
    public void ChangingTheModel_SavesRightAway()
    {
        var section = _harness.CreateSection();

        section.SelectedModel = "claude-b";

        Assert.Equal(new AiModelChoice(CollectorProvider.Claude, "claude-b"), _harness.Store.Choice);
        Assert.Equal(SettingsStrings.ChoiceSaved("Claude", "claude-b"), section.SavedMessage);
    }

    [Fact]
    public void ChangingTheModel_NotListed_RevertsAndShowsAnErrorBanner()
    {
        var section = _harness.CreateSection();

        section.SelectedModel = "not-a-model";

        Assert.Null(_harness.Store.Choice);
        Assert.Equal("claude-a", section.SelectedModel);
        Assert.Null(section.SavedMessage);
        Assert.Equal(BannerSeverity.Error, section.SaveBanner!.Severity);
        Assert.Equal(SettingsStrings.ModelNotListedTitle, section.SaveBanner.Title);
        Assert.Equal(SettingsStrings.ModelNotListedMessage, section.SaveBanner.Message);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void ChangingTheModel_SaveThrows_RevertsAndShowsAnErrorBanner(Type failure)
    {
        var section = _harness.CreateSection();
        _harness.Store.ChoiceSaveFailure = (Exception)Activator.CreateInstance(failure)!;

        section.SelectedModel = "claude-b";

        Assert.Equal("claude-a", section.SelectedModel);
        Assert.Equal(_harness.Choices.Current.Model, section.SelectedModel);
        Assert.Equal(BannerSeverity.Error, section.SaveBanner!.Severity);
        Assert.Equal(SettingsStrings.ChoiceSaveFailedTitle, section.SaveBanner.Title);
        Assert.Equal(SettingsStrings.SaveFailedMessage, section.SaveBanner.Message);
    }

    [Fact]
    public void PickingAProvider_SaveThrows_RevertsToTheCurrentProvider()
    {
        var section = _harness.CreateSection();
        _harness.Store.ChoiceSaveFailure = new IOException();

        Card(section, CollectorProvider.Gemini).IsSelected = true;

        Assert.Equal(CollectorProvider.Claude, section.SelectedProvider);
        Assert.True(Card(section, CollectorProvider.Claude).IsSelected);
        Assert.Equal(["claude-a", "claude-b"], section.Models);
    }

    [Fact]
    public void PickingAProvider_WithNoModels_DisablesTheListAndDoesNotSave()
    {
        _harness.Catalog.Models[CollectorProvider.Gemini] = [];
        var section = _harness.CreateSection();

        Card(section, CollectorProvider.Gemini).IsSelected = true;

        Assert.False(section.HasModels);
        Assert.Equal(SettingsStrings.ModelsEmpty("Gemini"), section.ModelsError);
        Assert.Equal(string.Empty, section.ModelFieldHelper);
        Assert.Null(_harness.Store.Choice);
    }

    [Fact]
    public async Task Refresh_KeyMissing_ShowsTheKeyWarningWithAGoToKeysAction()
    {
        _harness.Readiness.States[CollectorProvider.Claude] = ProviderReadinessState.Missing;
        var section = _harness.CreateSection();

        await section.RefreshReadinessAsync();

        var banner = section.ReadinessBanner!;
        Assert.Equal(BannerSeverity.Warning, banner.Severity);
        Assert.Equal(SettingsStrings.KeyWarningTitle("Claude"), banner.Title);
        Assert.Equal(SettingsStrings.GoToKeys, banner.ActionText);
        banner.ActionCommand!.Execute(null);
        Assert.Equal(SettingsSection.ProviderKeys, _harness.Navigator.Selected);
        Assert.Equal([ScreenKeys.Settings], _harness.Navigation.Visited);
    }

    [Fact]
    public async Task Refresh_BedrockNotConnected_ShowsTheBedrockWarning()
    {
        _harness.Readiness.States[CollectorProvider.Bedrock] = ProviderReadinessState.Missing;
        var section = _harness.CreateSection();
        Card(section, CollectorProvider.Bedrock).IsSelected = true;

        await section.RefreshReadinessAsync();

        Assert.Equal(SettingsStrings.BedrockWarningTitle, section.ReadinessBanner!.Title);
        Assert.Equal(SettingsStrings.BedrockWarningMessage, section.ReadinessBanner.Message);
        Assert.Equal(SettingsStrings.BedrockNotConnectedChip, Card(section, CollectorProvider.Bedrock).ChipText);
    }

    [Fact]
    public async Task Refresh_AllProvidersReady_ShowsNoBannerAndTheReadyChips()
    {
        var section = _harness.CreateSection();

        await section.RefreshReadinessAsync();

        Assert.Null(section.ReadinessBanner);
        Assert.Equal(SettingsStrings.ChipKeySet, Card(section, CollectorProvider.Claude).ChipText);
        Assert.Equal(SettingsStrings.BedrockConnectedChip, Card(section, CollectorProvider.Bedrock).ChipText);
    }

    [Fact]
    public async Task Refresh_Unknown_ShowsTheUnknownBannerWithoutAnAction()
    {
        _harness.Readiness.States[CollectorProvider.Claude] = ProviderReadinessState.Unknown;
        var section = _harness.CreateSection();

        await section.RefreshReadinessAsync();

        Assert.Equal(SettingsStrings.ReadinessUnknownTitle("Claude"), section.ReadinessBanner!.Title);
        Assert.Null(section.ReadinessBanner.ActionText);
    }

    [Fact]
    public async Task Refresh_Pending_ShowsCheckingChipsAndNoBanner()
    {
        _harness.Readiness.States[CollectorProvider.Claude] = ProviderReadinessState.Missing;
        _harness.Readiness.Gate = new TaskCompletionSource();
        var section = _harness.CreateSection();

        var refresh = section.RefreshReadinessAsync();

        Assert.All(section.Cards, card => Assert.Equal(ProviderReadinessState.Checking, card.Readiness));
        Assert.Null(section.ReadinessBanner);
        _harness.Readiness.Gate.SetResult();
        await refresh;
        Assert.NotNull(section.ReadinessBanner);
    }

    [Fact]
    public async Task Refresh_StaleResultDoesNotOverwriteANewerOne()
    {
        _harness.Readiness.States[CollectorProvider.Claude] = ProviderReadinessState.Missing;
        _harness.Readiness.Gate = new TaskCompletionSource();
        var section = _harness.CreateSection();
        var stale = section.RefreshReadinessAsync();
        _harness.Readiness.States[CollectorProvider.Claude] = ProviderReadinessState.Ready;
        var current = section.RefreshReadinessAsync();

        _harness.Readiness.Gate.SetResult();
        await Task.WhenAll(stale, current);

        Assert.Equal(ProviderReadinessState.Ready, Card(section, CollectorProvider.Claude).Readiness);
        Assert.Null(section.ReadinessBanner);
    }

    [Fact]
    public async Task ReadinessChanged_RefreshesTheCards()
    {
        var section = _harness.CreateSection();
        await section.RefreshReadinessAsync();
        _harness.Readiness.States[CollectorProvider.Gemini] = ProviderReadinessState.Missing;

        _harness.Readiness.NotifyChanged();

        Assert.Equal(ProviderReadinessState.Missing, Card(section, CollectorProvider.Gemini).Readiness);
    }

    [Fact]
    public async Task OnNavigatedTo_ReloadsTheSavedChoice()
    {
        var section = _harness.CreateSection();
        await _harness.Choices.SaveAsync(new AiModelChoice(CollectorProvider.Gemini, "gemini-b"), TestSupport.Ct);

        section.OnNavigatedTo();

        Assert.Equal(CollectorProvider.Gemini, section.SelectedProvider);
        Assert.Equal("gemini-b", section.SelectedModel);
        Assert.Null(section.SavedMessage);
    }

    [Fact]
    public async Task ProviderReadiness_Bedrock_UsesTheSsoStatusNotTheKeyStore()
    {
        var sso = new StubBedrockSso { Status = new BedrockSsoStatus(true, DateTimeOffset.UtcNow.AddHours(1)) };
        var readiness = CreateReadiness(sso, new InMemorySecretStore());

        Assert.Equal(ProviderReadinessState.Ready, await readiness.CheckAsync(CollectorProvider.Bedrock, TestSupport.Ct));

        sso.Status = BedrockSsoStatus.NotConnected;
        Assert.Equal(ProviderReadinessState.Missing, await readiness.CheckAsync(CollectorProvider.Bedrock, TestSupport.Ct));
        Assert.False(await readiness.IsReadyAsync(CollectorProvider.Bedrock, TestSupport.Ct));
    }

    [Fact]
    public async Task ProviderReadiness_StatusCheckThrows_IsUnknown()
    {
        var readiness = CreateReadiness(new StubBedrockSso { StatusFailure = new InvalidOperationException() }, new InMemorySecretStore());

        Assert.Equal(ProviderReadinessState.Unknown, await readiness.CheckAsync(CollectorProvider.Bedrock, TestSupport.Ct));
    }

    [Fact]
    public async Task ProviderReadiness_DirectProviders_FollowTheStoredKey()
    {
        var secrets = new InMemorySecretStore();
        var readiness = CreateReadiness(new StubBedrockSso(), secrets);

        Assert.Equal(ProviderReadinessState.Missing, await readiness.CheckAsync(CollectorProvider.Claude, TestSupport.Ct));

        secrets.Values[SecretSlot.AnthropicApiKey] = "key";
        Assert.Equal(ProviderReadinessState.Ready, await readiness.CheckAsync(CollectorProvider.Claude, TestSupport.Ct));
        Assert.Equal(ProviderReadinessState.Missing, await readiness.CheckAsync(CollectorProvider.Gemini, TestSupport.Ct));
    }

    private static ProviderReadiness CreateReadiness(StubBedrockSso sso, InMemorySecretStore secrets) =>
        new(new SettingsService(new FakeUserSettingsStore(), secrets), sso, NullLogger<ProviderReadiness>.Instance);
}
