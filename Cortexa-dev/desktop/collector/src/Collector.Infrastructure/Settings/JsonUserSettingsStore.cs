using System.Text.Json;
using System.Text.Json.Nodes;
using Collector.Application.Ports;
using Collector.Application.Settings;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Settings;

public sealed class JsonUserSettingsStore(
    IOptions<UserSettingsOptions> settingsOptions,
    IOptionsMonitor<GatewayOptions> gateway,
    IOptionsMonitor<CollectorServerOptions> collectorServer,
    IOptionsMonitor<RemoteSourceOptions> remoteSources,
    IOptionsMonitor<AiModelChoiceOptions> aiModelChoice,
    IOptionsMonitor<GeminiRotationOptions> geminiRotation) : IUserSettingsStore
{
    private const string AzureDevOpsSection = "AzureDevOps";
    private const string OrganizationKey = "Organization";
    private const string SshSection = "Ssh";
    private const string ProfilesKey = "Profiles";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public EndpointSettings GetEndpoints() =>
        new(gateway.CurrentValue.BaseUrl, collectorServer.CurrentValue.BaseUrl);

    public RemoteSourceSettings GetRemoteSources() =>
        new(remoteSources.CurrentValue.AzureDevOps.Organization)
        {
            SshProfiles = ToProfiles(remoteSources.CurrentValue.Ssh.Profiles),
        };

    public AiModelChoice? GetAiModelChoice()
    {
        var saved = aiModelChoice.CurrentValue;
        return saved.Provider is { } provider && !string.IsNullOrWhiteSpace(saved.Model)
            ? new AiModelChoice(provider, saved.Model)
            : null;
    }

    public Task SaveAiModelChoiceAsync(AiModelChoice choice, CancellationToken cancellationToken) =>
        UpdateAsync(
            root => root[AiModelChoiceOptions.SectionName] = new JsonObject
            {
                [nameof(AiModelChoiceOptions.Provider)] = choice.Provider.ToString(),
                [nameof(AiModelChoiceOptions.Model)] = choice.Model,
            },
            cancellationToken);

    public string? GetGeminiActiveKeyId() => geminiRotation.CurrentValue.ActiveKeyId;

    public Task SaveGeminiActiveKeyIdAsync(string? keyId, CancellationToken cancellationToken) =>
        UpdateAsync(
            root => root[GeminiRotationOptions.SectionName] = new JsonObject
            {
                [nameof(GeminiRotationOptions.ActiveKeyId)] = keyId,
            },
            cancellationToken);

    public Task SaveEndpointsAsync(EndpointSettings settings, CancellationToken cancellationToken) =>
        UpdateAsync(
            root =>
            {
                root[GatewayOptions.SectionName] =
                    JsonSerializer.SerializeToNode(new GatewayOptions { BaseUrl = settings.GatewayUrl });
                root[CollectorServerOptions.SectionName] =
                    JsonSerializer.SerializeToNode(new CollectorServerOptions { BaseUrl = settings.CollectorServerUrl });
            },
            cancellationToken);

    public Task SaveRemoteSourcesAsync(RemoteSourceSettings settings, CancellationToken cancellationToken) =>
        UpdateAsync(
            root =>
            {
                var section = root[RemoteSourceOptions.SectionName] as JsonObject ?? [];
                var azure = section[AzureDevOpsSection] as JsonObject ?? [];
                azure[OrganizationKey] = settings.AzureDevOpsOrganization;
                section[AzureDevOpsSection] = azure;

                var ssh = section[SshSection] as JsonObject ?? [];
                ssh[ProfilesKey] = SerializeProfiles(settings.SshProfiles);
                section[SshSection] = ssh;

                root[RemoteSourceOptions.SectionName] = section;
            },
            cancellationToken);

    private static IReadOnlyList<SshConnectionProfile> ToProfiles(List<SshProfileOptions> source) =>
        source.Count == 0 ? Array.Empty<SshConnectionProfile>() : source.Select(ToProfile).ToArray();

    private static SshConnectionProfile ToProfile(SshProfileOptions options) =>
        new(options.Host, options.Port, options.Username, options.KeyFilePath, options.RemoteRoot);

    private static JsonArray SerializeProfiles(IReadOnlyList<SshConnectionProfile> profiles)
    {
        var array = new JsonArray();
        foreach (var profile in profiles)
        {
            array.Add(new JsonObject
            {
                [nameof(SshProfileOptions.Host)] = profile.Host,
                [nameof(SshProfileOptions.Port)] = profile.Port,
                [nameof(SshProfileOptions.Username)] = profile.Username,
                [nameof(SshProfileOptions.KeyFilePath)] = profile.KeyFilePath,
                [nameof(SshProfileOptions.RemoteRoot)] = profile.RemoteRoot,
            });
        }

        return array;
    }

    private static JsonObject ReadRoot(string path)
    {
        try
        {
            return File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? [] : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static async Task WriteAtomicallyAsync(string path, object document, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temp))
            {
                await JsonSerializer.SerializeAsync(stream, document, WriteOptions, cancellationToken);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }

    private async Task UpdateAsync(Action<JsonObject> apply, CancellationToken cancellationToken)
    {
        var path = Environment.ExpandEnvironmentVariables(settingsOptions.Value.Path);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var root = ReadRoot(path);
            apply(root);
            await WriteAtomicallyAsync(path, root, cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }
}
