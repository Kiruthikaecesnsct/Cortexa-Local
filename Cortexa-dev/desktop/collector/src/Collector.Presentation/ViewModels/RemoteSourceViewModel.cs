using System.Collections.ObjectModel;
using Collector.Application.Ports;
using Collector.Application.Remote;
using Collector.Application.Settings;
using Collector.Domain.Enums;
using Collector.Domain.Remote;
using Collector.Presentation.Resources;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Collector.Presentation.ViewModels;

public sealed record RemoteSourceDependencies(
    IRemoteRepositoryClients Clients,
    IRemoteFetcher Fetcher,
    IRateLimitMonitor RateLimits,
    SettingsService Settings,
    ISessionCredentials Credentials,
    ISshConnectionCloser SshConnection,
    IExternalLinkLauncher Links,
    IFilePicker Picker,
    IOptions<RemoteFetchOptions> FetchOptions);

public sealed record RemoteFilesFetchedEventArgs(IReadOnlyList<string> Paths, SourceType Source, string Origin);

public sealed record RemoteFetchSummary(string Repository, string Detail, bool IsPrivate)
{
    public string AutomationName => $"{Repository}. {Detail}";
}

public enum RemoteListState
{
    Idle,
    Loading,
    Ready,
}

public static class RemoteFocusKeys
{
    public const string Cancel = "RemoteCancel";
    public const string Organization = "RemoteOrganization";
    public const string Token = "RemoteToken";
    public const string Search = "RemoteSearch";
    public const string Repositories = "RemoteRepositories";
}

public sealed partial class RemoteSourceViewModel : FocusableViewModel, IDisposable
{
    private readonly RemoteSourceDependencies _deps;
    private readonly TimeProvider _time;
    private readonly ILogger<RemoteSourceViewModel> _logger;
    private readonly RemoteBannerFactory _banners;
    private readonly RateLimitCountdown _countdown;
    private readonly long _limitBytes;
    private readonly string _limitText;
    private readonly Dictionary<SourceType, IReadOnlyList<RemoteRepository>> _loaded = [];
    private IReadOnlyList<RemoteRepository> _all = [];
    private CancellationTokenSource? _listCts;
    private CancellationTokenSource? _branchCts;
    private Func<Task>? _retry;
    private bool _rebuilding;
    private bool _rebuildingBranches;
    private bool _isLocked;
    private bool _cortexaBlocked;

    public RemoteSourceViewModel(
        RemoteSourceDependencies dependencies,
        TimeProvider time,
        ILogger<RemoteSourceViewModel> logger)
    {
        _deps = dependencies;
        _time = time;
        _logger = logger;
        _limitBytes = dependencies.FetchOptions.Value.MaxRepositoryBytes;
        _limitText = RemoteSizeFormatter.Format(_limitBytes);
        _countdown = new RateLimitCountdown(time);
        _banners = new RemoteBannerFactory(CreateActions());
        Sources = CreateSources();
        Repositories = [];
        Branches = [];
        InitializeOptions();
        SyncSteps();
        OrgUrl = DefaultOrgUrl(SelectedSource);
        dependencies.RateLimits.StatusChanged += OnRateStatusChanged;
        SyncChips();
    }

    public event EventHandler<RemoteFilesFetchedEventArgs>? FilesFetched;

    public IReadOnlyList<SourceChipViewModel> Sources { get; }

    public ObservableCollection<RemoteRepositoryRowViewModel> Repositories { get; }

    public ObservableCollection<BranchOptionViewModel> Branches { get; }

    public bool IsLocal => SelectedSource == SourceType.Local;

    public bool IsRemote => !IsLocal;

    public bool IsAzure => SelectedSource == SourceType.AzureDevops;

    public bool IsGitHub => SelectedSource == SourceType.Github;

    public bool IsSsh => SelectedSource == SourceType.Ssh;

    public bool IsCortexa => SelectedSource == SourceType.CortexaRepo;

    public bool AreControlsEnabled => !IsFetching && !_isLocked;

    public bool ShowForm => Summary is null && !_cortexaBlocked && (IsTokenSource || IsSsh || ListState != RemoteListState.Idle);

    public bool HasSummary => Summary is not null;

    public bool IsListLoading => ListState == RemoteListState.Loading;

    public bool HasRepositories => ListState == RemoteListState.Ready && _all.Count > 0;

    public bool ShowEmptyList => ListState == RemoteListState.Ready && _all.Count == 0;

    public bool ShowNoMatch => HasRepositories && Repositories.Count == 0;

    public bool ShowRepositoryList => HasRepositories && Repositories.Count > 0;

    public string ShowingText => RemoteSourceStrings.Showing(Repositories.Count, _all.Count);

    public string NoMatchText => RemoteSourceStrings.NoMatch(SearchText);

