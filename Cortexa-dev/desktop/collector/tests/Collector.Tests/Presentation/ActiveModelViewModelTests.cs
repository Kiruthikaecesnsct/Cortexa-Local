using Collector.Domain.Enums;
using Collector.Presentation.Navigation;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;

namespace Collector.Tests.Presentation;

public sealed class ActiveModelViewModelTests
{
    [Fact]
    public async Task ActiveModel_ChoiceChanged_FollowsTheNewChoiceAndRefreshesReadiness()
    {
        var harness = new ModelChoiceHarness();
        harness.Readiness.States[CollectorProvider.Gemini] = ProviderReadinessState.Missing;
        var active = harness.CreateActiveModel();
        var updates = 0;
        active.Updated += (_, _) => updates++;

        await harness.Choices.SaveAsync(new Collector.Application.Settings.AiModelChoice(CollectorProvider.Gemini, "gemini-b"), TestContext.Current.CancellationToken);

        Assert.Equal(CollectorProvider.Gemini, active.Provider);
        Assert.Equal("gemini-b", active.Model);
        Assert.Equal(ProviderReadinessState.Missing, active.Readiness);
        Assert.False(active.IsReady);
        Assert.Equal(ExtractionStrings.AddKeyInSettings, active.AddKeyText);
        Assert.Equal(ExtractionStrings.ModelLineName("Gemini", "gemini-b"), active.GroupName);
        Assert.True(updates > 0);
    }

    [Fact]
    public async Task ActiveModel_Bedrock_UsesTheConnectCopy()
    {
        var harness = new ModelChoiceHarness(CollectorProvider.Bedrock);
        harness.Readiness.States[CollectorProvider.Bedrock] = ProviderReadinessState.Missing;
        var active = harness.CreateActiveModel();

        await active.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ExtractionStrings.ConnectInSettings, active.AddKeyText);
        Assert.Equal(ExtractionStrings.ConnectInSettingsName, active.AddKeyName);
        Assert.Equal(SettingsStrings.BedrockNotConnectedChip, active.ChipText);
    }

    [Fact]
    public void ActiveModel_Commands_OpenTheMatchingSettingsSection()
    {
        var harness = new ModelChoiceHarness();
        var active = harness.CreateActiveModel();

        active.AddKeyCommand.Execute(null);
        Assert.Equal(SettingsSection.ProviderKeys, harness.Navigator.Selected);

        active.ChangeCommand.Execute(null);
        Assert.Equal(SettingsSection.AiModel, harness.Navigator.Selected);
        Assert.Equal([ScreenKeys.Settings, ScreenKeys.Settings], harness.Navigation.Visited);
    }

    [Fact]
    public async Task ActiveModel_RefreshWhilePending_ShowsChecking()
    {
        var harness = new ModelChoiceHarness();
        harness.Readiness.Gate = new TaskCompletionSource();
        var active = harness.CreateActiveModel();

        var refresh = active.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ProviderReadinessState.Checking, active.Readiness);
        harness.Readiness.Gate.SetResult();
        await refresh;
        Assert.Equal(ProviderReadinessState.Ready, active.Readiness);
    }
}
