using System.ComponentModel;
using Collector.Application.Remote;
using Collector.Domain.Remote;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Collector.Presentation.ViewModels;

public sealed partial class RemoteSourceViewModel
{
    private CancellationTokenSource? _treeCts;

    public RemoteFileTreeViewModel FileTree { get; }

    public bool AreFileControlsEnabled => !IsLoadingTree && FileTree.IsLoaded && AreControlsEnabled;

    public string LoadingTreeText => RemoteSourceStrings.LoadingTree(SelectedBranch?.Name ?? string.Empty);

    public string ProceedSelectionText => RemoteSelectionText.ProceedText(FileTree.SelectionSummary);

    public string ProceedSkippedText => RemoteSelectionText.SkippedLine(FileTree.SelectionSummary);

    public bool HasProceedSkipped => ProceedSkippedText.Length > 0;

    [ObservableProperty]
    public partial bool IsLoadingTree { get; set; }

    private bool CanProceedToFetch() => AreFileControlsEnabled && FileTree.CanProceed;

    [RelayCommand(CanExecute = nameof(CanProceedToFetch))]
    private async Task ProceedAsync()
    {
        if (!FetchCommand.CanExecute(null))
        {
            return;
        }

        Banner = null;
        Step = RemoteWizardStep.Proceed;
        NotifyProceedSummary();
        await FetchCommand.ExecuteAsync(null);
    }

    [RelayCommand(CanExecute = nameof(AreControlsEnabled))]
    private void ChangeFiles()
    {
        Summary = null;
        Banner = null;
        Step = RemoteWizardStep.Files;
        RequestFocus(RemoteFocusKeys.FileSearch);
    }

    private void OnFileTreeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RemoteFileTreeViewModel.CanProceed) or nameof(RemoteFileTreeViewModel.SummaryText))
        {
            ProceedCommand.NotifyCanExecuteChanged();
            NotifyProceedSummary();
        }
    }

    private void NotifyProceedSummary()
    {
        OnPropertyChanged(nameof(ProceedSelectionText));
        OnPropertyChanged(nameof(ProceedSkippedText));
        OnPropertyChanged(nameof(HasProceedSkipped));
    }

    private void CancelTree()
    {
        _treeCts?.Cancel();
        _treeCts = null;
        IsLoadingTree = false;
    }

    private void ReleaseTree()
    {
        CancelTree();
        FileTree.Unload();
    }

    private async Task LoadTreeAsync(BranchOptionViewModel branch)
    {
        var row = SelectedRepository!;
        CancelTree();
        var cts = new CancellationTokenSource();
        _treeCts = cts;
        IsLoadingTree = true;
        OnPropertyChanged(nameof(LoadingTreeText));
        RemoteTree? tree = null;
        RemoteFailureKind? failure = null;
        try
        {
            tree = await _deps.Clients.For(row.Repository.Provider).GetTreeAsync(row.Repository, branch.Name, cts.Token);
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
            _logger.LogWarning(ex, "Could not read the file tree for {Repository}.", row.FullName);
            failure = RemoteFailureKind.Upstream;
        }

        if (!cts.IsCancellationRequested)
        {
            FinishTree(branch, tree, failure);
        }
    }

    private void FinishTree(BranchOptionViewModel branch, RemoteTree? tree, RemoteFailureKind? failure)
    {
        _treeCts = null;
        IsLoadingTree = false;
        if (tree is not null)
        {
            FileTree.Load(tree);
            RequestFocus(RemoteFocusKeys.FileSearch);
        }

        if (failure is { } kind)
        {
            ShowFailure(kind, null, () => LoadTreeAsync(branch));
        }

        NotifyFetchState();
    }
}