    public string EmptyText => SelectedSource switch
    {
        SourceType.AzureDevops => RemoteSourceStrings.EmptyAzureDevOps(_connectedOrganization),
        SourceType.CortexaRepo => RemoteSourceStrings.EmptyCortexa,
        _ => RemoteSourceStrings.EmptyGitHub,
    };

    public string CortexaBranchName => RemoteSourceStrings.CortexaBranchName(SelectedBranch?.Name ?? string.Empty);

    public string? CortexaBranchText => SelectedBranch?.Name;

    public string FetchHelp => SelectedRepository switch
    {
        null => RemoteSourceStrings.FetchHelp,
        { IsTooBig: true } row => RemoteSourceStrings.TooBigHelp(row.SizeText, _limitText),
        _ => RemoteSourceStrings.FetchNote,
    };

    public bool FetchHelpIsError => SelectedRepository?.IsTooBig == true;

    public string? CommitText => SelectedBranch?.ShortSha is { } sha ? RemoteSourceStrings.Commit(sha) : null;

    public bool HasCommit => CommitText is not null;

    public bool ShowBranchField => SelectedRepository is not null;

    public string FetchTitle => SelectedSource == SourceType.Ssh
        ? SshFetchTitle
        : SelectedRepository is { } row && SelectedBranch is { } branch
            ? RemoteSourceStrings.FetchingTitle(row.FullName, branch.Name)
            : string.Empty;

