using Collector.Application.Remote.Selection;
using Collector.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Collector.Presentation.ViewModels;

public sealed partial class RemoteFileTreeViewModel
{
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(200);

    private ITimer? _debounce;
    private string _activeQuery = string.Empty;
    private HashSet<FileTreeNode>? _visible;
    private List<FileTreeNode>? _matches;

    public bool IsSearchActive => _activeQuery.Length > 0;

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    partial void OnSearchTextChanged(string value)
    {
        CancelDebounce();
        _debounce = _time.CreateTimer(_ => UiThread.Post(ApplySearch), null, SearchDelay, Timeout.InfiniteTimeSpan);
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        ApplySearch();
    }

    private void CancelDebounce()
    {
        _debounce?.Dispose();
        _debounce = null;
    }

    private IEnumerable<FileTreeNode>? SearchScope() => IsSearchActive ? _matches : null;

    private void ApplySearch()
    {
        CancelDebounce();
        var query = SearchText.Trim();
        if (_selection is null || query == _activeQuery)
        {
            return;
        }

        _activeQuery = query;
        _matches = query.Length == 0 ? null : FindMatches(query);
        _visible = _matches is null ? null : BuildVisibleSet(_matches);
        Rebuild(null);
    }

    private List<FileTreeNode> FindMatches(string query) =>
        [.. _files.Where(file => file.Path.Contains(query, StringComparison.OrdinalIgnoreCase))];

    private HashSet<FileTreeNode> BuildVisibleSet(List<FileTreeNode> matches)
    {
        var visible = new HashSet<FileTreeNode>(matches);
        foreach (var match in matches)
        {
            ExpandAncestors(match, visible);
        }

        return visible;
    }

    private void ExpandAncestors(FileTreeNode match, HashSet<FileTreeNode> visible)
    {
        for (var parent = match.Parent; parent?.Parent is not null; parent = parent.Parent)
        {
            RowFor(parent).IsExpanded = true;
            if (!visible.Add(parent))
            {
                return;
            }
        }
    }
}
