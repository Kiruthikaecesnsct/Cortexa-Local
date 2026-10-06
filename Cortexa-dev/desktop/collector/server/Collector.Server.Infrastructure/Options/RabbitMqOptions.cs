namespace Collector.Server.Infrastructure.Options;

public sealed class RabbitMqOptions
{
    public string HostName { get; set; } = string.Empty;

    public int Port { get; set; }

    public string VirtualHost { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string ClientName { get; set; } = string.Empty;
}
