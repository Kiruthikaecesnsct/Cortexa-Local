using Collector.Server.Infrastructure.Health;
using Collector.Server.Infrastructure.Options;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Collector.Server.Infrastructure.Messaging.RabbitMq;

public sealed class RabbitMqConnectionProvider : IBrokerProbe, IAsyncDisposable
{
    private readonly ConnectionFactory _factory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public RabbitMqConnectionProvider(IOptions<MessagingOptions> options)
    {
        var rabbit = options.Value.RabbitMq;

        _factory = new ConnectionFactory
        {
            HostName = rabbit.HostName,
            Port = rabbit.Port,
            VirtualHost = rabbit.VirtualHost,
            UserName = rabbit.UserName,
            Password = rabbit.Password,
            ClientProvidedName = rabbit.ClientName,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true
        };
    }

    public async Task<IChannel> CreateChannelAsync(CancellationToken cancellationToken)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);
        return await connection.CreateChannelAsync(channelOptions, cancellationToken);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        var connection = await GetConnectionAsync(cancellationToken);
        return connection.IsOpen;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _gate.Dispose();
    }

    private async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is not { IsOpen: true })
            {
                _connection = await _factory.CreateConnectionAsync(cancellationToken);
            }

            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }
}
