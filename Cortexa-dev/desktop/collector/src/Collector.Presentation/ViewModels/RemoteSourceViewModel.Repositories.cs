using Collector.Application.Remote;
using Collector.Domain.Enums;
using Collector.Domain.Remote;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Collector.Presentation.ViewModels;

public sealed partial class RemoteSourceViewModel
{
    private IReadOnlyList<RemoteBranch> _allBranches = [];

    public IReadOnlyList<SegmentOptionViewModel<RepositoryVisibility>> VisibilityOptions { get; private set; } = [];

    public IReadOnlyList<SegmentOptionViewModel<RepositorySort>> SortOptions { get; private set; } = [];

    public bool ShowNoBranchMatch => Branches.Count == 0 && _allBranches.Count > 0 && !IsLoadingBranches;

    [ObservableProperty]
    public partial RepositoryVisibility VisibilityFilter { get; set; }

    [ObservableProperty]
    public partial RepositorySort SortOrder { get; set; }

    [ObservableProperty]
    public partial string BranchSearchText { get; set; } = string.Empty;

    partial void OnSearchTextChanged(string value) => RebuildRows();

    partial void OnVisibilityFilterChanged(RepositoryVisibility value)
    {
        SyncOptions();
        RebuildRows();
    }

    partial void OnSortOrderChanged(RepositorySort value)
    {
        SyncOptions();
        RebuildRows();
    }

    partial void OnBranchSearchTextChanged(string value) => RebuildBranches();

    partial void OnSelectedRepositoryChanged(RemoteRepositoryRowViewModel? value)
    {
        if (_rebuilding)
        {
            return;
        }

        CancelBranches();
        Banner = null;
        ClearBranches();
        NotifyFetchState();
        if (value is null)
        {
            return;
        }

        if (value.Repository.Provider == SourceType.CortexaRepo)
        {
            ApplySavedBranch(value);
            return;
        }

        Step = RemoteWizardStep.Branch;
        _ = LoadBranchesAsync(value);
    }

    private bool CanLoadRepositories() => IsRemote && !IsSsh && AreControlsEnabled && !IsListLoading && (IsCortexa || IsConnected);

    [RelayCommand(CanExecute = nameof(CanLoadRepositories))]
    private Task LoadRepositoriesAsync() => RunListAsync();

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        RequestFocus(RemoteFocusKeys.Search);
    }

    private void InitializeOptions()
    {
        VisibilityOptions =
        [
            new(RepositoryVisibility.All, RemoteSourceStrings.VisibilityAll, value => VisibilityFilter = value),
            new(RepositoryVisibility.Public, RemoteSourceStrings.VisibilityPublic, value => VisibilityFilter = value),
            new(RepositoryVisibility.Private, RemoteSourceStrings.VisibilityPrivate, value => VisibilityFilter = value),
        ];
        SortOptions =
        [
            new(RepositorySort.Name, RemoteSourceStrings.SortName, value => SortOrder = value),
            new(RepositorySort.RecentlyUpdated, RemoteSourceStrings.SortRecentlyUpdated, value => SortOrder = value),
        ];
        SyncOptions();
    }

    private void SyncOptions()
    {
        foreach (var option in VisibilityOptions)
        {
            option.Sync(VisibilityFilter);
        }

        foreach (var option in SortOptions)
        {
            option.Sync(SortOrder);
        }
    }

    private async Task RunListAsync()
    {
        var source = SelectedSource;
        Banner = null;
        ResetSelection();
        var token = BeginList();
        try
        {
            await ListRepositoriesAsync(source, ScopeFor(source), token);
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

    private string? ScopeFor(SourceType source) =>
        source is SourceType.Github or SourceType.AzureDevops ? _connectedOrganization : null;

    private async Task ListRepositoriesAsync(SourceType source, string? scope, CancellationToken token)
    {
        var repositories = await _deps.Clients.For(source).ListRepositoriesAsync(scope, token);
        token.ThrowIfCancellationRequested();
        ApplyRepositories(source, repositories);
    }

    private CancellationToken BeginList()
    {
        CancelList();
        _listCts = new CancellationTokenSource();
        ListState = RemoteListState.Loading;
        if (IsTokenSource)
        {
            Status = ConnectionStatus.Connecting;
        }

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
        if (IsTokenSource)
        {
            Status = ConnectionStatus.Connected;
            Step = RemoteWizardStep.Repository;
        }
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
        if (IsTokenSource)
        {
            ReturnToConnect(kind);
        }

        if (IsTokenSource && kind == RemoteFailureKind.NotFound)
        {
            OrgUrlError = RemoteSourceStrings.OrganizationNotFound(_connectedOrganization);
            RequestFocus(RemoteFocusKeys.Organization);
            return;
        }

        var retryAfter = resetAt is { } at ? at - _time.GetUtcNow() : (TimeSpan?)null;
        ShowFailure(kind, retryAfter, RunListAsync);
    }

    private void ReturnToConnect(RemoteFailureKind kind)
    {
        if (kind == RemoteFailureKind.Auth)
        {
            _deps.Credentials.Clear(SelectedSource);
        }

        Status = ConnectionStatus.NotConnected;
        Step = RemoteWizardStep.Connect;
    }

    private void RebuildRows()
    {
        var keep = SelectedRepository;
        var query = new RepositoryQuery(SearchText, VisibilityFilter, SortOrder, IsCortexa);
        _rebuilding = true;
        try
        {
            Repositories.Clear();
            foreach (var repository in RepositoryFilter.Apply(_all, query))
            {
                Repositories.Add(new RemoteRepositoryRowViewModel(repository, _limitBytes, _time));
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
            ClearBranches();
        }
    }

    private void ResetSelection()
    {
        CancelBranches();
        SelectedRepository = null;
        ClearBranches();
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

    private void ClearBranches()
    {
        _allBranches = [];
        BranchSearchText = string.Empty;
        Branches.Clear();
        SelectedBranch = null;
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

    private void ApplySavedBranch(RemoteRepositoryRowViewModel row)
    {
        Branches.Clear();
        Branches.Add(new BranchOptionViewModel(row.DefaultBranch, null, true));
        SelectedBranch = Branches[0];
    }

    private void ApplyBranches(RemoteRepositoryRowViewModel row, IReadOnlyList<RemoteBranch> remote)
    {
        var defaultName = row.DefaultBranch;
        var hasDefault = remote.Any(branch => branch.Name == defaultName);
        _allBranches = hasDefault ? remote : [new RemoteBranch(defaultName, string.Empty), .. remote];
        RebuildBranches();
        OnPropertyChanged(nameof(ShowNoBranchMatch));
        NotifyFetchState();
    }

    private void RebuildBranches()
    {
        var keep = SelectedBranch;
        var defaultName = SelectedRepository?.DefaultBranch ?? string.Empty;
        _rebuildingBranches = true;
        try
        {
            Branches.Clear();
            foreach (var branch in BranchOrdering.Order(_allBranches, defaultName, BranchSearchText))
            {
                Branches.Add(new BranchOptionViewModel(branch.Name, branch.CommitSha, branch.Name == defaultName, branch.IsProtected));
            }

            SelectedBranch = Branches.FirstOrDefault(option => keep is not null && option.Name == keep.Name);
        }
        finally
        {
            _rebuildingBranches = false;
        }

        OnPropertyChanged(nameof(ShowNoBranchMatch));
    }
}
