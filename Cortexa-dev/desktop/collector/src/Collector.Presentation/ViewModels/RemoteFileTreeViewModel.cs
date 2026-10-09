using Collector.Application.Remote;
using Collector.Application.Remote.Selection;
using Collector.Domain.Remote;
using Collector.Presentation.Behaviors;
using Collector.Presentation.Resources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collector.Presentation.ViewModels;

public sealed partial class RemoteFileTreeViewModel : ObservableObject, IDisposable
{
    private readonly FileTreeBuilder _builder;
    private readonly int _maxSelectedFiles;
    private readonly TimeProvider _time;
    private readonly Dictionary<FileTreeNode, FileTreeRowViewModel> _rows = [];
    private FileTreeSelection? _selection;
    private List<FileTreeNode> _files = [];
    private string _commitSha = string.Empty;
    private bool _treeTruncated;
    private FileTreeRowViewModel? _selectedRow;

    public RemoteFileTreeViewModel(FileTreeBuilder builder, int maxSelectedFiles, TimeProvider time)
    {
        _builder = builder;
        _maxSelectedFiles = maxSelectedFiles;
        _time = time;
        ProceedHint = RemoteSourceStrings.SelectFileHint;
    }

    public RangeObservableCollection<FileTreeRowViewModel> VisibleRows { get; } = [];