    [ObservableProperty]
    public partial SourceType SelectedSource { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial RemoteListState ListState { get; set; }

    [ObservableProperty]
    public partial RemoteRepositoryRowViewModel? SelectedRepository { get; set; }

    [ObservableProperty]
    public partial BranchOptionViewModel? SelectedBranch { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingBranches { get; set; }

    [ObservableProperty]
    public partial bool IsFetching { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double ProgressValue { get; set; }

    [ObservableProperty]
    public partial double ProgressMaximum { get; set; } = 1;

    [ObservableProperty]
    public partial bool IsProgressIndeterminate { get; set; } = true;

    [ObservableProperty]
    public partial BannerViewModel? Banner { get; set; }

    [ObservableProperty]
    public partial RemoteFetchSummary? Summary { get; set; }

    public void SetLocked(bool locked)
    {
        _isLocked = locked;
        NotifyFetchState();
    }

    public void Dispose()
    {
        _deps.RateLimits.StatusChanged -= OnRateStatusChanged;
        _countdown.Dispose();
        _listCts?.Cancel();
        _branchCts?.Cancel();
    }

    partial void OnSelectedSourceChanging(SourceType oldValue, SourceType newValue)
    {
        _deps.Credentials.Clear(oldValue);
        if (oldValue == SourceType.Ssh)
        {
            _ = _deps.SshConnection.CloseAsync(CancellationToken.None);
        }

        _loaded.Remove(oldValue);
    }

    partial void OnSelectedSourceChanged(SourceType value)
    {
        CancelList();
        CancelBranches();
        Banner = null;
        Summary = null;
        _cortexaBlocked = false;
        ResetWizardState();
        ResetSelection();
        SyncChips();
        if (value == SourceType.CortexaRepo)
        {
            _loaded.Remove(value);
        }

        _all = _loaded.GetValueOrDefault(value) ?? [];
        ListState = _loaded.ContainsKey(value) ? RemoteListState.Ready : RemoteListState.Idle;
        SearchText = string.Empty;
        RebuildRows();
        NotifyFetchState();
        if (ListState == RemoteListState.Idle && value == SourceType.CortexaRepo)
        {
            _ = LoadRepositoriesCommand.ExecuteAsync(null);
        }
    }

    partial void OnBannerChanged(BannerViewModel? value)
    {
        if (value is null && _cortexaBlocked)
        {
            _cortexaBlocked = false;
            NotifyFetchState();
        }
    }

    partial void OnListStateChanged(RemoteListState value) => NotifyFetchState();

    partial void OnIsFetchingChanged(bool value) => NotifyFetchState();

    partial void OnIsLoadingBranchesChanged(bool value) => NotifyFetchState();

    partial void OnSummaryChanged(RemoteFetchSummary? value) => NotifyFetchState();

    partial void OnSelectedBranchChanged(BranchOptionViewModel? value)
    {
        if (_rebuildingBranches)
        {
            return;
        }

        if (!IsLoadingBranches)
        {
            Banner = null;
        }

        if (value is not null && IsTokenSource && Step == RemoteWizardStep.Branch)
        {
            Step = RemoteWizardStep.Fetch;
        }

        NotifyFetchState();
    }

    private void SyncChips()
    {
        foreach (var chip in Sources)
        {
            chip.Sync(SelectedSource);
        }
    }

    private SourceChipViewModel[] CreateSources() =>
    [
        new(SourceType.Local, RemoteSourceStrings.LocalLabel, RemoteSourceStrings.LocalName, Select),
        new(SourceType.Github, RemoteSourceStrings.GitHubLabel, RemoteSourceStrings.GitHubName, Select),
        new(SourceType.AzureDevops, RemoteSourceStrings.AzureDevOpsLabel, RemoteSourceStrings.AzureDevOpsName, Select),
        new(SourceType.Ssh, RemoteSourceStrings.SshLabel, RemoteSourceStrings.SshName, Select),
        new(SourceType.CortexaRepo, RemoteSourceStrings.CortexaLabel, RemoteSourceStrings.CortexaName, Select),
    ];

    private void Select(SourceType source)
    {
        if (AreControlsEnabled)
        {
            SelectedSource = source;
            return;
        }

        SyncChips();
    }

    private void NotifyFetchState()
    {
        OnPropertyChanged(nameof(IsLocal));
        OnPropertyChanged(nameof(IsRemote));
        OnPropertyChanged(nameof(IsAzure));
        OnPropertyChanged(nameof(IsGitHub));
        OnPropertyChanged(nameof(IsSsh));
        OnPropertyChanged(nameof(IsCortexa));
        OnPropertyChanged(nameof(CortexaBranchName));
        OnPropertyChanged(nameof(CortexaBranchText));
        OnPropertyChanged(nameof(AreControlsEnabled));
        OnPropertyChanged(nameof(ShowForm));
        OnPropertyChanged(nameof(HasSummary));
        OnPropertyChanged(nameof(FetchHelp));
        OnPropertyChanged(nameof(FetchHelpIsError));
        OnPropertyChanged(nameof(CommitText));
        OnPropertyChanged(nameof(HasCommit));
        OnPropertyChanged(nameof(ShowBranchField));
        OnPropertyChanged(nameof(FetchTitle));
        NotifyListState();
        FetchCommand.NotifyCanExecuteChanged();
        FetchAgainCommand.NotifyCanExecuteChanged();
        ChangeRepositoryCommand.NotifyCanExecuteChanged();
        LoadRepositoriesCommand.NotifyCanExecuteChanged();
        SshConnectCommand.NotifyCanExecuteChanged();
        NotifyWizard();
    }

    private void NotifyListState()
    {
        OnPropertyChanged(nameof(IsListLoading));
        OnPropertyChanged(nameof(HasRepositories));
        OnPropertyChanged(nameof(ShowEmptyList));
        OnPropertyChanged(nameof(ShowNoMatch));
        OnPropertyChanged(nameof(ShowRepositoryList));
        OnPropertyChanged(nameof(ShowingText));
        OnPropertyChanged(nameof(NoMatchText));
        OnPropertyChanged(nameof(EmptyText));
    }

    private RemoteBannerActions CreateActions() => new(
        new RelayCommand(ChangeToken),
        new AsyncRelayCommand(RetryAsync),
        new AsyncRelayCommand(RunListAsync),
        new RelayCommand(OpenGitHubTokens),
        new RelayCommand(() => SelectedSource = SourceType.Local),
        new RelayCommand(() => Banner = null));

    private void OpenGitHubTokens()
    {
        if (!_deps.Links.TryOpen(new Uri(RemoteSourceStrings.GitHubTokensUrl)))
        {
            _logger.LogWarning("Could not open the GitHub token settings page.");
        }
    }

    private async Task RetryAsync()
    {
        var retry = _retry;
        Banner = null;
        if (retry is not null)
        {
            await retry();
        }
    }

    private void ShowOutcome(BannerContent content)
    {
        Banner = new BannerViewModel(content);
        RequestFocus(KnowledgeFocusKeys.Banner);
    }

    private void ShowFailure(RemoteFailureKind kind, TimeSpan? retryAfter, Func<Task> retry)
    {
        _retry = retry;
        _cortexaBlocked = SelectedSource == SourceType.CortexaRepo
            && kind is RemoteFailureKind.Auth or RemoteFailureKind.AccessDenied;
        ShowOutcome(_banners.ForFailure(kind, FailureContext(retryAfter)));
        OnPropertyChanged(nameof(ShowForm));
    }

    private RemoteFailureContext FailureContext(TimeSpan? retryAfter)
    {
        var row = SelectedRepository;
        return new RemoteFailureContext(
            SelectedSource,
            row?.FullName ?? string.Empty,
            SelectedBranch?.Name ?? row?.DefaultBranch ?? string.Empty,
            row?.Repository.Owner ?? string.Empty,
            row?.SizeText ?? string.Empty,
            _limitText)
        {
            RetryAfter = retryAfter,
        };
    }
}
