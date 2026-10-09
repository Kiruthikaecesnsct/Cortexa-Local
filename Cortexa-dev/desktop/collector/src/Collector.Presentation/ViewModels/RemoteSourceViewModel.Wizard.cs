using System.IO;
using Collector.Application.Remote;
using Collector.Application.Settings;
using Collector.Domain.Enums;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Collector.Presentation.ViewModels;

public sealed partial class RemoteSourceViewModel
{
    private const string AzureDevOpsOrganizationUrl = "https://dev.azure.com/";

    private string _token = string.Empty;
    private string _connectedOrganization = string.Empty;

    public event EventHandler? CredentialsCleared;

    public IReadOnlyList<WizardStepItemViewModel> Steps { get; } =
    [
        new(RemoteWizardStep.Connect, RemoteSourceStrings.StepConnect),
        new(RemoteWizardStep.Repository, RemoteSourceStrings.StepRepository),
        new(RemoteWizardStep.Branch, RemoteSourceStrings.StepBranch),
        new(RemoteWizardStep.Files, RemoteSourceStrings.StepFiles),
        new(RemoteWizardStep.Proceed, RemoteSourceStrings.StepProceed),
    ];

    public string Token
    {
        get => _token;
        set
        {
            _token = value ?? string.Empty;
            TokenError = null;
        }
    }

    public bool IsTokenSource => SelectedSource is SourceType.Github or SourceType.AzureDevops;

    public bool IsConnected => Status == ConnectionStatus.Connected;

    public bool IsConnecting => Status == ConnectionStatus.Connecting;

    public bool ShowConnectStep => IsTokenSource && Step == RemoteWizardStep.Connect;

    public bool ShowRepositoryStep => IsTokenSource && Step == RemoteWizardStep.Repository;

    public bool ShowBranchStep => IsTokenSource && Step == RemoteWizardStep.Branch;

    public bool ShowFilesStep => IsTokenSource && Step == RemoteWizardStep.Files;

    public bool ShowProceedStep => IsTokenSource && Step == RemoteWizardStep.Proceed;

    public bool ShowChangeRepository =>
        IsTokenSource && Step is RemoteWizardStep.Branch or RemoteWizardStep.Files or RemoteWizardStep.Proceed;

    public bool ShowChangeBranch => IsTokenSource && Step is RemoteWizardStep.Files or RemoteWizardStep.Proceed;

    public bool ShowChangeFiles => IsTokenSource && Step == RemoteWizardStep.Proceed;

    public string StatusText => Status switch
    {
        ConnectionStatus.Connected => RemoteSourceStrings.ConnectedTo(_connectedOrganization),
        ConnectionStatus.Connecting => RemoteSourceStrings.StatusConnecting,
        _ => RemoteSourceStrings.StatusNotConnected,
    };

    public string StepPrompt => RemoteSourceStrings.StepPrompt((int)Step, StepPromptText);

    public string OrgUrlPlaceholder => RemoteConnectRules.UrlExample(SelectedSource);

    public string OrgUrlHint => IsAzure ? RemoteSourceStrings.AzureDevOpsOrganizationHint : RemoteSourceStrings.GitHubOrganizationHint;

    public string TokenHint => IsAzure ? RemoteSourceStrings.AzureDevOpsTokenHint : RemoteSourceStrings.GitHubTokenHint;

    public string TokenPlaceholder => IsAzure ? RemoteSourceStrings.AzureDevOpsTokenPlaceholder : RemoteSourceStrings.GitHubTokenPlaceholder;

    public string TokenLinkText => IsAzure ? RemoteSourceStrings.AzureDevOpsTokenLink : RemoteSourceStrings.GitHubTokenLink;

    private string StepPromptText => Step switch
    {
        RemoteWizardStep.Connect => RemoteSourceStrings.ConnectPromptText,
        RemoteWizardStep.Repository => RemoteSourceStrings.RepositoryPromptText(_all.Count, _connectedOrganization),
        RemoteWizardStep.Branch => RemoteSourceStrings.BranchPromptText(SelectedRepository?.FullName ?? string.Empty, _allBranches.Count),
        RemoteWizardStep.Files => RemoteSourceStrings.FilesPromptText,
        _ => Summary is null ? RemoteSourceStrings.ProceedPromptText : RemoteSourceStrings.ProceedDonePromptText,
    };

    [ObservableProperty]
    public partial RemoteWizardStep Step { get; set; } = RemoteWizardStep.Connect;

    [ObservableProperty]
    public partial ConnectionStatus Status { get; set; }

    [ObservableProperty]
    public partial string OrgUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? OrgUrlError { get; set; }

    [ObservableProperty]
    public partial string? TokenError { get; set; }

    [ObservableProperty]
    public partial bool IsTokenVisible { get; set; }

    partial void OnOrgUrlChanged(string value) => OrgUrlError = null;

    partial void OnStatusChanged(ConnectionStatus value) => NotifyFetchState();

    partial void OnStepChanged(RemoteWizardStep value)
    {
        SyncSteps();
        NotifyFetchState();
    }

