using System.Text.Json;
using Collector.Application.Settings;
using Collector.Infrastructure.Options;
using Collector.Infrastructure.Settings;
using Collector.Tests.Support;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Collector.Tests.Settings;

public sealed class JsonUserSettingsStoreRemoteTests : IDisposable
{
    private const string Organization = "my-org";
    private const string CurrentGateway = "https://current-gw.example";
    private const int CustomTimeoutSeconds = 45;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"collector-settings-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string SettingsPath => Path.Combine(_directory, "usersettings.json");

    private JsonUserSettingsStore CreateStore(string? organization = null)
    {
        var remote = new RemoteSourceOptions();
        remote.AzureDevOps.Organization = organization ?? string.Empty;
        return new JsonUserSettingsStore(
            MsOptions.Create(new UserSettingsOptions { Path = SettingsPath }),
            new StaticMonitor<GatewayOptions>(new GatewayOptions { BaseUrl = CurrentGateway }),
            new StaticMonitor<CollectorServerOptions>(new CollectorServerOptions { BaseUrl = "https://current-srv.example" }),
            new StaticMonitor<RemoteSourceOptions>(remote),
        new StaticMonitor<AiModelChoiceOptions>(new AiModelChoiceOptions()),
            new StaticMonitor<GeminiRotationOptions>(new GeminiRotationOptions()));
    }

    private JsonElement ReadRoot() => JsonDocument.Parse(File.ReadAllText(SettingsPath)).RootElement;

