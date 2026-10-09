using System.Text.Json;
using Collector.Application.Settings;
using Collector.Domain.Enums;
using Collector.Infrastructure.Options;
using Collector.Infrastructure.Settings;
using Collector.Tests.Support;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Collector.Tests.Settings;

public sealed class JsonUserSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"collector-settings-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static JsonUserSettingsStore CreateStore(string path, AiModelChoiceOptions? choice = null) => new(
        MsOptions.Create(new UserSettingsOptions { Path = path }),
        new StaticMonitor<GatewayOptions>(new GatewayOptions { BaseUrl = "https://current-gw.example" }),
        new StaticMonitor<CollectorServerOptions>(new CollectorServerOptions { BaseUrl = "https://current-srv.example" }),
        new StaticMonitor<RemoteSourceOptions>(new RemoteSourceOptions()),
        new StaticMonitor<AiModelChoiceOptions>(choice ?? new AiModelChoiceOptions()));

    [Fact]
    public void Reads_current_endpoints_from_options()
    {
        var store = CreateStore(Path.Combine(_directory, "s.json"));

        Assert.Equal(new EndpointSettings("https://current-gw.example", "https://current-srv.example"), store.GetEndpoints());
    }

    [Fact]
    public async Task Writes_option_shaped_json_and_leaves_no_temp_files()
    {
        var path = Path.Combine(_directory, "nested", "usersettings.json");
        var store = CreateStore(path);

        await store.SaveEndpointsAsync(new EndpointSettings("https://gw.example", "https://srv.example"), TestSupport.Ct);
        await store.SaveEndpointsAsync(new EndpointSettings("https://gw2.example", "https://srv2.example"), TestSupport.Ct);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, TestSupport.Ct));
        var root = document.RootElement;
        Assert.Equal("https://gw2.example", root.GetProperty("Gateway").GetProperty("BaseUrl").GetString());
        Assert.Equal("https://srv2.example", root.GetProperty("CollectorServer").GetProperty("BaseUrl").GetString());
        Assert.Equal([path], Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public async Task Saves_model_choice_under_its_own_root_key()
    {
        var path = Path.Combine(_directory, "choice.json");
        var store = CreateStore(path);

        await store.SaveAiModelChoiceAsync(new AiModelChoice(CollectorProvider.Gemini, "gemini-3.8-pro"), TestSupport.Ct);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, TestSupport.Ct));
        var section = document.RootElement.GetProperty("AiModelChoice");
        Assert.Equal("Gemini", section.GetProperty("Provider").GetString());
        Assert.Equal("gemini-3.8-pro", section.GetProperty("Model").GetString());
        Assert.False(document.RootElement.TryGetProperty("Ai", out _));
    }

    [Fact]
    public async Task Saving_model_choice_keeps_endpoints_and_remote_sources()
    {
        var path = Path.Combine(_directory, "keep.json");
        var store = CreateStore(path);
        await store.SaveEndpointsAsync(new EndpointSettings("https://gw.example", "https://srv.example"), TestSupport.Ct);
        await store.SaveRemoteSourcesAsync(new RemoteSourceSettings("acme"), TestSupport.Ct);

        await store.SaveAiModelChoiceAsync(new AiModelChoice(CollectorProvider.Claude, "claude-opus-5"), TestSupport.Ct);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path, TestSupport.Ct));
        var root = document.RootElement;
        Assert.Equal("https://gw.example", root.GetProperty("Gateway").GetProperty("BaseUrl").GetString());
        Assert.Equal("acme", root.GetProperty("RemoteSources").GetProperty("AzureDevOps").GetProperty("Organization").GetString());
        Assert.Equal("claude-opus-5", root.GetProperty("AiModelChoice").GetProperty("Model").GetString());
    }

    [Fact]
    public void Reads_saved_model_choice_from_options()
    {
        var store = CreateStore(Path.Combine(_directory, "read.json"), new AiModelChoiceOptions
        {
            Provider = CollectorProvider.Bedrock,
            Model = "m1",
        });

        Assert.Equal(new AiModelChoice(CollectorProvider.Bedrock, "m1"), store.GetAiModelChoice());
    }

    [Fact]
    public void Reads_null_when_no_model_choice_is_saved()
    {
        var store = CreateStore(Path.Combine(_directory, "none.json"));

        Assert.Null(store.GetAiModelChoice());
    }
}
