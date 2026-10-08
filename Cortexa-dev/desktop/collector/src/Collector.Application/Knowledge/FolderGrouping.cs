using Collector.Domain.Enums;

namespace Collector.Application.Knowledge;

public sealed record FolderFile(string FilePath, IReadOnlyList<ExtractedKnowledgeItem> Items)
{
    public ExtractedKnowledgeItem RepresentativeItem => Items[0];
}

public sealed record FolderGroup(string FolderPath, IReadOnlyList<FolderFile> Files)
{
    public FolderFile RepresentativeFile => Files[0];
}

public static class FolderGrouping
{
    public const int MinFilesPerFolder = 3;

    public static IReadOnlyList<FolderGroup> Group(IReadOnlyList<ExtractedKnowledgeItem> items)
    {
        var groups = new List<FolderGroup>();
        foreach (var folder in OrderedFolders(items))
        {
            var files = DistinctFilesInOrder(folder.Items);
            if (files.Count >= MinFilesPerFolder)
            {
                groups.Add(new FolderGroup(folder.FolderPath, files));
            }
        }

        return groups;
    }

    public static string? ParentFolder(string filePath)
    {
        var normalized = filePath.Replace('\\', '/');
        var index = normalized.LastIndexOf('/');
        return index <= 0 ? null : normalized[..index];
    }

    private static IEnumerable<(string FolderPath, List<ExtractedKnowledgeItem> Items)> OrderedFolders(
        IReadOnlyList<ExtractedKnowledgeItem> items)
    {
        var byFolder = new Dictionary<string, List<ExtractedKnowledgeItem>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var item in items)
        {
            if (item.UnitKind != UnitKind.File || item.Source.FilePath is not { Length: > 0 } filePath)
            {
                continue;
            }

            var folder = ParentFolder(filePath);
            if (folder is null)
            {
                continue;
            }

            if (!byFolder.TryGetValue(folder, out var list))
            {
                list = [];
                byFolder[folder] = list;
                order.Add(folder);
            }

            list.Add(item);
        }

        return order.Select(folder => (folder, byFolder[folder]));
    }

    private static List<FolderFile> DistinctFilesInOrder(List<ExtractedKnowledgeItem> items)
    {
        var byFile = new Dictionary<string, List<ExtractedKnowledgeItem>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var item in items)
        {
            var filePath = item.Source.FilePath!;
            if (!byFile.TryGetValue(filePath, out var list))
            {
                list = [];
                byFile[filePath] = list;
                order.Add(filePath);
            }

            list.Add(item);
        }

        return [.. order.Select(filePath => new FolderFile(filePath, byFile[filePath]))];
    }
}
