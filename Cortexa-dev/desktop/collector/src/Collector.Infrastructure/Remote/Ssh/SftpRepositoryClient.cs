using Collector.Application.Ports;
using Collector.Application.Remote;
using Collector.Application.Settings;
using Collector.Domain.Enums;
using Collector.Domain.Remote;
using Collector.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace Collector.Infrastructure.Remote.Ssh;

public sealed class SftpRepositoryClient(
    SftpConnectionFactory connectionFactory,
    SettingsService settings,
    IOptionsMonitor<RemoteSourceOptions> options,
    ILogger<SftpRepositoryClient> logger) : IRemoteRepositoryClient, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SftpClient? _client;
    private SshConnectionProfile? _connectedProfile;

    public SourceType Provider => SourceType.Ssh;

    public Task<IReadOnlyList<RemoteRepository>> ListRepositoriesAsync(
        string? scope,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("SSH sources do not support repository listing.");

    public Task<IReadOnlyList<RemoteBranch>> ListBranchesAsync(
        RemoteRepository repository,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("SSH sources do not support branch listing.");

    public async Task<RemoteTree> GetTreeAsync(
        RemoteRepository repository,
        string branch,
        CancellationToken cancellationToken)
    {
        var profile = ResolveProfile(repository);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var client = await EnsureConnectedAsync(profile, cancellationToken);
            var entries = new List<RemoteTreeEntry>();
            var walk = new SftpWalk(client, options.CurrentValue.Ssh.MaxWalkDepth, entries, cancellationToken);
            await WalkAsync(walk, profile.RemoteRoot, string.Empty, 0);
            return new RemoteTree(string.Empty, entries, Truncated: false);
        }
        catch (Exception exception) when (IsConnectionFailure(exception))
        {
            throw MapFailure(exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<RemoteBlob> OpenBlobAsync(
        RemoteRepository repository,
        string blobSha,
        CancellationToken cancellationToken)
    {
        var profile = ResolveProfile(repository);
        var key = SshBlobKeyCodec.Parse(blobSha);
        var absolutePath = JoinRemoteDir(profile.RemoteRoot, key.RelativePath);
        var stream = new SshLazyDownloadStream(token => DownloadAsync(profile, absolutePath, token), cancellationToken);
        return Task.FromResult(new RemoteBlob(stream, key.SizeBytes));
    }

    public void Dispose()
    {
        _gate.Wait();
        try
        {
            DisconnectCurrent();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task<Stream> DownloadAsync(SshConnectionProfile profile, string absolutePath, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var client = await EnsureConnectedAsync(profile, cancellationToken);
            var buffer = new MemoryStream();
            await client.DownloadFileAsync(absolutePath, buffer, cancellationToken);
            buffer.Position = 0;
            return buffer;
        }
        catch (Exception exception) when (IsConnectionFailure(exception))
        {
            throw MapFailure(exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SftpClient> EnsureConnectedAsync(SshConnectionProfile profile, CancellationToken cancellationToken)
    {
        if (_client is { IsConnected: true } && SameProfile(_connectedProfile, profile))
        {
            return _client;
        }

        DisconnectCurrent();
        _client = await connectionFactory.ConnectAsync(profile, options.CurrentValue.Ssh, cancellationToken);
        _connectedProfile = profile;
        return _client;
    }

    private void DisconnectCurrent()
    {
        if (_client is null)
        {
            return;
        }

        _client.Dispose();
        _client = null;
        _connectedProfile = null;
    }

    private SshConnectionProfile ResolveProfile(RemoteRepository repository)
    {
        var profile = settings.GetSshProfiles().FirstOrDefault(candidate => MatchesRepository(candidate, repository));
        if (profile is null)
        {
            logger.LogWarning("No SSH profile matches repository {RepoKey}.", repository.RepoKey);
            throw new RemoteSourceException(RemoteFailureKind.NotFound, SourceType.Ssh);
        }

        return profile;
    }

    private static bool MatchesRepository(SshConnectionProfile profile, RemoteRepository repository) =>
        string.Equals(profile.Host, repository.Owner, StringComparison.OrdinalIgnoreCase)
        && string.Equals(profile.Username, repository.Project, StringComparison.Ordinal)
        && string.Equals(profile.RemoteRoot, repository.Name, StringComparison.Ordinal);

    private static bool SameProfile(SshConnectionProfile? left, SshConnectionProfile right) =>
        left is not null
        && string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase)
        && left.Port == right.Port
        && string.Equals(left.Username, right.Username, StringComparison.Ordinal)
        && string.Equals(left.RemoteRoot, right.RemoteRoot, StringComparison.Ordinal);

    private sealed record SftpWalk(
        SftpClient Client,
        int MaxDepth,
        ICollection<RemoteTreeEntry> Entries,
        CancellationToken CancellationToken);

    private static async Task WalkAsync(SftpWalk walk, string absoluteDir, string relativePrefix, int depth)
    {
        if (depth > walk.MaxDepth)
        {
            return;
        }

        await foreach (var file in walk.Client.ListDirectoryAsync(absoluteDir, walk.CancellationToken))
        {
            if (IsSelfOrParent(file.Name) || file.IsSymbolicLink || !SshBlobKeyCodec.CanEncode(file.Name))
            {
                continue;
            }

            if (file.IsDirectory)
            {
                await WalkAsync(
                    walk,
                    JoinRemoteDir(absoluteDir, file.Name),
                    JoinRelative(relativePrefix, file.Name),
                    depth + 1);
            }
            else if (file.IsRegularFile)
            {
                walk.Entries.Add(ToEntry(relativePrefix, file));
            }
        }
    }

    private static bool IsSelfOrParent(string name) => name is "." or "..";

    private static RemoteTreeEntry ToEntry(string relativePrefix, ISftpFile file)
    {
        var relativePath = JoinRelative(relativePrefix, file.Name);
        var blobKey = SshBlobKeyCodec.Encode(relativePath, file.LastWriteTimeUtc.Ticks, file.Length);
        return new RemoteTreeEntry(relativePath, blobKey, file.Length);
    }

    private static string JoinRemoteDir(string parent, string name) => $"{parent.TrimEnd('/')}/{name}";

    private static string JoinRelative(string prefix, string name) => prefix.Length == 0 ? name : $"{prefix}/{name}";

    private static bool IsConnectionFailure(Exception exception) =>
        exception is SshException or System.Net.Sockets.SocketException or IOException;

    private static RemoteSourceException MapFailure(Exception exception) => exception switch
    {
        SshAuthenticationException or SshPassPhraseNullOrEmptyException =>
            new RemoteSourceException(RemoteFailureKind.Auth, SourceType.Ssh, null, exception),
        SftpPathNotFoundException => new RemoteSourceException(RemoteFailureKind.NotFound, SourceType.Ssh, null, exception),
        SftpPermissionDeniedException => new RemoteSourceException(RemoteFailureKind.AccessDenied, SourceType.Ssh, null, exception),
        _ => new RemoteSourceException(RemoteFailureKind.Upstream, SourceType.Ssh, null, exception),
    };
}
