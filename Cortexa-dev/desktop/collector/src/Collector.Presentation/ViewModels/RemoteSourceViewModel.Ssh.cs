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
    private const string DefaultSshPort = "22";
    private const int SyntheticShaLength = 7;

    private string _sshPassphrase = string.Empty;

    [ObservableProperty]
    public partial string SshHost { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SshPort { get; set; } = DefaultSshPort;

    [ObservableProperty]
    public partial string SshUsername { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SshKeyFilePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SshFolder { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsPassphraseVisible { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SshConnectCommand))]
    public partial bool IsSshConnecting { get; set; }

    [ObservableProperty]
    public partial string? SshHostError { get; set; }

    [ObservableProperty]
    public partial string? SshPortError { get; set; }

    [ObservableProperty]
    public partial string? SshUsernameError { get; set; }

    [ObservableProperty]
    public partial string? SshKeyFileError { get; set; }

    [ObservableProperty]
    public partial string? SshFolderError { get; set; }

    public string SshPassphrase
    {
        get => _sshPassphrase;
        set => _sshPassphrase = value ?? string.Empty;
    }

    private bool CanConnectSsh() => AreControlsEnabled && !IsSshConnecting;

    partial void OnSshHostChanged(string value) => SshHostError = null;

    partial void OnSshPortChanged(string value) => SshPortError = null;

    partial void OnSshUsernameChanged(string value) => SshUsernameError = null;

    partial void OnSshKeyFilePathChanged(string value) => SshKeyFileError = null;

    partial void OnSshFolderChanged(string value) => SshFolderError = null;

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
        if (!ValidateSshForm())
        {
            return;
        }

        var profile = BuildSshProfile();
        IsSshConnecting = true;
        try
        {
            await _deps.SshConnection.CloseAsync(cancellationToken);
            await PersistSshProfileAsync(profile, cancellationToken);
            StoreSshPassphrase();
            await RunSshFetchAsync(profile, cancellationToken);
        }
        finally
        {
            IsSshConnecting = false;
        }
    }

    private bool ValidateSshForm()
    {
        var errors = RemoteConnectRules.ValidateSshForm(new SshFormInput(SshHost, SshPort, SshUsername, SshKeyFilePath, SshFolder));
        SshHostError = errors.HostError;
        SshPortError = errors.PortError;
        SshUsernameError = errors.UsernameError;
        SshKeyFileError = errors.KeyFileError ?? MissingKeyFileError();
        SshFolderError = errors.FolderError;
        return errors.IsValid && SshKeyFileError is null;
    }

    private string? MissingKeyFileError() =>
        File.Exists(SshKeyFilePath.Trim()) ? null : RemoteSourceStrings.SshKeyFileNotFound;

    private void StoreSshPassphrase()
    {
        if (SshPassphrase.Length > 0)
        {
            _deps.Credentials.SetSshPassphrase(SshPassphrase);
            return;
        }

        _deps.Credentials.Clear(SourceType.Ssh);
    }

    private async Task RunSshFetchAsync(SshConnectionProfile profile, CancellationToken cancellationToken)
    {
        var request = new RemoteFetchRequest(BuildSshRepository(profile), string.Empty);
        var result = await ExecuteFetchAsync(request, () => SshConnectCommand.ExecuteAsync(null), cancellationToken);
        if (result is not null)
        {
            CompleteFetch(request, result);
        }
    }

    private SshConnectionProfile BuildSshProfile()
    {
        RemoteConnectRules.TryParsePort(SshPort, out var port);
        return new SshConnectionProfile(SshHost.Trim(), port, SshUsername.Trim(), SshKeyFilePath.Trim(), SshFolder.Trim());
    }

    private async Task PersistSshProfileAsync(SshConnectionProfile profile, CancellationToken cancellationToken)
    {
        var updated = _deps.Settings.GetSshProfiles()
            .Where(existing => !IsSameConnection(existing, profile))
            .Append(profile)
            .ToArray();
        await _deps.Settings.SaveSshProfilesAsync(updated, cancellationToken);
    }

    private static bool IsSameConnection(SshConnectionProfile existing, SshConnectionProfile profile) =>
        string.Equals(existing.Host, profile.Host, StringComparison.OrdinalIgnoreCase)
        && string.Equals(existing.Username, profile.Username, StringComparison.Ordinal)
        && string.Equals(existing.RemoteRoot, profile.RemoteRoot, StringComparison.Ordinal);

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
