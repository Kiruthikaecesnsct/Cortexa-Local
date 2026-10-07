using Collector.Application.Secrets;
using Collector.Application.Settings;
using Collector.Domain.Enums;
using Collector.Tests.Support;

namespace Collector.Tests.Settings;

public class SettingsServiceTests
{
    private readonly FakeUserSettingsStore _store = new();
    private readonly InMemorySecretStore _secrets = new();
    private readonly SettingsService _service;

    public SettingsServiceTests()
    {
        _service = new SettingsService(_store, _secrets);
    }

    [Fact]
    public async Task Saves_valid_endpoints_trimmed()
    {
        var result = await _service.SaveEndpointsAsync(
            new EndpointSettings(" https://gw.example ", "http://localhost:9000 "),
            TestSupport.Ct);

        Assert.True(result.IsValid);
        Assert.Equal(new EndpointSettings("https://gw.example", "http://localhost:9000"), _store.Current);
    }

    [Fact]
    public async Task Does_not_save_invalid_endpoints()
    {
        var result = await _service.SaveEndpointsAsync(
            new EndpointSettings("https://gw.example", "http://remote.example"),
            TestSupport.Ct);

        Assert.Equal(EndpointField.CollectorServerUrl, result.Field);
        Assert.Equal(0, _store.SaveCalls);
    }

    [Fact]
    public async Task Tracks_ai_key_presence_without_returning_the_key()
    {
        Assert.False(await _service.HasAiKeyAsync(CollectorProvider.Claude, TestSupport.Ct));

        await _service.SetAiKeyAsync(CollectorProvider.Claude, "  sk-key  ", TestSupport.Ct);

        Assert.True(await _service.HasAiKeyAsync(CollectorProvider.Claude, TestSupport.Ct));
        Assert.False(await _service.HasAiKeyAsync(CollectorProvider.Gemini, TestSupport.Ct));
        Assert.Equal("sk-key", _secrets.Values[SecretSlot.AnthropicApiKey]);
    }

    [Fact]
    public async Task Clears_an_ai_key()
    {
        await _service.SetAiKeyAsync(CollectorProvider.Gemini, "key", TestSupport.Ct);

        await _service.ClearAiKeyAsync(CollectorProvider.Gemini, TestSupport.Ct);

        Assert.False(await _service.HasAiKeyAsync(CollectorProvider.Gemini, TestSupport.Ct));
    }

    [Fact]
    public async Task Bedrock_has_no_key()
    {
        Assert.False(await _service.HasAiKeyAsync(CollectorProvider.Bedrock, TestSupport.Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.SetAiKeyAsync(CollectorProvider.Bedrock, "key", TestSupport.Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ClearAiKeyAsync(CollectorProvider.Bedrock, TestSupport.Ct));
    }

    [Fact]
    public async Task Rejects_a_blank_key()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => _service.SetAiKeyAsync(CollectorProvider.Claude, "   ", TestSupport.Ct));
    }
}
