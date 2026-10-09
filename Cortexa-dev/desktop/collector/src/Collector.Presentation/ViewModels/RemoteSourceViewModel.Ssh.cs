using System.IO;
using System.Security.Cryptography;
using System.Text;
using Collector.Application.Remote;
using Collector.Application.Settings;
using Collector.Domain.Enums;
using Collector.Domain.Remote;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Collector.Presentation.ViewModels;

public sealed partial class RemoteSourceViewModel
{
    private const int MinSshPort = 1;
    private const int MaxSshPort = 65535;
    private const int SyntheticShaLength = 7;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FetchTitle))]
    [NotifyCanExecuteChangedFor(nameof(SshConnectCommand))]
    public partial string SshHost { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SshPortError))]
    [NotifyCanExecuteChangedFor(nameof(SshConnectCommand))]
    public partial int SshPort { get; set; } = 22;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FetchTitle))]
    [NotifyCanExecuteChangedFor(nameof(SshConnectCommand))]
    public partial string SshUsername { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SshKeyFileError))]
    [NotifyCanExecuteChangedFor(nameof(SshConnectCommand))]
    public partial string SshKeyFilePath { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SshConnectCommand))]
    public partial string SshFingerprint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SshPassphrase { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SshConnectCommand))]
    public partial bool IsSshConnecting { get; set; }

    public string? SshPortError => SshPort is < MinSshPort or > MaxSshPort ? RemoteSourceStrings.SshInvalidPort : null;

    public string? SshKeyFileError =>
        SshKeyFilePath.Length > 0 && !File.Exists(SshKeyFilePath) ? RemoteSourceStrings.SshKeyFileNotFound : null;

    public string SshFetchTitle => RemoteSourceStrings.SshConnecting;

    private bool IsSshFormValid =>
        SshHost.Trim().Length > 0
        && SshPortError is null
        && SshUsername.Trim().Length > 0
        && SshKeyFilePath.Length > 0
        && SshKeyFileError is null
        && SshFingerprint.Trim().Length > 0;

    private bool CanConnectSsh() => AreControlsEnabled && !IsSshConnecting && IsSshFormValid;

    [RelayCommand]
    private async Task BrowseSshKeyFileAsync(CancellationToken cancellationToken)
    {
        var picked = await _deps.Picker.PickSingleFileAsync(
            RemoteSourceStrings.SshKeyFileDialogTitle,
            RemoteSourceStrings.SshKeyFileDialogFilter,
            cancellationToken);
        if (picked is not null)
        {
            SshKeyFilePath = picked;
        }
    }

    [RelayCommand(CanExecute = nameof(CanFetch), IncludeCancelCommand = true)]
    private async Task SshConnectAsync(CancellationToken cancellationToken)
    {
        var profile = BuildSshProfile();
        IsSshConnecting = true;
        try
        {
            await PersistSshProfileAsync(profile, cancellationToken);
            await RunSshFetchAsync(profile, cancellationToken);
        }
        finally
        {
            IsSshConnecting = false;
        }
    }

    private async Task RunSshFetchAsync(SshConnectionProfile profile, CancellationToken cancellationToken)
    {
        var request = new RemoteFetchRequest(BuildSshRepository(profile), string.Empty);
        BeginFetch(SourceType.Ssh);
        RemoteFetchResult? result = null;
        try
        {
            var progress = new SyncProgress<RemoteFetchProgress>(OnProgress);
            result = await _deps.Fetcher.FetchAsync(request, progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            ShowOutcome(_banners.Canceled());
        }
        catch (RemoteSourceException ex)
        {
            ShowFailure(ex.Kind, RetryDelay(ex.ResetAt), () => SshConnectCommand.ExecuteAsync(null));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected failure connecting to {Host}.", profile.Host);
            ShowFailure(RemoteFailureKind.Upstream, null, () => SshConnectCommand.ExecuteAsync(null));
        }
        finally
        {
            EndFetch();
        }

        if (result is not null)
        {
            CompleteFetch(request, result);
        }
    }

    private SshConnectionProfile BuildSshProfile() => new(
        SshHost.Trim(),
        SshPort,
        SshUsername.Trim(),
        SshKeyFilePath,
        SshFingerprint.Trim(),
        RemoteRoot: string.Empty);

    private async Task PersistSshProfileAsync(SshConnectionProfile profile, CancellationToken cancellationToken)
    {
        var updated = _deps.Settings.GetSshProfiles()
            .Where(existing => !string.Equals(existing.Host, profile.Host, StringComparison.OrdinalIgnoreCase))
            .Append(profile)
            .ToArray();
        await _deps.Settings.SaveSshProfilesAsync(updated, cancellationToken);
        if (SshPassphrase.Length > 0)
        {
            await _deps.Settings.SetSshPassphraseAsync(SshPassphrase, cancellationToken);
        }
    }

    private static RemoteRepository BuildSshRepository(SshConnectionProfile profile) => new(
        SourceType.Ssh,
        profile.Host,
        profile.Username,
        profile.RemoteRoot,
        $"{profile.Username}@{profile.Host}",
        string.Empty,
        string.Empty,
        0,
        false);

    private static RemoteFetchSummary BuildSshSummary(RemoteFetchRequest request, RemoteFetchResult result)
    {
        var commit = SyntheticCommitSha(request.Repository);
        var counts = RemoteSourceStrings.Summary(result.Downloaded, result.CacheHits, result.SkippedByFilter, result.TooLarge);
        return new RemoteFetchSummary(
            request.Repository.FullName,
            $"{commit} · {counts}",
            request.Repository.IsPrivate);
    }

    private static string SyntheticCommitSha(RemoteRepository repository)
    {
        var seed = $"{repository.Owner}/{repository.Project}/{repository.Name}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));
        return hash[..SyntheticShaLength].ToLowerInvariant();
    }
}