    private void WriteRaw(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, json);
    }

    [Fact]
    public void GetRemoteSources_ConfiguredOrganization_ReturnsIt()
    {
        var store = CreateStore(Organization);

        Assert.Equal(new RemoteSourceSettings(Organization), store.GetRemoteSources());
    }

    [Fact]
    public async Task SaveRemoteSourcesAsync_NewFile_WritesOrganizationUnderRemoteSources()
    {
        await CreateStore().SaveRemoteSourcesAsync(new RemoteSourceSettings(Organization), TestSupport.Ct);

        var organization = ReadRoot().GetProperty("RemoteSources").GetProperty("AzureDevOps").GetProperty("Organization");
        Assert.Equal(Organization, organization.GetString());
    }

    [Fact]
    public async Task SaveRemoteSourcesAsync_ExistingEndpoints_KeepsEndpointSections()
    {
        var store = CreateStore();
        await store.SaveEndpointsAsync(new EndpointSettings("https://gw.example", "https://srv.example"), TestSupport.Ct);

        await store.SaveRemoteSourcesAsync(new RemoteSourceSettings(Organization), TestSupport.Ct);

        var root = ReadRoot();
        Assert.Equal("https://gw.example", root.GetProperty("Gateway").GetProperty("BaseUrl").GetString());
        Assert.Equal("https://srv.example", root.GetProperty("CollectorServer").GetProperty("BaseUrl").GetString());
        Assert.Equal(Organization, root.GetProperty("RemoteSources").GetProperty("AzureDevOps").GetProperty("Organization").GetString());
    }

    [Fact]
    public async Task SaveEndpointsAsync_ExistingRemoteSources_KeepsRemoteSourcesSection()
    {
        var store = CreateStore();
        await store.SaveRemoteSourcesAsync(new RemoteSourceSettings(Organization), TestSupport.Ct);

        await store.SaveEndpointsAsync(new EndpointSettings("https://gw.example", "https://srv.example"), TestSupport.Ct);

        var root = ReadRoot();
        Assert.Equal(Organization, root.GetProperty("RemoteSources").GetProperty("AzureDevOps").GetProperty("Organization").GetString());
        Assert.Equal("https://gw.example", root.GetProperty("Gateway").GetProperty("BaseUrl").GetString());
    }

    [Fact]
    public async Task SaveEndpointsAsync_LegacyFileWithoutRemoteSources_DoesNotAddTheSection()
    {
        WriteRaw("""{"Gateway":{"BaseUrl":"https://old-gw.example"}}""");

        await CreateStore().SaveEndpointsAsync(new EndpointSettings("https://gw.example", "https://srv.example"), TestSupport.Ct);

        Assert.False(ReadRoot().TryGetProperty("RemoteSources", out _));
    }

    [Fact]
    public async Task SaveRemoteSourcesAsync_LegacyFileWithoutRemoteSources_AddsSectionAndKeepsGateway()
    {
        WriteRaw("""{"Gateway":{"BaseUrl":"https://old-gw.example"}}""");

        await CreateStore().SaveRemoteSourcesAsync(new RemoteSourceSettings(Organization), TestSupport.Ct);

        var root = ReadRoot();
        Assert.Equal("https://old-gw.example", root.GetProperty("Gateway").GetProperty("BaseUrl").GetString());
        Assert.Equal(Organization, root.GetProperty("RemoteSources").GetProperty("AzureDevOps").GetProperty("Organization").GetString());
    }

    [Fact]
    public async Task SaveRemoteSourcesAsync_OtherRemoteSourceSettings_AreKept()
    {
        WriteRaw("{\"RemoteSources\":{\"TimeoutSeconds\":" + CustomTimeoutSeconds + ",\"AzureDevOps\":{\"Organization\":\"old\",\"ApiVersion\":\"7.0\"}}}");

        await CreateStore().SaveRemoteSourcesAsync(new RemoteSourceSettings(Organization), TestSupport.Ct);

        var section = ReadRoot().GetProperty("RemoteSources");
        Assert.Equal(CustomTimeoutSeconds, section.GetProperty("TimeoutSeconds").GetInt32());
        Assert.Equal("7.0", section.GetProperty("AzureDevOps").GetProperty("ApiVersion").GetString());
        Assert.Equal(Organization, section.GetProperty("AzureDevOps").GetProperty("Organization").GetString());
    }

    [Fact]
    public async Task SaveRemoteSourcesAsync_EmptyOrganization_WritesEmptyString()
    {
        var store = CreateStore();
        await store.SaveRemoteSourcesAsync(new RemoteSourceSettings(Organization), TestSupport.Ct);

        await store.SaveRemoteSourcesAsync(new RemoteSourceSettings(string.Empty), TestSupport.Ct);

        var organization = ReadRoot().GetProperty("RemoteSources").GetProperty("AzureDevOps").GetProperty("Organization");
        Assert.Equal(string.Empty, organization.GetString());
    }

    [Fact]
    public async Task SaveRemoteSourcesAsync_CorruptFile_StartsFreshAndLeavesNoTemporaryFiles()
    {
        WriteRaw("{ not json");

        await CreateStore().SaveRemoteSourcesAsync(new RemoteSourceSettings(Organization), TestSupport.Ct);

        Assert.Equal(Organization, ReadRoot().GetProperty("RemoteSources").GetProperty("AzureDevOps").GetProperty("Organization").GetString());
        Assert.Equal([SettingsPath], Directory.GetFiles(_directory));
    }

    [Fact]
    public void GetRemoteSources_LegacyProfileWithPinnedFingerprint_StillLoads()
    {
        const string Legacy = """{"Host":"h","Port":22,"Username":"u","KeyFilePath":"k","PinnedFingerprint":"SHA256:abc","RemoteRoot":"/r"}""";
        var options = JsonSerializer.Deserialize<SshProfileOptions>(Legacy)!;
        var remote = new RemoteSourceOptions();
        remote.Ssh.Profiles.Add(options);
        var store = new JsonUserSettingsStore(
            MsOptions.Create(new UserSettingsOptions { Path = SettingsPath }),
            new StaticMonitor<GatewayOptions>(new GatewayOptions()),
            new StaticMonitor<CollectorServerOptions>(new CollectorServerOptions()),
            new StaticMonitor<RemoteSourceOptions>(remote),
        new StaticMonitor<AiModelChoiceOptions>(new AiModelChoiceOptions()),
            new StaticMonitor<GeminiRotationOptions>(new GeminiRotationOptions()));

        var profile = Assert.Single(store.GetRemoteSources().SshProfiles);

        Assert.Equal(new SshConnectionProfile("h", 22, "u", "k", "/r"), profile);
    }

    [Fact]
    public async Task SaveRemoteSourcesAsync_LegacyFileWithPinnedFingerprint_DropsTheKeyOnSave()
    {
        WriteRaw("""{"RemoteSources":{"Ssh":{"Profiles":[{"Host":"h","Port":22,"Username":"u","KeyFilePath":"k","PinnedFingerprint":"SHA256:abc","RemoteRoot":"/r"}]}}}""");
        var profile = new SshConnectionProfile("h", 22, "u", "k", "/r");

        await CreateStore().SaveRemoteSourcesAsync(new RemoteSourceSettings(Organization) { SshProfiles = [profile] }, TestSupport.Ct);

        var text = File.ReadAllText(SettingsPath);
        Assert.DoesNotContain("PinnedFingerprint", text, StringComparison.Ordinal);
        var saved = ReadRoot().GetProperty("RemoteSources").GetProperty("Ssh").GetProperty("Profiles")[0];
        Assert.Equal("/r", saved.GetProperty("RemoteRoot").GetString());
    }
}
