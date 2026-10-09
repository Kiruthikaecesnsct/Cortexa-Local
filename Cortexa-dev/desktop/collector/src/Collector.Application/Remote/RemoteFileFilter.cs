using Collector.Application.Extraction;
using Collector.Domain.Remote;
using Microsoft.Extensions.Options;

namespace Collector.Application.Remote;

public sealed class RemoteFileFilter(IOptions<RemoteFetchOptions> options)
{
    private static readonly char[] Separators = ['/', '\\'];

    public bool Keep(RemoteTreeEntry entry) => Classify(entry) == RemoteEntryVerdict.Supported;

    public RemoteEntryVerdict Classify(RemoteTreeEntry entry)
    {
        if (HasExcludedSegment(entry.Path))
        {
            return RemoteEntryVerdict.ExcludedFolder;
        }

        if (IsOversized(entry))
        {
            return RemoteEntryVerdict.TooLarge;
        }

        return IsSupportedType(entry.Path) ? RemoteEntryVerdict.Supported : RemoteEntryVerdict.Unsupported;
    }

    private static bool IsOversized(RemoteTreeEntry entry) =>
        entry.SizeBytes is { } size && size > FileContentGuard.MaxFileBytes;

    private bool IsSupportedType(string path) => FileClassifier.Classify(path) switch
    {
        FileClassification.Text => HasTextExtension(path),
        _ => true,
    };

    private bool HasTextExtension(string path)
    {
        var extension = Path.GetExtension(path);
        return options.Value.TextExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private bool HasExcludedSegment(string path)
    {
        var excluded = options.Value.ExcludedDirectories;
        var segments = path.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        return segments.Take(Math.Max(segments.Length - 1, 0))
            .Any(segment => excluded.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }
}
