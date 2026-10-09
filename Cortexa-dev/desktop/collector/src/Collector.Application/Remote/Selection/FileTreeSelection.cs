using Collector.Domain.Remote;

namespace Collector.Application.Remote.Selection;

public sealed class FileTreeSelection(FileTreeNode root, int maxSelectedFiles)
{
    public FileTreeNode Root => root;

    public void SetChecked(FileTreeNode node, bool isChecked)
    {
        var before = node.Selected;
        var after = isChecked ? node.Total : FileTreeCounts.Zero;
        if (before == after)
        {
            return;
        }

        ApplyToSubtree(node, isChecked);
        var delta = after - before;
        for (var ancestor = node.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            ancestor.Selected += delta;
        }
    }

    public void SelectAll(IEnumerable<FileTreeNode>? scope = null) => SetScope(scope, true);

    public void Clear(IEnumerable<FileTreeNode>? scope = null) => SetScope(scope, false);

    public void OnlySupported()
    {
        Clear();
        foreach (var file in EnumerateFiles(root).Where(file => file.Verdict == RemoteEntryVerdict.Supported))
        {
            SetChecked(file, true);
        }
    }

    public FileSelectionSummary Summary()
    {
        var selected = root.Selected;
        return new FileSelectionSummary(
            selected.Files,
            selected.SupportedBytes,
            selected.Unsupported,
            selected.TooLarge,
            selected.Supported,
            selected.Supported > maxSelectedFiles);
    }

    public IReadOnlyList<RemoteTreeEntry> SelectedSupportedEntries()
    {
        var entries = new List<RemoteTreeEntry>(root.Selected.Supported);
        var pending = new Stack<FileTreeNode>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            CollectSelected(pending.Pop(), entries, pending);
        }

        entries.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
        return entries;
    }

    private static void CollectSelected(FileTreeNode node, List<RemoteTreeEntry> entries, Stack<FileTreeNode> pending)
    {
        if (!node.IsFolder)
        {
            AddIfSupported(node, entries);
            return;
        }

        foreach (var child in node.UnsortedChildren.Where(child => child.Selected.Files > 0))
        {
            pending.Push(child);
        }
    }

    private static void AddIfSupported(FileTreeNode file, List<RemoteTreeEntry> entries)
    {
        if (file.Verdict == RemoteEntryVerdict.Supported && file.Entry is { } entry)
        {
            entries.Add(entry);
        }
    }

    private static IEnumerable<FileTreeNode> EnumerateFiles(FileTreeNode start)
    {
        var pending = new Stack<FileTreeNode>();
        pending.Push(start);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (!node.IsFolder)
            {
                yield return node;
                continue;
            }

            foreach (var child in node.UnsortedChildren)
            {
                pending.Push(child);
            }
        }
    }

    private static void ApplyToSubtree(FileTreeNode node, bool isChecked)
    {
        var pending = new Stack<FileTreeNode>();
        pending.Push(node);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            current.Selected = isChecked ? current.Total : FileTreeCounts.Zero;
            foreach (var child in current.UnsortedChildren)
            {
                pending.Push(child);
            }
        }
    }

    private void SetScope(IEnumerable<FileTreeNode>? scope, bool isChecked)
    {
        foreach (var node in scope ?? [root])
        {
            SetChecked(node, isChecked);
        }
    }
}
