using Collector.Domain.Remote;

namespace Collector.Application.Remote.Selection;

public sealed class FileTreeBuilder(RemoteFileFilter filter)
{
    private static readonly char[] Separators = ['/', '\\'];

    public FileTreeBuild Build(RemoteTree tree)
    {
        var root = FileTreeNode.CreateRoot();
        var folders = new Dictionary<string, FileTreeNode>(StringComparer.Ordinal) { [string.Empty] = root };
        var hidden = 0;
        foreach (var entry in tree.Entries)
        {
            var verdict = filter.Classify(entry);
            if (verdict == RemoteEntryVerdict.ExcludedFolder)
            {
                hidden++;
                continue;
            }

            AddFile(entry, verdict, folders);
        }

        return new FileTreeBuild(root, hidden);
    }

    private static void AddFile(
        RemoteTreeEntry entry,
        RemoteEntryVerdict verdict,
        Dictionary<string, FileTreeNode> folders)
    {
        var segments = entry.Path.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return;
        }

        var parent = EnsureFolder(segments.AsSpan(0, segments.Length - 1), folders);
        var file = FileTreeNode.CreateFile(segments[^1], entry, verdict, parent);
        parent.AddChild(file);
        AddToAncestors(file);
    }

    private static FileTreeNode EnsureFolder(ReadOnlySpan<string> segments, Dictionary<string, FileTreeNode> folders)
    {
        var current = folders[string.Empty];
        var path = string.Empty;
        foreach (var segment in segments)
        {
            path = path.Length == 0 ? segment : $"{path}/{segment}";
            if (!folders.TryGetValue(path, out var folder))
            {
                folder = FileTreeNode.CreateFolder(segment, path, current);
                current.AddChild(folder);
                folders[path] = folder;
            }

            current = folder;
        }

        return current;
    }

    private static void AddToAncestors(FileTreeNode file)
    {
        for (var ancestor = file.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            ancestor.AddToTotals(file.Total);
        }
    }
}