    public FileTreeRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set => SetProperty(ref _selectedRow, value);
    }

    public bool ShowEmpty => IsLoaded && (_selection?.Root.Total.Files ?? 0) == 0;

    public bool ShowNoMatch => IsSearchActive && VisibleRows.Count == 0;

    public string NoMatchText => RemoteSourceStrings.NoFileMatch(_activeQuery);

    public string SelectAllLabel => IsSearchActive ? RemoteSourceStrings.SelectAllMatching : RemoteSourceStrings.SelectAll;

    public string SelectAllName => IsSearchActive ? RemoteSourceStrings.SelectAllMatchingName : RemoteSourceStrings.SelectAllName;

    public string ClearLabel => IsSearchActive ? RemoteSourceStrings.ClearMatching : RemoteSourceStrings.ClearSelection;

    public string ClearName => IsSearchActive ? RemoteSourceStrings.ClearMatchingName : RemoteSourceStrings.ClearSelectionName;

    [ObservableProperty]
    public partial bool IsLoaded { get; private set; }

    [ObservableProperty]
    public partial string SummaryText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ProceedHint { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool CanProceed { get; private set; }

    [ObservableProperty]
    public partial bool IsOverLimit { get; private set; }

    [ObservableProperty]
    public partial string HiddenExcludedNotice { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string TruncatedNotice { get; private set; } = string.Empty;

    public void Load(RemoteTree tree)
    {
        var build = _builder.Build(tree);
        Reset();
        _selection = new FileTreeSelection(build.Root, _maxSelectedFiles);
        _files = EnumerateFiles(build.Root);
        _commitSha = tree.CommitSha;
        _treeTruncated = tree.Truncated;
        HiddenExcludedNotice = build.HiddenExcluded > 0 ? RemoteSourceStrings.HiddenExcludedNotice(build.HiddenExcluded) : string.Empty;
        TruncatedNotice = tree.Truncated ? RemoteSourceStrings.TruncatedTreeNotice : string.Empty;
        IsLoaded = true;
        Rebuild(null);
        UpdateSummary();
    }

    public FileSelectionSummary SelectionSummary => _selection?.Summary() ?? new FileSelectionSummary(0, 0, 0, 0, 0, false);

    public void Unload()
    {
        if (!IsLoaded)
        {
            return;
        }

        Reset();
        Rebuild(null);
        UpdateSummary();
    }

    public RemoteSelection BuildSelection() =>
        new(_commitSha, _selection?.SelectedSupportedEntries() ?? [], _treeTruncated);

    public void ToggleCheck(FileTreeRowViewModel row)
    {
        if (_selection is null)
        {
            return;
        }

        _selection.SetChecked(row.Node, row.Node.IsChecked != true);
        RefreshAfterCheck(row);
        UpdateSummary();
    }

    public void ToggleExpand(FileTreeRowViewModel row)
    {
        if (!row.IsFolder)
        {
            return;
        }

        row.IsExpanded = !row.IsExpanded;
        Rebuild(row);
    }

    public void Activate(FileTreeRowViewModel row)
    {
        if (row.IsFolder)
        {
            ToggleExpand(row);
            return;
        }

        ToggleCheck(row);
    }

    public void ExpandOrDescend(FileTreeRowViewModel row)
    {
        if (!row.IsFolder)
        {
            return;
        }

        if (!row.IsExpanded)
        {
            ToggleExpand(row);
            return;
        }

        var next = VisibleRows.IndexOf(row) + 1;
        if (next > 0 && next < VisibleRows.Count && VisibleRows[next].Node.Parent == row.Node)
        {
            Focus(VisibleRows[next]);
        }
    }

    public void CollapseOrAscend(FileTreeRowViewModel row)
    {
        if (row.IsFolder && row.IsExpanded)
        {
            ToggleExpand(row);
            return;
        }

        var parent = row.Node.Parent;
        if (parent?.Parent is not null && _rows.TryGetValue(parent, out var parentRow) && VisibleRows.Contains(parentRow))
        {
            Focus(parentRow);
        }
    }

    public void Dispose() => CancelDebounce();

    [RelayCommand]
    private void SelectAll()
    {
        _selection?.SelectAll(SearchScope());
        RefreshAllRows();
        UpdateSummary();
    }

    [RelayCommand]
    private void Clear()
    {
        _selection?.Clear(SearchScope());
        RefreshAllRows();
        UpdateSummary();
    }

    [RelayCommand]
    private void OnlySupported()
    {
        _selection?.OnlySupported();
        RefreshAllRows();
        UpdateSummary();
    }

    private void Reset()
    {
        _rows.Clear();
        _files = [];
        _selection = null;
        _selectedRow = null;
        _visible = null;
        _matches = null;
        _activeQuery = string.Empty;
        SearchText = string.Empty;
        CancelDebounce();
        IsLoaded = false;
        HiddenExcludedNotice = string.Empty;
        TruncatedNotice = string.Empty;
    }

    private void Focus(FileTreeRowViewModel? row)
    {
        _selectedRow = row;
        OnPropertyChanged(nameof(SelectedRow));
    }

    private void Rebuild(FileTreeRowViewModel? focus)
    {
        var keep = focus ?? _selectedRow;
        var rows = new List<FileTreeRowViewModel>();
        if (_selection is not null)
        {
            AppendChildren(_selection.Root, rows);
        }

        VisibleRows.ReplaceAll(rows);
        Focus(keep is not null && rows.Contains(keep) ? keep : null);
        NotifyViewState();
    }

    private void AppendChildren(FileTreeNode parent, List<FileTreeRowViewModel> rows)
    {
        foreach (var child in parent.Children.Where(IsShown))
        {
            var row = RowFor(child);
            rows.Add(row);
            if (child.IsFolder && row.IsExpanded)
            {
                AppendChildren(child, rows);
            }
        }
    }

    private bool IsShown(FileTreeNode node) => _visible is null || _visible.Contains(node);

    private FileTreeRowViewModel RowFor(FileTreeNode node)
    {
        if (!_rows.TryGetValue(node, out var row))
        {
            row = new FileTreeRowViewModel(node, DepthOf(node), ToggleExpand, ToggleCheck);
            _rows[node] = row;
        }

        return row;
    }

    private static int DepthOf(FileTreeNode node)
    {
        var depth = 0;
        for (var parent = node.Parent; parent?.Parent is not null; parent = parent.Parent)
        {
            depth++;
        }

        return depth;
    }

    private void RefreshAfterCheck(FileTreeRowViewModel row)
    {
        if (row.IsFolder)
        {
            RefreshAllRows();
            return;
        }

        row.Refresh();
        for (var parent = row.Node.Parent; parent is not null; parent = parent.Parent)
        {
            if (_rows.TryGetValue(parent, out var parentRow))
            {
                parentRow.Refresh();
            }
        }
    }

    private void RefreshAllRows()
    {
        foreach (var row in VisibleRows)
        {
            row.Refresh();
        }
    }

    private void UpdateSummary()
    {
        var summary = SelectionSummary;
        SummaryText = summary.SelectedFiles == 0
            ? RemoteSourceStrings.NoFilesSelected
            : RemoteSourceStrings.FilesSummary(
                summary.SupportedSelected,
                RemoteSizeFormatter.Format(summary.SelectedBytes),
                summary.SkippedUnsupported,
                summary.SkippedTooLarge);
        IsOverLimit = summary.OverLimit;
        CanProceed = summary.SupportedSelected >= 1 && !summary.OverLimit;
        ProceedHint = CanProceed ? string.Empty : HintFor(summary);
    }

    private string HintFor(FileSelectionSummary summary) => summary.OverLimit
        ? RemoteSourceStrings.OverLimitHint(_maxSelectedFiles)
        : RemoteSourceStrings.SelectFileHint;

    private void NotifyViewState()
    {
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(ShowNoMatch));
        OnPropertyChanged(nameof(NoMatchText));
        OnPropertyChanged(nameof(IsSearchActive));
        OnPropertyChanged(nameof(SelectAllLabel));
        OnPropertyChanged(nameof(SelectAllName));
        OnPropertyChanged(nameof(ClearLabel));
        OnPropertyChanged(nameof(ClearName));
    }

    private static List<FileTreeNode> EnumerateFiles(FileTreeNode root)
    {
        var files = new List<FileTreeNode>(root.Total.Files);
        var pending = new Stack<FileTreeNode>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (!node.IsFolder)
            {
                files.Add(node);
                continue;
            }

            foreach (var child in node.Children)
            {
                pending.Push(child);
            }
        }

        return files;
    }
}
