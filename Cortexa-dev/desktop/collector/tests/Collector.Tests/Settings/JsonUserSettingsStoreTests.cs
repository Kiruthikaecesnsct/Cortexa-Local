using System.Text.Json;
using Collector.Application.Settings;
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

    private static JsonUserSettingsStore CreateStore(string path) => new(
        MsOptions.Create(new UserSettingsOptions { Path = path }),
        new StaticMonitor<GatewayOptions>(new GatewayOptions { BaseUrl = "https://current-gw.example" }),
        new StaticMonitor<CollectorServerOptions>(new CollectorServerOptions { BaseUrl = "https://current-srv.example" }),
        new StaticMonitor<RemoteSourceOptions>(new RemoteSourceOptions()));

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
}
