using Collector.Application.Knowledge;
using Collector.Application.Ports;
using Collector.Application.Settings;
using Collector.Domain.Enums;
using Collector.Tests.Support;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Collector.Tests.Settings;

public sealed class AiModelChoiceServiceTests
{
    private sealed class FakeCatalog : IProviderModelCatalog
    {
        private readonly Dictionary<CollectorProvider, string[]> _models = new()
        {
            [CollectorProvider.Claude] = ["claude-a", "claude-b"],
            [CollectorProvider.Gemini] = ["gemini-a", "gemini-b"],
        };

        public IReadOnlyList<string> ModelsFor(CollectorProvider provider) =>
            _models.GetValueOrDefault(provider) ?? [];

        public string? DefaultModelFor(CollectorProvider provider) => ModelsFor(provider).FirstOrDefault();
    }

    private static AiModelChoiceService Create(
        FakeUserSettingsStore store,
        CollectorProvider configured = CollectorProvider.Claude) =>
        new(store, new FakeCatalog(), MsOptions.Create(new KnowledgeExtractionOptions { Provider = configured }));

    [Fact]
    public void Current_with_nothing_saved_is_the_configured_provider_default()
    {
        var service = Create(new FakeUserSettingsStore(), CollectorProvider.Gemini);

        Assert.Equal(new AiModelChoice(CollectorProvider.Gemini, "gemini-a"), service.Current);
    }

    [Fact]
    public async Task Current_uses_saved_choice_when_listed()
    {
        var store = new FakeUserSettingsStore();
        await store.SaveAiModelChoiceAsync(new AiModelChoice(CollectorProvider.Gemini, "gemini-b"), TestSupport.Ct);

        Assert.Equal(new AiModelChoice(CollectorProvider.Gemini, "gemini-b"), Create(store).Current);
    }

    [Fact]
    public async Task Current_falls_back_to_configured_default_when_saved_model_left_the_catalog()
    {
        var store = new FakeUserSettingsStore();
        await store.SaveAiModelChoiceAsync(new AiModelChoice(CollectorProvider.Gemini, "retired"), TestSupport.Ct);

        Assert.Equal(new AiModelChoice(CollectorProvider.Claude, "claude-a"), Create(store).Current);
    }

    [Fact]
    public async Task Current_falls_back_when_saved_provider_is_unknown()
    {
        var store = new FakeUserSettingsStore();
        await store.SaveAiModelChoiceAsync(new AiModelChoice(CollectorProvider.Bedrock, "x"), TestSupport.Ct);

        Assert.Equal(new AiModelChoice(CollectorProvider.Claude, "claude-a"), Create(store).Current);
    }

    [Fact]
    public void ModelsFor_lists_only_that_providers_models()
    {
        var service = Create(new FakeUserSettingsStore());

        Assert.Equal(["gemini-a", "gemini-b"], service.ModelsFor(CollectorProvider.Gemini));
        Assert.Empty(service.ModelsFor(CollectorProvider.Bedrock));
    }

    [Fact]
    public void DefaultModelFor_delegates_to_the_catalog()
    {
        var service = Create(new FakeUserSettingsStore());

        Assert.Equal("gemini-a", service.DefaultModelFor(CollectorProvider.Gemini));
        Assert.Null(service.DefaultModelFor(CollectorProvider.Bedrock));
    }

    [Fact]
    public async Task SaveAsync_rejects_a_model_not_listed_for_the_provider()
    {
        var store = new FakeUserSettingsStore();
        var service = Create(store);

        var saved = await service.SaveAsync(new AiModelChoice(CollectorProvider.Claude, "gemini-a"), TestSupport.Ct);

        Assert.False(saved);
        Assert.Null(store.Choice);
        Assert.Equal("claude-a", service.Current.Model);
    }

    [Fact]
    public async Task SaveAsync_persists_updates_current_and_raises_Changed()
    {
        var store = new FakeUserSettingsStore();
        var service = Create(store);
        var raised = 0;
        service.Changed += (_, _) => raised++;
        var choice = new AiModelChoice(CollectorProvider.Gemini, "gemini-b");

        var saved = await service.SaveAsync(choice, TestSupport.Ct);

        Assert.True(saved);
        Assert.Equal(choice, store.Choice);
        Assert.Equal(choice, service.Current);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task SaveAsync_rejection_does_not_raise_Changed()
    {
        var service = Create(new FakeUserSettingsStore());
        var raised = 0;
        service.Changed += (_, _) => raised++;

        await service.SaveAsync(new AiModelChoice(CollectorProvider.Claude, "nope"), TestSupport.Ct);

        Assert.Equal(0, raised);
    }
}
