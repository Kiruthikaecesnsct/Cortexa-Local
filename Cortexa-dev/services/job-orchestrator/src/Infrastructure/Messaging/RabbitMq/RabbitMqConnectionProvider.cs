using Cortexa.JobOrchestrator.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Cortexa.JobOrchestrator.Infrastructure.Messaging.RabbitMq;

public sealed class RabbitMqConnectionProvider : IAsyncDisposable
{
    private readonly ConnectionFactory _factory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public RabbitMqConnectionProvider(IOptions<RabbitMqSettings> settings)
    {
        var value = settings.Value;
        if (string.IsNullOrWhiteSpace(value.Uri))
            throw new InvalidOperationException("RabbitMq:Uri is required when Messaging:Backend is rabbitmq.");

        _factory = new ConnectionFactory
        {
            Uri = new Uri(value.Uri),
            ClientProvidedName = value.ClientName,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true
        };
        PrefetchCount = value.PrefetchCount;
    }

    public ushort PrefetchCount { get; }

    // Publisher confirmations are tracked, so BasicPublishAsync completes only once the broker
    // accepts the message and throws when a mandatory message cannot be routed.
    public async Task<IChannel> CreateChannelAsync(CancellationToken ct)
    {
        var connection = await GetConnectionAsync(ct);
        var options = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);
        return await connection.CreateChannelAsync(options, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();

        _gate.Dispose();
    }

    private async Task<IConnection> GetConnectionAsync(CancellationToken ct)
    {
        if (_connection is not null)
            return _connection;

        await _gate.WaitAsync(ct);
        try
        {
            return _connection ??= await _factory.CreateConnectionAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }
}
