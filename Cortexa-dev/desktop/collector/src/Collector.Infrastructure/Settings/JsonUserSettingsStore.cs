using System.Text.Json;
using Collector.Application.Ports;
using Collector.Application.Settings;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Collector.Infrastructure.Settings;

public sealed class JsonUserSettingsStore(
    IOptions<UserSettingsOptions> settingsOptions,
    IOptionsMonitor<GatewayOptions> gateway,
    IOptionsMonitor<CollectorServerOptions> collectorServer) : IUserSettingsStore
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public EndpointSettings GetEndpoints() =>
        new(gateway.CurrentValue.BaseUrl, collectorServer.CurrentValue.BaseUrl);

    public async Task SaveEndpointsAsync(EndpointSettings settings, CancellationToken cancellationToken)
    {
        var path = Environment.ExpandEnvironmentVariables(settingsOptions.Value.Path);
        var document = new Dictionary<string, object>
        {
            [GatewayOptions.SectionName] = new GatewayOptions { BaseUrl = settings.GatewayUrl },
            [CollectorServerOptions.SectionName] = new CollectorServerOptions { BaseUrl = settings.CollectorServerUrl },
        };

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await WriteAtomicallyAsync(path, document, cancellationToken);
        }
        finally
        {
            _writeGate.Release();
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
}