    private void SyncSteps()
    {
        foreach (var item in Steps)
        {
            item.Sync(Step);
        }
    }

    private bool CanConnect() => IsTokenSource && AreControlsEnabled && !IsConnecting && !IsListLoading;

    private bool CanDisconnect() => IsTokenSource && AreControlsEnabled && Status != ConnectionStatus.NotConnected;

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var form = RemoteConnectRules.ValidateTokenForm(SelectedSource, OrgUrl, Token);
        OrgUrlError = form.UrlError;
        TokenError = form.TokenError;
        if (!form.IsValid)
        {
            RequestFocus(form.UrlError is null ? RemoteFocusKeys.Token : RemoteFocusKeys.Organization);
            return;
        }

        _deps.Credentials.SetToken(SelectedSource, form.Token);
        _connectedOrganization = form.Organization!.Organization;
        await PersistAzureOrganizationAsync(cancellationToken);
        await RunListAsync();
    }

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private void Disconnect() => DisconnectCore();

    [RelayCommand]
    private void OpenTokenPage()
    {
        var url = IsAzure ? RemoteSourceStrings.AzureDevOpsTokenPageUrl : RemoteSourceStrings.GitHubTokenPageUrl;
        if (!_deps.Links.TryOpen(new Uri(url)))
        {
            _logger.LogWarning("Could not open the {Source} token page.", SelectedSource);
        }
    }

    [RelayCommand(CanExecute = nameof(AreControlsEnabled))]
    private void ChangeRepository()
    {
        Summary = null;
        Banner = null;
        if (IsTokenSource && IsConnected)
        {
            ResetSelection();
            Step = RemoteWizardStep.Repository;
        }

        RequestFocus(RemoteFocusKeys.Repositories);
    }

    [RelayCommand(CanExecute = nameof(AreControlsEnabled))]
    private void ChangeBranch()
    {
        Summary = null;
        Banner = null;
        Step = RemoteWizardStep.Branch;
        SelectedBranch = null;
        ReleaseTree();
    }

    private void ChangeToken()
    {
        DisconnectCore();
        RequestFocus(RemoteFocusKeys.Token);
    }

    private void DisconnectCore()
    {
        _deps.Credentials.Clear(SelectedSource);
        _loaded.Remove(SelectedSource);
        CancelList();
        CancelBranches();
        Banner = null;
        Summary = null;
        var url = OrgUrl;
        ResetWizardState();
        _all = [];
        ListState = RemoteListState.Idle;
        RebuildRows();
        OrgUrl = url;
    }

    private void ResetWizardState()
    {
        ResetSelection();
        _connectedOrganization = string.Empty;
        _token = string.Empty;
        TokenError = null;
        IsTokenVisible = false;
        SshPassphrase = string.Empty;
        IsPassphraseVisible = false;
        SearchText = string.Empty;
        VisibilityFilter = RepositoryVisibility.All;
        SortOrder = RepositorySort.Name;
        BranchSearchText = string.Empty;
        Status = ConnectionStatus.NotConnected;
        Step = RemoteWizardStep.Connect;
        OrgUrl = DefaultOrgUrl(SelectedSource);
        OrgUrlError = null;
        CredentialsCleared?.Invoke(this, EventArgs.Empty);
    }

    private string DefaultOrgUrl(SourceType source)
    {
        var organization = _deps.Settings.GetRemoteSources().AzureDevOpsOrganization;
        return source == SourceType.AzureDevops && organization.Length > 0
            ? AzureDevOpsOrganizationUrl + organization
            : string.Empty;
    }

    private async Task PersistAzureOrganizationAsync(CancellationToken cancellationToken)
    {
        if (!IsAzure)
        {
            return;
        }

        try
        {
            await _deps.Settings.SaveRemoteSourcesAsync(
                new RemoteSourceSettings(_connectedOrganization),
                cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save the Azure DevOps organization.");
        }
    }

    private void NotifyWizard()
    {
        OnPropertyChanged(nameof(IsTokenSource));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(ShowConnectStep));
        OnPropertyChanged(nameof(ShowRepositoryStep));
        OnPropertyChanged(nameof(ShowBranchStep));
        OnPropertyChanged(nameof(ShowFilesStep));
        OnPropertyChanged(nameof(ShowProceedStep));
        OnPropertyChanged(nameof(ShowChangeRepository));
        OnPropertyChanged(nameof(ShowChangeBranch));
        OnPropertyChanged(nameof(ShowChangeFiles));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StepPrompt));
        OnPropertyChanged(nameof(OrgUrlPlaceholder));
        OnPropertyChanged(nameof(OrgUrlHint));
        OnPropertyChanged(nameof(TokenHint));
        OnPropertyChanged(nameof(TokenPlaceholder));
        OnPropertyChanged(nameof(TokenLinkText));
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
        ChangeBranchCommand.NotifyCanExecuteChanged();
        ChangeFilesCommand.NotifyCanExecuteChanged();
        ProceedCommand.NotifyCanExecuteChanged();
    }
}
