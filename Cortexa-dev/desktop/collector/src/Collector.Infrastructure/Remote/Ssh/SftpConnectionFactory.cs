using Collector.Application.Ports;
using Collector.Application.Settings;
using Collector.Infrastructure.Options;
using Renci.SshNet;

namespace Collector.Infrastructure.Remote.Ssh;

public sealed class SftpConnectionFactory(ISessionCredentials credentials)
{
    public async Task<SftpClient> ConnectAsync(
        SshConnectionProfile profile,
        SshSourceOptions options,
        CancellationToken cancellationToken)
    {
        var passphrase = credentials.GetSshPassphrase();
        using var keyFile = CreatePrivateKeyFile(profile, passphrase);
        var connectionInfo = CreateConnectionInfo(profile, keyFile, options);
        var client = new SftpClient(connectionInfo);
        client.HostKeyReceived += (_, args) => args.CanTrust = true; // accepted risk: any host key is trusted

        try
        {
            await client.ConnectAsync(cancellationToken);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        return client;
    }

    private static PrivateKeyFile CreatePrivateKeyFile(SshConnectionProfile profile, string? passphrase) =>
        string.IsNullOrEmpty(passphrase)
            ? new PrivateKeyFile(profile.KeyFilePath)
            : new PrivateKeyFile(profile.KeyFilePath, passphrase);

    private static ConnectionInfo CreateConnectionInfo(
        SshConnectionProfile profile,
        PrivateKeyFile keyFile,
        SshSourceOptions options)
    {
        var port = profile.Port > 0 ? profile.Port : options.DefaultPort;
        var authentication = new PrivateKeyAuthenticationMethod(profile.Username, [keyFile]);
        var connectionInfo = new ConnectionInfo(profile.Host, port, profile.Username, authentication)
        {
            Timeout = TimeSpan.FromSeconds(options.ConnectTimeoutSeconds),
        };
        return connectionInfo;
    }
}
