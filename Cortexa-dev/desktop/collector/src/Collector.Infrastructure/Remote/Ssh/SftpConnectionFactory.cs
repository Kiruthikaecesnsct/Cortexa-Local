using Collector.Application.Ports;
using Collector.Application.Remote;
using Collector.Application.Secrets;
using Collector.Application.Settings;
using Collector.Domain.Enums;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Collector.Infrastructure.Remote.Ssh;

public sealed class SftpConnectionFactory(ISecretStore secrets, ILogger<SftpConnectionFactory> logger)
{
    public async Task<SftpClient> ConnectAsync(
        SshConnectionProfile profile,
        SshSourceOptions options,
        CancellationToken cancellationToken)
    {
        var passphrase = await secrets.ReadAsync(SecretSlot.SshPassphrase, cancellationToken);
        using var keyFile = CreatePrivateKeyFile(profile, passphrase);
        var connectionInfo = CreateConnectionInfo(profile, keyFile, options);
        var client = new SftpClient(connectionInfo);
        client.HostKeyReceived += (_, args) => OnHostKeyReceived(args, profile);

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

    private void OnHostKeyReceived(HostKeyEventArgs args, SshConnectionProfile profile)
    {
        var fingerprint = SshFingerprint.Compute(args.HostKey);
        var trusted = !string.IsNullOrEmpty(profile.PinnedFingerprint)
            && SshFingerprint.Matches(profile.PinnedFingerprint, fingerprint);
        args.CanTrust = trusted;
        if (trusted)
        {
            return;
        }

        logger.LogWarning(
            "Rejected SSH host key for {Host}: fingerprint did not match the pinned value.",
            profile.Host);
        throw new RemoteSourceException(RemoteFailureKind.FingerprintMismatch, SourceType.Ssh);
    }
}
