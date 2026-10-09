using Collector.Domain.Remote;

namespace Collector.Application.Remote.Selection;

public sealed class FileTreeNode
{
    private readonly List<FileTreeNode> _children = [];
    private IReadOnlyList<FileTreeNode>? _sorted;

    private FileTreeNode(string name, string path, FileTreeNode? parent)
    {
        Name = name;
        Path = path;
        Parent = parent;
    }

    public string Name { get; }

    public string Path { get; }

    public FileTreeNode? Parent { get; }

    public bool IsFolder { get; private init; }

    public RemoteEntryVerdict Verdict { get; private init; }

    public long? SizeBytes { get; private init; }

    public RemoteTreeEntry? Entry { get; private init; }

    public FileTreeCounts Total { get; private set; }

    public FileTreeCounts Selected { get; internal set; }

    public IReadOnlyList<FileTreeNode> Children => _sorted ??= SortChildren();

    public bool? IsChecked => Selected.Files switch
    {
        0 => false,
        _ when Selected.Files == Total.Files => true,
        _ => null,
    };

    internal IReadOnlyList<FileTreeNode> UnsortedChildren => _children;

    internal static FileTreeNode CreateRoot() => new(string.Empty, string.Empty, null) { IsFolder = true };

    internal static FileTreeNode CreateFolder(string name, string path, FileTreeNode parent) =>
        new(name, path, parent) { IsFolder = true };

    internal static FileTreeNode CreateFile(
        string name,
        RemoteTreeEntry entry,
        RemoteEntryVerdict verdict,
        FileTreeNode parent) =>
        new(name, entry.Path, parent)
        {
            Verdict = verdict,
            SizeBytes = entry.SizeBytes,
            Entry = entry,
            Total = FileTreeCounts.ForFile(verdict, entry.SizeBytes),
        };

    internal void AddChild(FileTreeNode child)
    {
        _children.Add(child);
        _sorted = null;
    }

    internal void AddToTotals(FileTreeCounts counts) => Total += counts;

    private IReadOnlyList<FileTreeNode> SortChildren() =>
        [.. _children
            .OrderByDescending(child => child.IsFolder)
            .ThenBy(child => child.Name, StringComparer.OrdinalIgnoreCase)];
}
