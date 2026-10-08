using System.IO;
using Collector.Application.Remote;
using Collector.Application.Secrets;
using Collector.Application.Settings;
using Collector.Domain.Enums;
using Collector.Domain.Remote;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Collector.Presentation.ViewModels;

public sealed partial class RemoteSourceViewModel
{
    private readonly record struct ListScope(bool IsValid, string? Value);

    partial void OnSearchTextChanged(string value) => RebuildRows();

    partial void OnSelectedRepositoryChanged(RemoteRepositoryRowViewModel? value)
    {
        if (_rebuilding)
        {
            return;
        }

        CancelBranches();
        Banner = null;
        Branches.Clear();
        SelectedBranch = null;
        NotifyFetchState();
        if (value is not null)
        {
            _ = LoadBranchesAsync(value);
        }
    }

    private bool CanLoadRepositories() => IsRemote && AreControlsEnabled && !IsListLoading;

    [RelayCommand(CanExecute = nameof(CanLoadRepositories))]
    private async Task LoadRepositoriesAsync()
    {
        var source = SelectedSource;
        Banner = null;
        var scope = await ResolveScopeAsync(source);
        if (!scope.IsValid)
        {
            return;
        }

        ResetSelection();
        var token = BeginList();
        try
        {
            await ListRepositoriesAsync(source, scope.Value, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (RemoteSourceException ex)
        {
            FailList(source, ex.Kind, ex.ResetAt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not list {Source} repositories.", source);
            FailList(source, RemoteFailureKind.Upstream, null);
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        RequestFocus(RemoteFocusKeys.Search);
    }

    private async Task ListRepositoriesAsync(SourceType source, string? scope, CancellationToken token)
    {
        if (!await HasTokenAsync(source))
        {
            FailList(source, RemoteFailureKind.MissingToken, null);
            return;
        }

        var repositories = await _deps.Clients.For(source).ListRepositoriesAsync(scope, token);
        token.ThrowIfCancellationRequested();
        ApplyRepositories(source, repositories);
    }

    private async Task<bool> HasTokenAsync(SourceType source)
    {
        try
        {
            return await _deps.Settings.HasRemoteTokenAsync(source, CancellationToken.None);
        }
        catch (SecretStoreException ex)
        {
            _logger.LogWarning(ex, "Could not read the {Source} token status.", source);
            return true;
        }
    }

    private async Task<ListScope> ResolveScopeAsync(SourceType source)
    {
        if (source != SourceType.AzureDevops)
        {
            return new ListScope(true, null);
        }

        var organization = AzureOrganizationParser.Parse(OrganizationText);
        OrganizationText = organization;
        OrganizationError = OrganizationProblem(organization) ?? await SaveOrganizationAsync(organization);
        if (OrganizationError is not null)
        {
            RequestFocus(RemoteFocusKeys.Organization);
        }

        return new ListScope(OrganizationError is null, organization);
    }

    private static string? OrganizationProblem(string organization)
    {
        if (organization.Length == 0)
        {
            return RemoteSourceStrings.OrganizationEmpty;
        }

        return RemoteSourceRules.IsValidOrganization(organization) ? null : RemoteSourceRules.OrganizationInvalidReason;
    }

    private async Task<string?> SaveOrganizationAsync(string organization)
    {
        try
        {
            var result = await _deps.Settings.SaveRemoteSourcesAsync(new RemoteSourceSettings(organization), CancellationToken.None);
            return result.IsValid ? null : result.Reason;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save the Azure DevOps organization.");
            return null;
        }
    }

    private CancellationToken BeginList()
    {
        CancelList();
        _listCts = new CancellationTokenSource();
        ListState = RemoteListState.Loading;
        _all = [];
        RebuildRows();
        return _listCts.Token;
    }

    private void ApplyRepositories(SourceType source, IReadOnlyList<RemoteRepository> repositories)
    {
        _loaded[source] = repositories;
        if (source != SelectedSource)
        {
            return;
        }

        _all = repositories;
        ListState = RemoteListState.Ready;
        SearchText = string.Empty;
        RebuildRows();
    }

    private void FailList(SourceType source, RemoteFailureKind kind, DateTimeOffset? resetAt)
    {
        if (source != SelectedSource)
        {
            return;
        }

        _loaded.Remove(source);
        _all = [];
        ListState = RemoteListState.Idle;
        RebuildRows();
        if (source == SourceType.AzureDevops && kind == RemoteFailureKind.NotFound)
        {
            OrganizationError = RemoteSourceStrings.OrganizationNotFound(OrganizationText);
            RequestFocus(RemoteFocusKeys.Organization);
            return;
        }

        var retryAfter = resetAt is { } at ? at - _time.GetUtcNow() : (TimeSpan?)null;
        ShowFailure(kind, retryAfter, () => LoadRepositoriesCommand.ExecuteAsync(null));
    }

    private void RebuildRows()
    {
        var keep = SelectedRepository;
        _rebuilding = true;
        try
        {
            Repositories.Clear();
            foreach (var repository in _all.Where(MatchesSearch))
            {
                Repositories.Add(new RemoteRepositoryRowViewModel(repository, _limitBytes));
            }

            SelectedRepository = Repositories.FirstOrDefault(row => keep is not null && row.Repository == keep.Repository);
        }
        finally
        {
            _rebuilding = false;
        }

        SyncSelectionAfterRebuild(keep);
        NotifyFetchState();
    }

    private void SyncSelectionAfterRebuild(RemoteRepositoryRowViewModel? previous)
    {
        if (previous is not null && SelectedRepository is null)
        {
            CancelBranches();
            Branches.Clear();
            SelectedBranch = null;
        }
    }

    private bool MatchesSearch(RemoteRepository repository) =>
        string.IsNullOrWhiteSpace(SearchText)
        || repository.Name.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase);

    private void ResetSelection()
    {
        CancelBranches();
        SelectedRepository = null;
        Branches.Clear();
        SelectedBranch = null;
    }

    private void CancelList()
    {
        _listCts?.Cancel();
        _listCts = null;
    }

    private void CancelBranches()
    {
        _branchCts?.Cancel();
        _branchCts = null;
        IsLoadingBranches = false;
    }

    private async Task LoadBranchesAsync(RemoteRepositoryRowViewModel row)
    {
        var cts = new CancellationTokenSource();
        _branchCts = cts;
        IsLoadingBranches = true;
        IReadOnlyList<RemoteBranch> remote = [];
        RemoteFailureKind? failure = null;
        try
        {
            remote = await _deps.Clients.For(row.Repository.Provider).ListBranchesAsync(row.Repository, cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return;
        }
        catch (RemoteSourceException ex)
        {
            failure = ex.Kind;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not list branches for {Repository}.", row.FullName);
            failure = RemoteFailureKind.Upstream;
        }

        if (!cts.IsCancellationRequested)
        {
            FinishBranches(row, remote, failure);
        }
    }

    private void FinishBranches(RemoteRepositoryRowViewModel row, IReadOnlyList<RemoteBranch> remote, RemoteFailureKind? failure)
    {
        IsLoadingBranches = false;
        _branchCts = null;
        ApplyBranches(row, remote);
        if (failure is { } kind)
        {
            ShowFailure(kind, null, () => LoadBranchesAsync(row));
        }
    }

    private void ApplyBranches(RemoteRepositoryRowViewModel row, IReadOnlyList<RemoteBranch> remote)
    {
        var defaultName = row.DefaultBranch;
        var found = remote.FirstOrDefault(branch => branch.Name == defaultName);
        Branches.Clear();
        Branches.Add(new BranchOptionViewModel(defaultName, found?.CommitSha, true));
        foreach (var branch in remote.Where(branch => branch.Name != defaultName))
        {
            Branches.Add(new BranchOptionViewModel(branch.Name, branch.CommitSha, false));
        }

        SelectedBranch = Branches[0];
    }
}
