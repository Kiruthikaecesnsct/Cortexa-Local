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
    IOptionsMonitor<RemoteSourceOptions> remoteSources) : IUserSettingsStore
{
    private const string AzureDevOpsSection = "AzureDevOps";
    private const string OrganizationKey = "Organization";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public EndpointSettings GetEndpoints() =>
        new(gateway.CurrentValue.BaseUrl, collectorServer.CurrentValue.BaseUrl);

    public RemoteSourceSettings GetRemoteSources() =>
        new(remoteSources.CurrentValue.AzureDevOps.Organization);

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
                root[RemoteSourceOptions.SectionName] = section;
            },
            cancellationToken);

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
